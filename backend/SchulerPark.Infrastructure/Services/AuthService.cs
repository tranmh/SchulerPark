namespace SchulerPark.Infrastructure.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly AzureAdTokenValidator _azureAdValidator;

    public AuthService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher,
        AzureAdTokenValidator azureAdValidator)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _azureAdValidator = azureAdValidator;
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> RegisterAsync(
        string email, string displayName, string password, string? ipAddress)
    {
        if (await _context.Users.AnyAsync(u => u.Email == email))
            throw new InvalidOperationException("Email is already registered.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return await GenerateTokensAsync(user, ipAddress);
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(
        string email, string password, string? ipAddress)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("Invalid email or password.");

        // Rehash if needed (algorithm upgrade)
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return await GenerateTokensAsync(user, ipAddress);
    }

    public async Task<(User User, string AccessToken, string RefreshToken)> LoginWithAzureAdAsync(
        string idToken, string? ipAddress)
    {
        var userInfo = await _azureAdValidator.ValidateTokenAsync(idToken)
            ?? throw new UnauthorizedAccessException("Invalid Azure AD token.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.AzureAdObjectId == userInfo.ObjectId);

        if (user == null)
        {
            // Also check if a local user with the same email exists and link them
            user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userInfo.Email);

            if (user != null)
            {
                user.AzureAdObjectId = userInfo.ObjectId;
                user.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = userInfo.Email,
                    DisplayName = userInfo.DisplayName,
                    AzureAdObjectId = userInfo.ObjectId,
                    Role = UserRole.User,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
            }

            await _context.SaveChangesAsync();
        }

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

    private async Task<(User User, string AccessToken, string RefreshToken)> GenerateTokensAsync(
        User user, string? ipAddress)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user, ipAddress);

        return (user, accessToken, refreshToken);
    }
}
