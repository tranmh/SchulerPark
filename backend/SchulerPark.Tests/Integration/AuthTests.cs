using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class AuthTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewEmail(string label) => $"{label}-{Guid.NewGuid():N}@schuler.de";

    // ── Registration & verification ──

    [Fact]
    public async Task Register_ReturnsGenericMessage_AndSendsVerificationEmail()
    {
        var email = NewEmail("reg");

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "New User",
            password = "Test1234!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Emails.VerificationTokenFor(email).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsSameGenericResponse()
    {
        var email = NewEmail("dup");
        await AuthTestHelper.RegisterAsync(_client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "Second",
            password = "Other1234!"
        });

        // No 409: the response must not reveal whether the address is taken.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_WeakOrInvalidInput_Returns400()
    {
        var noEmail = await _client.PostAsJsonAsync("/api/auth/register", new
        { email = "not-an-email", displayName = "X", password = "Test1234!" });
        noEmail.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var shortPassword = await _client.PostAsJsonAsync("/api/auth/register", new
        { email = NewEmail("weak"), displayName = "X", password = "short" });
        shortPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var longName = await _client.PostAsJsonAsync("/api/auth/register", new
        { email = NewEmail("long"), displayName = new string('x', 300), password = "Test1234!" });
        longName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_BeforeVerification_Returns403WithCode()
    {
        var email = NewEmail("unverified");
        await AuthTestHelper.RegisterAsync(_client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = AuthTestHelper.DefaultPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["code"].Should().Be("email_not_verified");
    }

    [Fact]
    public async Task VerifyEmail_ThenLogin_Succeeds()
    {
        var email = NewEmail("verify");
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        auth.AccessToken.Should().NotBeNullOrEmpty();
        auth.User.Email.Should().Be(email);
        auth.User.Role.Should().Be("User");
    }

    [Fact]
    public async Task VerifyEmail_WithBogusToken_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/verify-email", new { token = "bogus" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveOnEmail()
    {
        var email = NewEmail("case");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        { email = email.ToUpperInvariant(), password = AuthTestHelper.DefaultPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var email = NewEmail("wrongpw");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = "WrongPassword!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Lockout (H3, Phase 20 WP2) ──

    private async Task<HttpResponseMessage> LoginRawAsync(string email, string password) =>
        await _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task FailLoginsAsync(string email, int count)
    {
        for (var i = 0; i < count; i++)
            (await LoginRawAsync(email, "Nope1234!")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_AfterFiveFailures_CorrectPassword_Returns423_WrongPassword_Returns401()
    {
        var email = NewEmail("lockout");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        await FailLoginsAsync(email, 5);
        _factory.Emails.AccountMails.Should().Contain(("AccountLocked", email));

        // The owner (correct password) learns about the lock…
        var locked = await LoginRawAsync(email, AuthTestHelper.DefaultPassword);
        locked.StatusCode.Should().Be(HttpStatusCode.Locked);
        var body = await locked.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("code").GetString().Should().Be("account_locked");
        body.GetProperty("retryAfterSeconds").GetInt32().Should().BeGreaterThan(0);

        // …a guesser (wrong password) sees the same 401 as always, and does not extend the lock.
        var wrong = await LoginRawAsync(email, "StillWrong1!");
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Email == email).AccessFailedCount.Should().Be(5);
    }

    [Fact]
    public async Task Login_AfterLockoutExpires_OneWrongAttempt_DoesNotRelock()
    {
        var email = NewEmail("relock");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);
        await FailLoginsAsync(email, 5);

        // Let the lock expire.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            user.LockoutEnd = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        // Previously: the counter was still 5, so this re-locked with a doubled duration.
        await FailLoginsAsync(email, 1);

        var response = await LoginRawAsync(email, AuthTestHelper.DefaultPassword);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_Success_ResetsFailureCounter()
    {
        var email = NewEmail("counter");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        await FailLoginsAsync(email, 4);
        (await LoginRawAsync(email, AuthTestHelper.DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Four more failures would have locked the account had the counter not been reset.
        await FailLoginsAsync(email, 4);
        (await LoginRawAsync(email, AuthTestHelper.DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Password reset (Phase 20 WP2) ──

    private async Task<HttpResponseMessage> AuthedAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_Returns202_AndSendsNothing()
    {
        var email = NewEmail("nobody");
        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.Emails.ResetLinkFor(email).Should().BeNull();
        _factory.Emails.AccountMails.Should().NotContain(m => m.Email == email);
    }

    [Fact]
    public async Task ForgotPassword_LocalUser_SendsResetMail_AndResetWorksOnce()
    {
        var email = NewEmail("reset");
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);
        var oldRefreshCookie = auth.AccessToken; // marker only; the refresh cookie itself is on the client

        var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        forgot.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = _factory.Emails.ResetTokenFor(email);
        token.Should().NotBeNullOrEmpty();
        _factory.Emails.ResetLinkFor(email).Should().Contain("/reset-password?token=");

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Brand-New-Pass9" });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // New password works, old one does not.
        (await LoginRawAsync(email, "Brand-New-Pass9")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginRawAsync(email, AuthTestHelper.DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Every pre-reset refresh token is revoked.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            db.RefreshTokens.Where(t => t.UserId == user.Id && t.CreatedAt < user.UpdatedAt)
                .Should().OnlyContain(t => t.RevokedAt != null);
            user.PasswordResetTokenHash.Should().BeNull();
        }

        // The token is single-use.
        var again = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Another-Pass9" });
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await again.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("reset_token_invalid");
        _ = oldRefreshCookie;
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_Returns400()
    {
        var email = NewEmail("expired");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var token = _factory.Emails.ResetTokenFor(email)!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Brand-New-Pass9" });
        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reset.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString()
            .Should().Be("reset_token_invalid");
    }

    [Fact]
    public async Task ResetPassword_WeakPassword_Returns400WithCode()
    {
        var email = NewEmail("weakreset");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var token = _factory.Emails.ResetTokenFor(email)!;

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "abcdefgh" });
        reset.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reset.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString()
            .Should().Be("password_too_weak");

        // The token survives a rejected attempt.
        (await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Brand-New-Pass9" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ResetPassword_MarksUnverifiedUserVerified_AndClearsLockout()
    {
        var email = NewEmail("unverifiedreset");
        await AuthTestHelper.RegisterAsync(_client, email);   // never verified
        await FailLoginsAsync(email, 5);                       // and locked

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var token = _factory.Emails.ResetTokenFor(email)!;
        (await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Brand-New-Pass9" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Following the reset link proved mailbox control: login now works straight away.
        (await LoginRawAsync(email, "Brand-New-Pass9")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForgotPassword_SsoOnlyAccount_SendsNotApplicableMail_NoToken()
    {
        var email = NewEmail("ssoonly");
        (await _client.PostAsJsonAsync("/api/auth/azure-callback", new
        { idToken = FakeAzureAdTokenValidator.Token($"oid-{Guid.NewGuid():N}", email) })).EnsureSuccessStatusCode();

        var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        forgot.StatusCode.Should().Be(HttpStatusCode.Accepted);

        _factory.Emails.AccountMails.Should().Contain(("PasswordResetNotApplicable", email));
        _factory.Emails.ResetLinkFor(email).Should().BeNull();
    }

    // ── Change password (Phase 20 WP2) ──

    [Fact]
    public async Task ChangePassword_HappyPath_RotatesSession_AndOldPasswordStopsWorking()
    {
        var email = NewEmail("change");
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);
        auth.User.HasPassword.Should().BeTrue();

        var response = await AuthedAsync(HttpMethod.Post, "/api/profile/change-password", auth.AccessToken, new
        { currentPassword = AuthTestHelper.DefaultPassword, newPassword = "Changed-Pass-77" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = (await response.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!;
        fresh.AccessToken.Should().NotBeNullOrEmpty();

        (await LoginRawAsync(email, "Changed-Pass-77")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginRawAsync(email, AuthTestHelper.DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Returns400WithCode()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, NewEmail("changewrong"));

        var response = await AuthedAsync(HttpMethod.Post, "/api/profile/change-password", auth.AccessToken, new
        { currentPassword = "NotMyPassword1!", newPassword = "Changed-Pass-77" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString()
            .Should().Be("password_incorrect");
    }

    [Fact]
    public async Task ChangePassword_SsoOnlyAccount_Returns400PasswordNotSet()
    {
        var email = NewEmail("changesso");
        var azure = await _client.PostAsJsonAsync("/api/auth/azure-callback", new
        { idToken = FakeAzureAdTokenValidator.Token($"oid-{Guid.NewGuid():N}", email) });
        var auth = (await azure.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!;
        auth.User.HasPassword.Should().BeFalse();

        var response = await AuthedAsync(HttpMethod.Post, "/api/profile/change-password", auth.AccessToken, new
        { currentPassword = "whatever1!", newPassword = "Changed-Pass-77" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString()
            .Should().Be("password_not_set");
    }

    // ── Disabled / deleted accounts (H1) ──

    [Fact]
    public async Task DeletedAccount_CannotLoginAgain()
    {
        var email = NewEmail("deleted");
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/profile/data");
        deleteRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        (await _client.SendAsync(deleteRequest)).EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = AuthTestHelper.DefaultPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeletedAccount_ExistingAccessToken_IsRejectedOnNextRequest()
    {
        var email = NewEmail("deadtoken");
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/profile/data");
        deleteRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        (await _client.SendAsync(deleteRequest)).EnsureSuccessStatusCode();

        // The 60-min JWT is still within its lifetime — it must die anyway.
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(meRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Azure AD linking (C1 / H5) ──

    [Fact]
    public async Task AzureLogin_ClaimsUnverifiedSquattedAccount_AndKillsLocalPassword()
    {
        var victimEmail = NewEmail("victim");

        // Attacker pre-registers the victim's corporate address with their own
        // password but never verifies it (they can't — it's not their mailbox).
        await AuthTestHelper.RegisterAsync(_client, victimEmail, "Attacker", "Attacker1!");

        // Victim signs in through Azure SSO with that email.
        var azureResponse = await _client.PostAsJsonAsync("/api/auth/azure-callback", new
        { idToken = FakeAzureAdTokenValidator.Token("oid-victim-1", victimEmail, "Real Victim") });
        azureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // The attacker's password must no longer work on this account.
        var attackerLogin = await _client.PostAsJsonAsync("/api/auth/login", new
        { email = victimEmail, password = "Attacker1!" });
        attackerLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AzureLogin_LinksToVerifiedLocalAccount_KeepingPassword()
    {
        var email = NewEmail("linkverified");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        var azureResponse = await _client.PostAsJsonAsync("/api/auth/azure-callback", new
        { idToken = FakeAzureAdTokenValidator.Token("oid-link-1", email) });
        azureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // A legitimate owner keeps their local password.
        var login = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = AuthTestHelper.DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>();
        body!.User.HasAzureAd.Should().BeTrue();
    }

    [Fact]
    public async Task AzureLogin_MatchesOnOid_NotOnChangedEmailClaim()
    {
        var email = NewEmail("oidfirst");
        var azure1 = await _client.PostAsJsonAsync("/api/auth/azure-callback", new
        { idToken = FakeAzureAdTokenValidator.Token("oid-stable-1", email) });
        var user1 = (await azure1.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!.User;

        // Same oid, mutated preferred_username → must resolve to the same account.
        var azure2 = await _client.PostAsJsonAsync("/api/auth/azure-callback", new
        { idToken = FakeAzureAdTokenValidator.Token("oid-stable-1", NewEmail("changed-upn")) });
        var user2 = (await azure2.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!.User;

        user2.Id.Should().Be(user1.Id);
        user2.Email.Should().Be(user1.Email);
    }

    // ── Misc ──

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithValidToken_ReturnsUser()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, NewEmail("me"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Availability_UnboundedRange_Returns400()
    {
        var auth = await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, NewEmail("range"));

        Guid locationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var location = new SchulerPark.Core.Entities.Location
            {
                Id = Guid.NewGuid(),
                Name = $"RangeLoc-{Guid.NewGuid():N}",
                Address = "X",
                IsActive = true
            };
            db.Locations.Add(location);
            await db.SaveChangesAsync();
            locationId = location.Id;
        }

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/locations/{locationId}/availability?from=0001-01-01&to=9999-12-31");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
