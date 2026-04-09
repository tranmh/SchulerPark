using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class AuthTests
{
    private readonly HttpClient _client;

    public AuthTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsTokenAndUser()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"reg-{Guid.NewGuid():N}@schuler.de",
            displayName = "New User",
            password = "Test1234!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.User.DisplayName.Should().Be("New User");
        body.User.Role.Should().Be("User");
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = $"dup-{Guid.NewGuid():N}@schuler.de";

        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "First",
            password = "Test1234!"
        });

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "Second",
            password = "Test1234!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var email = $"login-{Guid.NewGuid():N}@schuler.de";

        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "Login Test",
            password = "Test1234!"
        });

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Test1234!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var email = $"wrongpw-{Guid.NewGuid():N}@schuler.de";

        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "Wrong PW",
            password = "Test1234!"
        });

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "WrongPassword!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithValidToken_ReturnsUser()
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"me-{Guid.NewGuid():N}@schuler.de",
            displayName = "Me Test",
            password = "Test1234!"
        });

        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
    private record UserDto(Guid Id, string Email, string DisplayName, string? CarLicensePlate, string Role, bool HasAzureAd);
}
