namespace SchulerPark.Tests.Services;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using SchulerPark.Tests.Integration;
using SchulerPark.Tests.TestSupport;

/// <summary>Token service stand-in: issues predictable tokens and records revocations.</summary>
public sealed class FakeTokenService : ITokenService
{
    public Dictionary<string, User> ValidRefreshTokens { get; } = [];
    public List<string> Revoked { get; } = [];
    public List<Guid> RevokedAllFor { get; } = [];
    public int Issued { get; private set; }

    public string GenerateAccessToken(User user) => $"access-{user.Id:N}-{++Issued}";
    public Task<string> GenerateRefreshTokenAsync(User user, string? ipAddress)
    {
        var token = $"refresh-{user.Id:N}-{Issued}";
        ValidRefreshTokens[token] = user;
        return Task.FromResult(token);
    }
    public Task<User?> ValidateRefreshTokenAsync(string token) =>
        Task.FromResult(ValidRefreshTokens.TryGetValue(token, out var u) ? u : null);
    public Task RevokeRefreshTokenAsync(string token)
    {
        Revoked.Add(token);
        ValidRefreshTokens.Remove(token);
        return Task.CompletedTask;
    }
    public Task RevokeAllUserTokensAsync(Guid userId)
    {
        RevokedAllFor.Add(userId);
        return Task.CompletedTask;
    }
}

public sealed class RecordingAdminNotifier : IAdminNotifier
{
    public List<User> PendingUsers { get; } = [];
    public List<LotteryRunSummary> LotteryFailures { get; } = [];

    public Task PendingUserAsync(User pendingUser) { PendingUsers.Add(pendingUser); return Task.CompletedTask; }
    public Task LotteryFailedAsync(LotteryRunSummary summary, int attempt) { LotteryFailures.Add(summary); return Task.CompletedTask; }
    public Task LotteryWatchdogInterventionAsync(DateOnly targetDate, IReadOnlyList<string> healedSlots, int sweptPending) => Task.CompletedTask;
    public Task FeedbackReceivedAsync(FeedbackReport report) => Task.CompletedTask;
}

public class AuthServiceTests
{
    private const string Password = "Correct-Horse-42!";

    private readonly AppDbContext _db = TestDb.InMemory();
    private readonly FakeTokenService _tokens = new();
    private readonly CapturingEmailService _email = new();
    private readonly RecordingAdminNotifier _admins = new();
    private readonly PasswordHasher<User> _hasher = new();

    private AuthService NewService(string autoApproved = "partner.com; Other.org", string sso = "andritz.com") => new(
        _db, _tokens, _hasher,
        new FakeAzureAdTokenValidator(Options.Create(new AzureAdSettings()), NullLogger<AzureAdTokenValidator>.Instance),
        _email, _admins,
        Options.Create(new AppSettings { BaseUrl = "https://park.test/" }),
        Options.Create(new RegistrationSettings { AutoApprovedDomains = autoApproved, SsoDomains = sso }),
        NullLogger<AuthService>.Instance);

