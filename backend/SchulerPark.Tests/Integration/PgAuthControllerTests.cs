using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// AuthController register / login / refresh / logout on the real PostgreSQL schema
/// (filtered unique email index, refresh-token rotation persisted in the DB).
/// </summary>
[Collection("PgApi")]
[Trait("Category", "Integration")]
public class PgAuthControllerTests
{
    private readonly PgApiFixture _fx;

    public PgAuthControllerTests(PgApiFixture fx) => _fx = fx;

    private PgWebApplicationFactory Factory => _fx.Factory;

    // Cookies are handled by hand: the refresh cookie is Secure and the TestServer speaks http.
    private HttpClient NewClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string LocalEmail(string label) => $"{label}-{Guid.NewGuid():N}@schuler.de";
    private static string ExternalEmail(string label) => $"{label}-{Guid.NewGuid():N}@extern-partner.example";

    private static object RegisterBody(string email, string firstName = "Anna", string lastName = "Muster",
        string? company = "Muster AG", string password = AuthTestHelper.DefaultPassword) =>
        new { email, firstName, lastName, company, password };

    private async Task<User> UserAsync(string email)
    {
        User? user = null;
        await AdminTestHelper.WithDbAsync(Factory, async db =>
            user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email.ToLowerInvariant()));
        return user!;
    }

    private static string? RefreshCookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        var cookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken=", StringComparison.Ordinal));
        if (cookie == null) return null;
        var value = cookie["refreshToken=".Length..];
        var end = value.IndexOf(';');
        return end < 0 ? value : value[..end];
    }

    private static HttpRequestMessage WithRefreshCookie(HttpMethod method, string url, string refreshToken, string? bearer = null)
    {
        var req = bearer != null ? AdminTestHelper.Authed(method, url, bearer) : new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", $"refreshToken={refreshToken}");
        return req;
    }

    private async Task<HttpResponseMessage> LoginRawAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password });

    // ── register ────────────────────────────────────────────────

    [SkippableFact]
    public async Task Register_ValidRequest_Returns200WithoutSessionAndStoresIdentity()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("reg");

        var response = await client.PostAsJsonAsync("/api/auth/register",
            RegisterBody(email, "  Anna ", " Muster ", "  Muster AG  "));

        // B12: no auto-login — no token in the body, no refresh cookie, a verification mail instead.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("accessToken");
        RefreshCookieValue(response).Should().BeNull();
        Factory.Emails.VerificationTokenFor(email).Should().NotBeNull();

        // B02: names and company stored, DisplayName derived "First Last" (trimmed).
        var user = await UserAsync(email);
        user.FirstName.Should().Be("Anna");
        user.LastName.Should().Be("Muster");
        user.DisplayName.Should().Be("Anna Muster");
        user.Company.Should().Be("Muster AG");
        user.EmailVerified.Should().BeFalse();
    }

    [SkippableTheory]
    [InlineData("firstName")]
    [InlineData("lastName")]
    [InlineData("company")]
    public async Task Register_MissingRequiredField_Returns400AndCreatesNoUser(string missing)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("missing");
        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["firstName"] = "Anna",
            ["lastName"] = "Muster",
            ["company"] = "Muster AG",
            ["password"] = AuthTestHelper.DefaultPassword
        };
        body.Remove(missing);

        var response = await client.PostAsJsonAsync("/api/auth/register", body);

        // B01
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AdminTestHelper.WithDbAsync(Factory, async db =>
            (await db.Users.AnyAsync(u => u.Email == email)).Should().BeFalse());
    }

    [SkippableTheory]
    [InlineData("firstName", 101)]
    [InlineData("lastName", 101)]
    [InlineData("company", 201)]
    public async Task Register_FieldTooLong_Returns400(string field, int length)
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var body = new Dictionary<string, object?>
        {
            ["email"] = LocalEmail("long"),
            ["firstName"] = "Anna",
            ["lastName"] = "Muster",
            ["company"] = "Muster AG",
            ["password"] = AuthTestHelper.DefaultPassword,
            [field] = new string('x', length)
        };

        var response = await client.PostAsJsonAsync("/api/auth/register", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Register_LongestCompanyAndFittingNames_Accepted()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("long-ok");

        var response = await client.PostAsJsonAsync("/api/auth/register",
            RegisterBody(email, new string('f', 99), new string('l', 100), new string('c', 200)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await UserAsync(email);
        user.Company.Should().HaveLength(200);
        user.DisplayName.Should().HaveLength(200);
    }

    [SkippableFact]
    public async Task Register_MaximumLengthNames_Accepted()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("max");

        var response = await client.PostAsJsonAsync("/api/auth/register",
            RegisterBody(email, new string('f', 100), new string('l', 100), new string('c', 200)));

        // B01: 100-char first and last names are valid; B02: DisplayName is "First Last".
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await UserAsync(email);
        user.FirstName.Should().HaveLength(100);
        user.LastName.Should().HaveLength(100);
        user.DisplayName.Should().Be($"{new string('f', 100)} {new string('l', 100)}");
    }

    [SkippableFact]
    public async Task Register_ExistingEmail_SameResponseAndNothingUpdated()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("dup");

        var first = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email));
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);
        var second = await client.PostAsJsonAsync("/api/auth/register",
            RegisterBody(email, "Eve", "Attacker", "Evil Corp", "Other1234!"));

        // B04: identical response, the existing account is not touched.
        second.StatusCode.Should().Be(first.StatusCode);
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());

        var user = await UserAsync(email);
        user.DisplayName.Should().Be("Anna Muster");
        user.Company.Should().Be("Muster AG");
        (await AuthTestHelper.LoginAsync(client, email)).User.Email.Should().Be(email);
        (await LoginRawAsync(client, email, "Other1234!")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Register_UnverifiedEmailRegisteredAgain_LatestRegistrantOwnsTheAccount()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("squat");
        const string attackerPassword = "Attacker1234!";
        const string victimPassword = "Victim1234!";

        // A squatter registers the address first and never verifies it …
        var first = await client.PostAsJsonAsync("/api/auth/register",
            RegisterBody(email, "Eve", "Attacker", "Evil Corp", attackerPassword));
        var firstToken = Factory.Emails.VerificationTokenFor(email)!;

        // … then the mailbox owner registers it and verifies with the mail they receive.
        var second = await client.PostAsJsonAsync("/api/auth/register",
            RegisterBody(email, "Anna", "Muster", "Muster AG", victimPassword));
        var secondToken = Factory.Emails.VerificationTokenFor(email)!;

        // B6: no enumeration — identical response either way.
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(first.StatusCode);
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        secondToken.Should().NotBe(firstToken);

        (await client.PostAsJsonAsync("/api/auth/verify-email", new { token = secondToken }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The squatter's password must not have survived; the owner's works.
        (await LoginRawAsync(client, email, attackerPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginRawAsync(client, email, victimPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await UserAsync(email);
        user.DisplayName.Should().Be("Anna Muster");
        user.Company.Should().Be("Muster AG");

        // The first token was replaced and no longer verifies anything.
        (await client.PostAsJsonAsync("/api/auth/verify-email", new { token = firstToken }))
            .IsSuccessStatusCode.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Register_AutoApprovedDomainAnyCase_ApprovedOtherDomainPending()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var local = $"case-{Guid.NewGuid():N}@SCHULER.De";
        var external = ExternalEmail("ext");

        (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(local))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(external))).EnsureSuccessStatusCode();

        // B06
        (await UserAsync(local)).ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        (await UserAsync(external)).ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }

    [SkippableFact]
    public async Task Register_SsoOnlyDomain_Returns400SsoOnlyDomain()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = $"sso-{Guid.NewGuid():N}@andritz.com";

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("sso_only_domain");
        await AdminTestHelper.WithDbAsync(Factory, async db =>
            (await db.Users.AnyAsync(u => u.Email == email)).Should().BeFalse());
    }

    [SkippableFact]
    public async Task Register_ExceedingAuthRateLimit_Returns429()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        await using var limited = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimit:AuthPermitLimit", "2");
            b.UseSetting("RateLimit:GlobalPermitLimit", "1000000");
        });
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var res = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(LocalEmail("rl")));
            statuses.Add(res.StatusCode);
        }

        statuses.Take(2).Should().AllSatisfy(s => s.Should().Be(HttpStatusCode.OK));
        statuses.Skip(2).Should().AllSatisfy(s => s.Should().Be(HttpStatusCode.TooManyRequests));
    }

    // ── approval-aware login ────────────────────────────────────

    [SkippableFact]
    public async Task Login_PendingExternalUser_AdminMailedOnlyAfterVerification_403PendingApproval()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(Factory, client, UserRole.Admin, "admin");
        var email = ExternalEmail("pending");

        (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, company: "Partner GmbH")))
            .EnsureSuccessStatusCode();

        // B07: no admin mail at registration time.
        Factory.Emails.ApprovalRequests.Should().NotContain(r => r.UserEmail == email);

        // B08: unverified → wrong password 401, right password 403 email_not_verified.
        (await LoginRawAsync(client, email, "Wrong1234!")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var unverified = await LoginRawAsync(client, email, AuthTestHelper.DefaultPassword);
        unverified.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await unverified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("email_not_verified");

        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);

        // B07/B18: after verification the admin is told the name and the company.
        Factory.Emails.ApprovalRequests.Should().ContainSingle(r =>
            r.UserEmail == email && r.AdminEmail == admin.User.Email
            && r.DisplayName == "Anna Muster" && r.Company == "Partner GmbH");

        // B08: verified + pending + correct password → 403 pending_approval; wrong password stays 401.
        var pending = await LoginRawAsync(client, email, AuthTestHelper.DefaultPassword);
        pending.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("pending_approval");
        RefreshCookieValue(pending).Should().BeNull();
        (await LoginRawAsync(client, email, "Wrong1234!")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Login_RejectedUser_IndistinguishableFromWrongPassword()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var admin = await AdminTestHelper.CreateUserWithRoleAsync(Factory, client, UserRole.Admin, "admin");
        var email = ExternalEmail("rejected");
        (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email))).EnsureSuccessStatusCode();
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);
        var userId = (await UserAsync(email)).Id;

        var decision = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Post,
            $"/api/admin/users/{userId}/approval", admin.AccessToken, new { approve = false }));
        decision.StatusCode.Should().Be(HttpStatusCode.OK);

        var rightPassword = await LoginRawAsync(client, email, AuthTestHelper.DefaultPassword);
        var wrongPassword = await LoginRawAsync(client, email, "Wrong1234!");
        var unknownUser = await LoginRawAsync(client, ExternalEmail("nobody"), AuthTestHelper.DefaultPassword);

        // B09
        rightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await rightPassword.Content.ReadAsStringAsync();
        body.Should().Be(await wrongPassword.Content.ReadAsStringAsync());
        body.Should().Be(await unknownUser.Content.ReadAsStringAsync());
        body.Should().Contain("invalid_credentials");
    }

    // ── login / refresh / logout session contract ───────────────

    [SkippableFact]
    public async Task Login_ValidCredentials_ReturnsTokenAndHardenedRefreshCookie()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("login");
        await AuthTestHelper.RegisterAsync(client, email, "Anna Muster");
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);

        var response = await LoginRawAsync(client, email.ToUpperInvariant(), AuthTestHelper.DefaultPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!;
        auth.AccessToken.Should().NotBeNullOrEmpty();
        auth.User.Email.Should().Be(email);
        auth.User.FirstName.Should().Be("Anna");
        auth.User.LastName.Should().Be("Muster");
        auth.User.Company.Should().Be(AuthTestHelper.DefaultCompany);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("refreshToken="));
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure")
            .And.Contain("samesite=strict").And.Contain("path=/api/auth");

        // The refresh token is persisted (not revoked) for this user.
        var token = RefreshCookieValue(response)!;
        await AdminTestHelper.WithDbAsync(Factory, async db =>
            (await db.RefreshTokens.CountAsync(t => t.UserId == auth.User.Id && t.RevokedAt == null))
                .Should().Be(1));
        token.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task Profile_AfterRegistration_ReturnsFirstLastAndCompany()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("profile");
        await AuthTestHelper.RegisterAsync(client, email, "Berta Beispiel", company: "Beispiel KG");
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);
        var auth = await AuthTestHelper.LoginAsync(client, email);

        var response = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/profile", auth.AccessToken));

        // B02
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("firstName").GetString().Should().Be("Berta");
        json.GetProperty("lastName").GetString().Should().Be("Beispiel");
        json.GetProperty("company").GetString().Should().Be("Beispiel KG");
        json.GetProperty("displayName").GetString().Should().Be("Berta Beispiel");
    }

    [SkippableFact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();

        var response = await client.PostAsync("/api/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Refresh_ValidCookie_RotatesTokenAndOldTokenIsRejected()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("refresh");
        await AuthTestHelper.RegisterAsync(client, email);
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);
        var login = await LoginRawAsync(client, email, AuthTestHelper.DefaultPassword);
        var original = RefreshCookieValue(login)!;

        var refreshed = await client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/refresh", original));

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await refreshed.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!;
        auth.AccessToken.Should().NotBeNullOrEmpty();
        auth.User.Email.Should().Be(email);
        var rotated = RefreshCookieValue(refreshed);
        rotated.Should().NotBeNullOrEmpty().And.NotBe(original);

        // The new access token works.
        var me = await client.SendAsync(AdminTestHelper.Authed(HttpMethod.Get, "/api/auth/me", auth.AccessToken));
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        // The rotated token is itself usable once.
        var again = await client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/refresh", rotated!));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var latest = RefreshCookieValue(again)!;

        // The old refresh token was revoked by the rotation; replaying it is treated as theft
        // (reuse detection) and revokes the whole family, including the newest token.
        var replay = await client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/refresh", original));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var afterReplay = await client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/refresh", latest));
        afterReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Refresh_GarbageCookie_Returns401()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();

        var response = await client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/refresh", "not-a-real-token"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Logout_Authenticated_RevokesRefreshTokenAndClearsCookie()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = LocalEmail("logout");
        await AuthTestHelper.RegisterAsync(client, email);
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);
        var login = await LoginRawAsync(client, email, AuthTestHelper.DefaultPassword);
        var auth = (await login.Content.ReadFromJsonAsync<AuthTestHelper.AuthResult>())!;
        var refreshToken = RefreshCookieValue(login)!;

        var logout = await client.SendAsync(
            WithRefreshCookie(HttpMethod.Post, "/api/auth/logout", refreshToken, auth.AccessToken));

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cleared = logout.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("refreshToken="));
        cleared.ToLowerInvariant().Should().Contain("expires=thu, 01 jan 1970").And.Contain("path=/api/auth");

        await AdminTestHelper.WithDbAsync(Factory, async db =>
            (await db.RefreshTokens.CountAsync(t => t.UserId == auth.User.Id && t.RevokedAt == null))
                .Should().Be(0));
        var refresh = await client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/refresh", refreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Logout_Anonymous_Returns401()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();

        var response = await client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Azure AD SSO ────────────────────────────────────────────

    [SkippableFact]
    public async Task AzureCallback_NewUser_CreatedApprovedWithoutCompany()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = $"sso-{Guid.NewGuid():N}@andritz.com";

        var response = await client.PostAsJsonAsync("/api/auth/azure-callback",
            new { idToken = FakeAzureAdTokenValidator.Token(Guid.NewGuid().ToString(), email, "Sso Person") });

        // B10/B05
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RefreshCookieValue(response).Should().NotBeNullOrEmpty();
        var user = await UserAsync(email);
        user.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        user.Company.Should().BeNull();
        user.EmailVerified.Should().BeTrue();
    }

    [SkippableFact]
    public async Task AzureCallback_LinksExistingPendingLocalAccount_BecomesApproved()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();
        var email = ExternalEmail("link");
        (await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email))).EnsureSuccessStatusCode();
        await AuthTestHelper.VerifyEmailAsync(Factory, client, email);
        (await UserAsync(email)).ApprovalStatus.Should().Be(ApprovalStatus.Pending);

        var oid = Guid.NewGuid().ToString();
        var response = await client.PostAsJsonAsync("/api/auth/azure-callback",
            new { idToken = FakeAzureAdTokenValidator.Token(oid, email, "Anna Muster") });

        // B10
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await UserAsync(email);
        user.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        user.AzureAdObjectId.Should().Be(oid);
        (await AuthTestHelper.LoginAsync(client, email)).User.HasAzureAd.Should().BeTrue();
    }

    [SkippableFact]
    public async Task AzureCallback_InvalidToken_Returns401()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var client = NewClient();

        var response = await client.PostAsJsonAsync("/api/auth/azure-callback", new { idToken = "garbage" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("azure_token_invalid");
    }
}
