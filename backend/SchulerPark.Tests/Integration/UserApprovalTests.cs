using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Phase 18: external (non-allowlisted-domain) registrations are Pending until an
/// admin accepts them; allowlisted domains and Azure AD SSO stay self-service.
/// The test factory allowlists andritz.com + schuler.de, so @external.example is
/// the "needs approval" domain here.
/// </summary>
[Collection("Integration")]
public class UserApprovalTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UserApprovalTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string ExternalEmail(string label) => $"{label}-{Guid.NewGuid():N}@external.example";
    private static string InternalEmail(string label) => $"{label}-{Guid.NewGuid():N}@andritz.com";

    private async Task<string> CreateAdminTokenAsync()
    {
        var email = InternalEmail("approver");
        await AuthTestHelper.RegisterAsync(_client, email, "Approver");
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            user.Role = UserRole.Admin;
            await db.SaveChangesAsync();
        }

        var auth = await AuthTestHelper.LoginAsync(_client, email);
        return auth.AccessToken;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    // ── Domain gating ──

    [Fact]
    public async Task AllowlistedDomain_CanLoginRightAfterVerification()
    {
        var email = InternalEmail("auto");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        var auth = await AuthTestHelper.LoginAsync(_client, email);
        auth.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExternalDomain_AfterVerification_LoginReturns403PendingApproval()
    {
        var email = ExternalEmail("pending");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = AuthTestHelper.DefaultPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["code"].Should().Be("pending_approval");
    }

    [Fact]
    public async Task ExternalDomain_WithWrongPassword_Returns401NotPendingCode()
    {
        // The pending state must not leak to callers who don't hold the password.
        var email = ExternalEmail("oracle");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = "WrongPassword1!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterResponse_IsIdenticalForInternalAndExternalDomains()
    {
        var internalResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        { email = InternalEmail("same"), displayName = "A", password = AuthTestHelper.DefaultPassword });
        var externalResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        { email = ExternalEmail("same"), displayName = "A", password = AuthTestHelper.DefaultPassword });

        internalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        externalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await internalResponse.Content.ReadAsStringAsync())
            .Should().Be(await externalResponse.Content.ReadAsStringAsync());
    }

    // ── Admin notification ──

    [Fact]
    public async Task AdminIsNotified_OnVerification_NotOnRegistration()
    {
        var adminToken = await CreateAdminTokenAsync();
        _ = adminToken; // admin exists; notification goes to its email

        var email = ExternalEmail("notify");
        await AuthTestHelper.RegisterAsync(_client, email);
        var notifiedAfterRegister = _factory.Emails.ApprovalMails
            .Count(m => m.Type == "ApprovalRequest");

        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);
        var notifiedAfterVerify = _factory.Emails.ApprovalMails
            .Count(m => m.Type == "ApprovalRequest");

        notifiedAfterVerify.Should().BeGreaterThan(notifiedAfterRegister);
    }

    // ── Approve / reject ──

    [Fact]
    public async Task Approve_AllowsLogin_AndNotifiesUser()
    {
        var adminToken = await CreateAdminTokenAsync();
        var email = ExternalEmail("approve");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = db.Users.Single(u => u.Email == email).Id;
        }

        var decide = await _client.SendAsync(Authed(
            HttpMethod.Post, $"/api/admin/users/{userId}/approval", adminToken, new { approve = true }));
        decide.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await AuthTestHelper.LoginAsync(_client, email);
        auth.AccessToken.Should().NotBeNullOrEmpty();
        _factory.Emails.ApprovalMails.Should().Contain(m => m.Type == "AccountApproved" && m.Email == email);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Id == userId);
            user.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
            user.ApprovedAt.Should().NotBeNull();
            user.ApprovedByUserId.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Reject_LoginBehavesLikeBadCredentials_AndNotifiesUser()
    {
        var adminToken = await CreateAdminTokenAsync();
        var email = ExternalEmail("reject");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = db.Users.Single(u => u.Email == email).Id;
        }

        var decide = await _client.SendAsync(Authed(
            HttpMethod.Post, $"/api/admin/users/{userId}/approval", adminToken, new { approve = false }));
        decide.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new
        { email, password = AuthTestHelper.DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _factory.Emails.ApprovalMails.Should().Contain(m => m.Type == "AccountRejected" && m.Email == email);
    }

    [Fact]
    public async Task Decide_OnAlreadyApprovedUser_Returns400()
    {
        var adminToken = await CreateAdminTokenAsync();
        var email = InternalEmail("already");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = db.Users.Single(u => u.Email == email).Id;
        }

        var decide = await _client.SendAsync(Authed(
            HttpMethod.Post, $"/api/admin/users/{userId}/approval", adminToken, new { approve = true }));
        decide.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PendingList_RequiresAdmin()
    {
        var email = InternalEmail("plainuser");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);
        var auth = await AuthTestHelper.LoginAsync(_client, email);

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/users/pending", auth.AccessToken));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PendingList_AsAdmin_ContainsPendingUser()
    {
        var adminToken = await CreateAdminTokenAsync();
        var email = ExternalEmail("listed");
        await AuthTestHelper.RegisterAsync(_client, email);
        await AuthTestHelper.VerifyEmailAsync(_factory, _client, email);

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/users/pending", adminToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(email);
    }

    // ── SSO bypass ──

    [Fact]
    public async Task AzureSsoUser_WithExternalDomain_IsAutoApproved()
    {
        var email = ExternalEmail("sso");
        var token = FakeAzureAdTokenValidator.Token(Guid.NewGuid().ToString(), email, "SSO User");

        var response = await _client.PostAsJsonAsync("/api/auth/azure-callback", new { idToken = token });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Email == email).ApprovalStatus.Should().Be(ApprovalStatus.Approved);
    }

    // ── Settings unit checks ──

    [Theory]
    [InlineData("someone@andritz.com", true)]
    [InlineData("someone@ANDRITZ.COM", true)]
    [InlineData("someone@sub.andritz.com", false)]
    [InlineData("someone@andritz.com.evil.example", false)]
    [InlineData("someone@external.example", false)]
    [InlineData("no-at-sign", false)]
    public void RegistrationSettings_DomainMatching(string email, bool expected)
    {
        var settings = new RegistrationSettings { AutoApprovedDomains = "andritz.com" };
        settings.IsAutoApprovedDomain(email).Should().Be(expected);
    }

    [Fact]
    public void RegistrationSettings_SupportsMultipleSeparators()
    {
        var settings = new RegistrationSettings { AutoApprovedDomains = "andritz.com; schuler.de ,example.org" };
        settings.IsAutoApprovedDomain("a@schuler.de").Should().BeTrue();
        settings.IsAutoApprovedDomain("a@example.org").Should().BeTrue();
        settings.IsAutoApprovedDomain("a@other.example").Should().BeFalse();
    }
}