    private User SeedLocalUser(string email, ApprovalStatus status = ApprovalStatus.Approved, bool verified = true)
    {
        var user = TestData.User(u =>
        {
            u.Email = email;
            u.EmailVerified = verified;
            u.ApprovalStatus = status;
        });
        user.PasswordHash = _hasher.HashPassword(user, Password);
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private User Stored(string email) => _db.Users.AsNoTracking().Single(u => u.Email == email);

    // ---- register ----------------------------------------------------------------

    [Fact]
    public async Task RegisterAsync_ExternalDomain_CreatesPendingUnverifiedUserWithIdentity()
    {
        await NewService().RegisterAsync("  New.Person@External.DE ", "  Erika ", " Muster ", "  ACME GmbH ", Password, "en");

        var user = Stored("new.person@external.de");
        user.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
        user.EmailVerified.Should().BeFalse();
        user.FirstName.Should().Be("Erika");
        user.LastName.Should().Be("Muster");
        user.DisplayName.Should().Be("Erika Muster");
        user.Company.Should().Be("ACME GmbH");
        user.Role.Should().Be(UserRole.User);
        user.PreferredLanguage.Should().Be("en");
        _hasher.VerifyHashedPassword(user, user.PasswordHash!, Password).Should().NotBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public async Task RegisterAsync_SendsVerificationMailAndDoesNotLogIn()
    {
        await NewService().RegisterAsync("a@external.de", "A", "B", "C", Password);

        var link = _email.VerificationLinkFor("a@external.de");
        link.Should().StartWith("https://park.test/verify-email?token=");
        _tokens.Issued.Should().Be(0);
        _admins.PendingUsers.Should().BeEmpty("admins are told only after verification (B07)");
        Stored("a@external.de").EmailVerificationTokenHash.Should().NotBeNullOrEmpty()
            .And.NotBe(_email.VerificationTokenFor("a@external.de"), "only the hash is stored");
    }

    [Theory]
    [InlineData("x@partner.com")]
    [InlineData("x@PARTNER.COM")]
    [InlineData("x@other.org")]
    public async Task RegisterAsync_AutoApprovedDomain_IsApproved(string email)
    {
        await NewService().RegisterAsync(email, "A", "B", "Partner Inc", Password);

        var user = Stored(email.ToLowerInvariant());
        user.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        user.Company.Should().Be("Partner Inc");
    }

    [Theory]
    [InlineData("x@sub.partner.com")]
    [InlineData("partner.com@evil.de")]
    public async Task RegisterAsync_DomainMatchIsExactOnPartAfterLastAt(string email)
    {
        await NewService().RegisterAsync(email, "A", "B", "C", Password);

        Stored(email).ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task RegisterAsync_SsoDomain_ThrowsAndCreatesNothing()
    {
        await NewService().Invoking(s => s.RegisterAsync("x@Andritz.com", "A", "B", "C", Password))
            .Should().ThrowAsync<SsoOnlyDomainException>();

        _db.Users.Should().BeEmpty();
        _email.VerificationLinkFor("x@andritz.com").Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_ExistingVerifiedEmail_ChangesNothingAndSendsNothing()
    {
        var existing = SeedLocalUser("taken@external.de");
        var hashBefore = existing.PasswordHash;

        await NewService().RegisterAsync("Taken@External.de", "Mallory", "Evil", "Evil Corp", "Other-Password-1!");

        var user = Stored("taken@external.de");
        user.DisplayName.Should().Be("Test User");
        user.Company.Should().Be("Schuler AG");
        user.PasswordHash.Should().Be(hashBefore);
        _db.Users.Should().ContainSingle();
        _email.VerificationLinkFor("taken@external.de").Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_ExistingUnverifiedEmail_ResendsVerificationWithoutUpdatingIdentity()
    {
        SeedLocalUser("half@external.de", ApprovalStatus.Pending, verified: false);

        await NewService().RegisterAsync("half@external.de", "Mallory", "Evil", "Evil Corp", "Other-Password-1!");

        _email.VerificationLinkFor("half@external.de").Should().NotBeNull();
        var user = Stored("half@external.de");
        user.DisplayName.Should().Be("Test User");
        user.Company.Should().Be("Schuler AG");
        _hasher.VerifyHashedPassword(user, user.PasswordHash!, Password).Should().NotBe(PasswordVerificationResult.Failed);
    }

    // ---- verify email --------------------------------------------------------------

    [Fact]
    public async Task VerifyEmailAsync_ValidTokenPendingUser_VerifiesAndNotifiesAdmins()
    {
        var service = NewService();
        await service.RegisterAsync("p@external.de", "Pia", "Pending", "External GmbH", Password);
        var token = _email.VerificationTokenFor("p@external.de")!;

        (await service.VerifyEmailAsync(token)).Should().BeTrue();

        var user = Stored("p@external.de");
        user.EmailVerified.Should().BeTrue();
        user.EmailVerificationTokenHash.Should().BeNull();
        user.EmailVerificationTokenExpiresAt.Should().BeNull();
        _admins.PendingUsers.Should().ContainSingle().Which.Email.Should().Be("p@external.de");
    }

    [Fact]
    public async Task VerifyEmailAsync_ApprovedUser_DoesNotNotifyAdmins()
    {
        var service = NewService();
        await service.RegisterAsync("a@partner.com", "A", "B", "Partner", Password);

        (await service.VerifyEmailAsync(_email.VerificationTokenFor("a@partner.com")!)).Should().BeTrue();

        _admins.PendingUsers.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyEmailAsync_TokenUsedTwice_SecondTimeFails()
    {
        var service = NewService();
        await service.RegisterAsync("p@external.de", "A", "B", "C", Password);
        var token = _email.VerificationTokenFor("p@external.de")!;

        (await service.VerifyEmailAsync(token)).Should().BeTrue();
        (await service.VerifyEmailAsync(token)).Should().BeFalse();
        _admins.PendingUsers.Should().ContainSingle();
    }

    [Fact]
    public async Task VerifyEmailAsync_UnknownToken_ReturnsFalse()
    {
        (await NewService().VerifyEmailAsync("not-a-real-token")).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyEmailAsync_ExpiredToken_ReturnsFalseAndStaysUnverified()
    {
        var service = NewService();
        await service.RegisterAsync("late@external.de", "A", "B", "C", Password);
        var user = _db.Users.Single(u => u.Email == "late@external.de");
        user.EmailVerificationTokenExpiresAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.SaveChanges();

        (await service.VerifyEmailAsync(_email.VerificationTokenFor("late@external.de")!)).Should().BeFalse();

        Stored("late@external.de").EmailVerified.Should().BeFalse();
        _admins.PendingUsers.Should().BeEmpty();
    }

    // ---- login ---------------------------------------------------------------------

    [Fact]
    public async Task LoginAsync_ApprovedVerifiedUser_ReturnsTokensAndResetsFailures()
    {
        var user = SeedLocalUser("ok@schuler.de");
        user.AccessFailedCount = 3;
        _db.SaveChanges();

        var (loggedIn, access, refresh) = await NewService().LoginAsync("  OK@Schuler.de ", Password, "10.0.0.1");

        loggedIn.Id.Should().Be(user.Id);
        access.Should().NotBeNullOrEmpty();
        refresh.Should().NotBeNullOrEmpty();
        Stored("ok@schuler.de").AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ThrowsGenericUnauthorized()
    {
        await NewService().Invoking(s => s.LoginAsync("nobody@schuler.de", Password, null))
            .Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Invalid email or password.");
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedAndCountsFailure()
    {
        SeedLocalUser("ok@schuler.de");

        await NewService().Invoking(s => s.LoginAsync("ok@schuler.de", "wrong", null))
            .Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Invalid email or password.");

        Stored("ok@schuler.de").AccessFailedCount.Should().Be(1);
        _tokens.Issued.Should().Be(0);
    }

    [Fact]
    public async Task LoginAsync_FifthWrongPassword_LocksAccountAndMailsOwner()
    {
        SeedLocalUser("ok@schuler.de");
        var service = NewService();

        for (var i = 0; i < 5; i++)
            await service.Invoking(s => s.LoginAsync("ok@schuler.de", "wrong", null))
                .Should().ThrowAsync<UnauthorizedAccessException>();

        var user = Stored("ok@schuler.de");
        user.AccessFailedCount.Should().Be(5);
        user.LockoutEnd.Should().NotBeNull();
        _email.AccountMails.Should().ContainSingle().Which.Should().Be(("AccountLocked", "ok@schuler.de"));
    }

    [Fact]
    public async Task LoginAsync_LockedWithCorrectPassword_ThrowsAccountLocked()
    {
        var user = SeedLocalUser("ok@schuler.de");
        user.AccessFailedCount = 5;
        user.LockoutEnd = DateTime.UtcNow.AddHours(1);
        _db.SaveChanges();

        await NewService().Invoking(s => s.LoginAsync("ok@schuler.de", Password, null))
            .Should().ThrowAsync<AccountLockedException>();
        _tokens.Issued.Should().Be(0);
    }

    [Fact]
    public async Task LoginAsync_UnverifiedCorrectPassword_ThrowsEmailNotVerified()
    {
        SeedLocalUser("new@external.de", ApprovalStatus.Pending, verified: false);

        await NewService().Invoking(s => s.LoginAsync("new@external.de", Password, null))
            .Should().ThrowAsync<EmailNotVerifiedException>();
    }

    [Fact]
    public async Task LoginAsync_PendingVerifiedCorrectPassword_ThrowsPendingApproval()
    {
        SeedLocalUser("p@external.de", ApprovalStatus.Pending);

        await NewService().Invoking(s => s.LoginAsync("p@external.de", Password, null))
            .Should().ThrowAsync<AccountPendingApprovalException>();
        _tokens.Issued.Should().Be(0);
    }

    [Fact]
    public async Task LoginAsync_PendingWrongPassword_DoesNotRevealPendingState()
    {
        SeedLocalUser("p@external.de", ApprovalStatus.Pending);

        await NewService().Invoking(s => s.LoginAsync("p@external.de", "wrong", null))
            .Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Invalid email or password.");
    }

    [Fact]
    public async Task LoginAsync_RejectedCorrectPassword_LooksLikeBadCredentials()
    {
        SeedLocalUser("r@external.de", ApprovalStatus.Rejected);

        var ex = await NewService().Invoking(s => s.LoginAsync("r@external.de", Password, null))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        ex.Which.GetType().Should().Be(typeof(UnauthorizedAccessException));
        ex.Which.Message.Should().Be("Invalid email or password.");
        _tokens.Issued.Should().Be(0);
    }

    [Fact]
    public async Task LoginAsync_DeletedUser_ThrowsGenericUnauthorized()
    {
        var user = SeedLocalUser("gone@schuler.de");
        user.DeletedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.SaveChanges();

        await NewService().Invoking(s => s.LoginAsync("gone@schuler.de", Password, null))
            .Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Invalid email or password.");
    }

    // ---- refresh -------------------------------------------------------------------

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesAndIssuesNewPair()
    {
        var user = SeedLocalUser("ok@schuler.de");
        var service = NewService();
        var (_, _, oldRefresh) = await service.LoginAsync("ok@schuler.de", Password, null);

        var (refreshedUser, access, newRefresh) = await service.RefreshAsync(oldRefresh, "10.0.0.2");

        refreshedUser.Id.Should().Be(user.Id);
        access.Should().NotBeNullOrEmpty();
        newRefresh.Should().NotBe(oldRefresh);
        _tokens.Revoked.Should().ContainSingle().Which.Should().Be(oldRefresh);
        await service.Invoking(s => s.RefreshAsync(oldRefresh, null)).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ThrowsUnauthorizedWithoutRevoking()
    {
        await NewService().Invoking(s => s.RefreshAsync("bogus", null)).Should().ThrowAsync<UnauthorizedAccessException>();
        _tokens.Revoked.Should().BeEmpty();
    }

    // ---- Azure AD SSO ----------------------------------------------------------------

    [Fact]
    public async Task LoginWithAzureAdAsync_NewUser_IsApprovedVerifiedAndHasNoCompany()
    {
        var (user, access, _) = await NewService().LoginWithAzureAdAsync(
            FakeAzureAdTokenValidator.Token("oid-1", "Sso.User@Andritz.com", "Sso User"), null);

        user.Email.Should().Be("sso.user@andritz.com");
        user.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        user.EmailVerified.Should().BeTrue();
        user.Company.Should().BeNull();
        user.AzureAdObjectId.Should().Be("oid-1");
        access.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoginWithAzureAdAsync_LinksPendingLocalAccount_AndApprovesIt()
    {
        var local = SeedLocalUser("p@external.de", ApprovalStatus.Pending);

        var (user, _, _) = await NewService().LoginWithAzureAdAsync(
            FakeAzureAdTokenValidator.Token("oid-2", "p@external.de"), null);

        user.Id.Should().Be(local.Id);
        var stored = Stored("p@external.de");
        stored.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        stored.AzureAdObjectId.Should().Be("oid-2");
        stored.PasswordHash.Should().NotBeNull("a verified local account keeps its password");
    }

    [Fact]
    public async Task LoginWithAzureAdAsync_UnverifiedLocalSquatter_LosesCredentials()
    {
        var local = SeedLocalUser("victim@external.de", ApprovalStatus.Pending, verified: false);

        await NewService().LoginWithAzureAdAsync(FakeAzureAdTokenValidator.Token("oid-3", "victim@external.de", "Real Owner"), null);

        var stored = Stored("victim@external.de");
        stored.PasswordHash.Should().BeNull();
        stored.DisplayName.Should().Be("Real Owner");
        stored.EmailVerified.Should().BeTrue();
        _tokens.RevokedAllFor.Should().ContainSingle().Which.Should().Be(local.Id);
    }

    [Fact]
    public async Task LoginWithAzureAdAsync_InvalidToken_ThrowsUnauthorized()
    {
        await NewService().Invoking(s => s.LoginWithAzureAdAsync("garbage", null))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        _db.Users.Should().BeEmpty();
    }
}
