namespace SchulerPark.Infrastructure.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

public class AuthService : IAuthService
{
    private const int VerificationTokenValidHours = 24;
    private const int MaxFailedAttemptsBeforeLockout = 5;
    private static readonly TimeSpan MaxLockout = TimeSpan.FromHours(1);

    // Verified against a fixed dummy password when the email is unknown so that
    // login latency does not reveal whether an account exists. Static + lazy:
    // hashing is expensive and AuthService is scoped (per request).
    private const string TimingEqualizationPassword = "timing-equalization-dummy";
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<User>().HashPassword(new User(), TimingEqualizationPassword));

    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly AzureAdTokenValidator _azureAdValidator;
    private readonly IEmailService _emailService;
    private readonly AppSettings _appSettings;
    private readonly RegistrationSettings _registrationSettings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher,
        AzureAdTokenValidator azureAdValidator,
        IEmailService emailService,
        IOptions<AppSettings> appSettings,
        IOptions<RegistrationSettings> registrationSettings,
        ILogger<AuthService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _azureAdValidator = azureAdValidator;
        _emailService = emailService;
        _appSettings = appSettings.Value;
        _registrationSettings = registrationSettings.Value;
        _logger = logger;
    }

    public async Task RegisterAsync(string email, string displayName, string password, string? preferredLanguage = null)
    {
        email = NormalizeEmail(email);
        var language = Localization.Normalize(preferredLanguage);

        // SSO-mandated domains (e.g. andritz.com) must not create local accounts —
        // checked before anything else so the rule stays purely domain-based.
        if (_registrationSettings.IsSsoDomain(email))
            throw new SsoOnlyDomainException(
                "Accounts with this email domain sign in with Microsoft. Please use the Microsoft sign-in on the login page.");

        var existing = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing != null)
        {
            // No enumeration: respond identically whether or not the email is taken.
            // An unverified account gets a fresh verification email; a verified one
            // is left untouched. Burn a hash like the new-account path below does,
            // so response timing doesn't reveal that the address exists.
            _passwordHasher.VerifyHashedPassword(new User(), DummyPasswordHash.Value, password);
            if (!existing.EmailVerified && existing.DeletedAt == null)
                await IssueVerificationTokenAsync(existing);
            return;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            Role = UserRole.User,
            EmailVerified = false,
            PreferredLanguage = language,
            // Phase 18: external domains need an admin to accept the account.
            // The HTTP response stays identical either way (no enumeration).
            ApprovalStatus = _registrationSettings.IsAutoApprovedDomain(email)
                ? ApprovalStatus.Approved
                : ApprovalStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        _context.Users.Add(user);

        await IssueVerificationTokenAsync(user);
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        var tokenHash = HashToken(token);
        var user = await _context.Users.FirstOrDefaultAsync(u =>
            u.EmailVerificationTokenHash == tokenHash && u.DeletedAt == null);

        if (user == null || user.EmailVerificationTokenExpiresAt < DateTime.UtcNow)
            return false;

        user.EmailVerified = true;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Phase 18: admins are notified about external registrations only once the
        // address is verified — bots that never verify must not generate admin mail.
        if (user.ApprovalStatus == ApprovalStatus.Pending)
            await NotifyAdminsOfPendingUserAsync(user);

        return true;
    }

    private async Task NotifyAdminsOfPendingUserAsync(User pendingUser)
    {
        var admins = await _context.Users
            .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin)
                        && u.DeletedAt == null)
            .Select(u => new { u.Email, u.DisplayName, u.PreferredLanguage })
            .ToListAsync();

        var approvalLink = $"{_appSettings.BaseUrl.TrimEnd('/')}/admin/approvals";
        foreach (var admin in admins)
        {
            await _emailService.SendApprovalRequestToAdminAsync(
                admin.Email, admin.DisplayName, pendingUser.Email, pendingUser.DisplayName, approvalLink, admin.PreferredLanguage);
        }
    }

    public async Task ResendVerificationEmailAsync(string email)
    {
        email = NormalizeEmail(email);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || user.EmailVerified || user.DeletedAt != null)
            return;

        await IssueVerificationTokenAsync(user);
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(
        string email, string password, string? ipAddress)
    {
        email = NormalizeEmail(email);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || user.DeletedAt != null)
        {
            // Equalize timing with the known-user path (H4/M4: no user-enumeration oracle).
            _passwordHasher.VerifyHashedPassword(new User(), DummyPasswordHash.Value, password);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid email or password.");

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (result == PasswordVerificationResult.Failed)
        {
            await RegisterFailedAttemptAsync(user);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (!user.EmailVerified)
            throw new EmailNotVerifiedException("Email address is not verified.");

        // Phase 18 approval gate — deliberately AFTER the password check, so neither
        // state is an enumeration oracle. Rejected is indistinguishable from bad
        // credentials; Pending gets a distinct code (the caller proved ownership).
        if (user.ApprovalStatus == ApprovalStatus.Rejected)
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (user.ApprovalStatus == ApprovalStatus.Pending)
            throw new AccountPendingApprovalException("Account is awaiting admin approval.");

        if (user.AccessFailedCount > 0 || user.LockoutEnd.HasValue)
        {
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
        }

        // Rehash if needed (algorithm upgrade)
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return await GenerateTokensAsync(user, ipAddress);
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> LoginWithAzureAdAsync(
        string idToken, string? ipAddress)
    {
        var userInfo = await _azureAdValidator.ValidateTokenAsync(idToken)
            ?? throw new UnauthorizedAccessException("Invalid Azure AD token.");

        // The immutable `oid` is the identity key. The email claim
        // (preferred_username) is mutable and unverified in Azure AD v2 tokens,
        // so it must never override an oid match.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.AzureAdObjectId == userInfo.ObjectId);

        if (user == null)
        {
            var email = NormalizeEmail(userInfo.Email);
            user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user != null)
            {
                if (user.EmailVerified || string.IsNullOrEmpty(user.PasswordHash))
                {
                    // Safe link: the local account proved ownership of this address.
                    user.AzureAdObjectId = userInfo.ObjectId;
                }
                else
                {
                    // The address is squatted by an unverified local registration.
                    // The Azure identity is authoritative: claim the account and
                    // kill the local credentials so whoever pre-registered it is
                    // locked out (C1 pre-account-takeover).
                    _logger.LogWarning(
                        "Azure AD login claimed unverified local account {UserId}; local password disabled.",
                        user.Id);
                    user.AzureAdObjectId = userInfo.ObjectId;
                    user.PasswordHash = null;
                    user.DisplayName = userInfo.DisplayName;
                    user.EmailVerificationTokenHash = null;
                    user.EmailVerificationTokenExpiresAt = null;
                    await _tokenService.RevokeAllUserTokensAsync(user.Id);
                }

                user.EmailVerified = true;
                // Phase 18: corporate tenant membership is the trust anchor —
                // SSO logins bypass the registration-approval gate.
                user.ApprovalStatus = ApprovalStatus.Approved;
                user.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    DisplayName = userInfo.DisplayName,
                    AzureAdObjectId = userInfo.ObjectId,
                    Role = UserRole.User,
                    EmailVerified = true,
                    // Phase 18: SSO users are auto-approved (tenant trust anchor).
                    ApprovalStatus = ApprovalStatus.Approved,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
            }

            await _context.SaveChangesAsync();
        }

        if (user.DeletedAt != null)
            throw new UnauthorizedAccessException("Account is disabled.");

        return await GenerateTokensAsync(user, ipAddress);
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> RefreshAsync(
        string refreshToken, string? ipAddress)
    {
        var user = await _tokenService.ValidateRefreshTokenAsync(refreshToken)
            ?? throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        // Revoke old token (rotation)
        await _tokenService.RevokeRefreshTokenAsync(refreshToken);

        return await GenerateTokensAsync(user, ipAddress);
    }

    public async Task<User> GetUserAsync(Guid userId)
    {
        return await _context.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");
    }

    private async Task IssueVerificationTokenAsync(User user)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('/', '_')
            .Replace('+', '-')
            .TrimEnd('=');

        user.EmailVerificationTokenHash = HashToken(rawToken);
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(VerificationTokenValidHours);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var baseUrl = _appSettings.BaseUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            _logger.LogWarning("App:BaseUrl is not configured; verification link for {Email} will be relative.", user.Email);

        var link = $"{baseUrl}/verify-email?token={rawToken}";
        await _emailService.SendEmailVerificationAsync(user.Email, user.DisplayName, link, user.PreferredLanguage);
    }

    private async Task RegisterFailedAttemptAsync(User user)
    {
        user.AccessFailedCount++;

        if (user.AccessFailedCount >= MaxFailedAttemptsBeforeLockout)
        {
            // Exponential backoff: 1 min after the 5th failure, doubling per
            // further failure, capped at 1 hour.
            var minutes = Math.Min(
                Math.Pow(2, user.AccessFailedCount - MaxFailedAttemptsBeforeLockout),
                MaxLockout.TotalMinutes);
            user.LockoutEnd = DateTime.UtcNow.AddMinutes(minutes);
            _logger.LogWarning("Account {UserId} locked until {LockoutEnd} after {Count} failed logins.",
                user.Id, user.LockoutEnd, user.AccessFailedCount);
        }

        await _context.SaveChangesAsync();
    }

    private async Task<(User User, string AccessToken, string RefreshToken)> GenerateTokensAsync(
        User user, string? ipAddress)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user, ipAddress);

        return (user, accessToken, refreshToken);
    }

    internal static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
