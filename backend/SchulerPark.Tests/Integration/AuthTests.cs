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

    // ── Lockout (H3) ──

    [Fact]
    public async Task Login_AfterFiveFailures_LocksOutEvenCorrectPassword()
    {
        var email = NewEmail("lockout");
        await AuthTestHelper.RegisterVerifiedAsync(_factory, _client, email);

        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Nope1234!" });
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = AuthTestHelper.DefaultPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
