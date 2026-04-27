using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class UsersAdminTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UsersAdminTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(Guid Id, string Email, string Token)> CreateUserAsync(UserRole role, string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@schuler.de";
        const string password = "Test1234!";

        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = label,
            password
        });
        register.EnsureSuccessStatusCode();

        if (role != UserRole.User)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            user.Role = role;
            await db.SaveChangesAsync();
        }

        // Re-login so the JWT carries the updated role claim
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();

        return (auth!.User.Id, email, auth.AccessToken);
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task ListUsers_AsAdmin_Returns403()
    {
        var (_, _, token) = await CreateUserAsync(UserRole.Admin, "list-admin");
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/users", token));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListUsers_AsRegularUser_Returns403()
    {
        var (_, _, token) = await CreateUserAsync(UserRole.User, "list-user");
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/users", token));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListUsers_AsSuperAdmin_Returns200()
    {
        var (_, _, token) = await CreateUserAsync(UserRole.SuperAdmin, "list-su");
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/users", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateRole_AsSuperAdmin_PromotesUserToAdmin()
    {
        var (_, _, suToken) = await CreateUserAsync(UserRole.SuperAdmin, "promoter");
        var (targetId, _, _) = await CreateUserAsync(UserRole.User, "promote-target");

        var response = await _client.SendAsync(Authed(
            HttpMethod.Put, $"/api/admin/users/{targetId}/role", suToken,
            new { role = "Admin" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Id == targetId).Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task UpdateRole_AsAdmin_Returns403()
    {
        var (_, _, adminToken) = await CreateUserAsync(UserRole.Admin, "blocked-admin");
        var (targetId, _, _) = await CreateUserAsync(UserRole.User, "blocked-target");

        var response = await _client.SendAsync(Authed(
            HttpMethod.Put, $"/api/admin/users/{targetId}/role", adminToken,
            new { role = "Admin" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateRole_SelfDemotion_Returns400()
    {
        var (id, _, token) = await CreateUserAsync(UserRole.SuperAdmin, "selfdemote");

        var response = await _client.SendAsync(Authed(
            HttpMethod.Put, $"/api/admin/users/{id}/role", token,
            new { role = "Admin" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Disable_AsSuperAdmin_SoftDeletesUser()
    {
        var (_, _, suToken) = await CreateUserAsync(UserRole.SuperAdmin, "disabler");
        var (targetId, _, _) = await CreateUserAsync(UserRole.User, "disable-target");

        var response = await _client.SendAsync(Authed(
            HttpMethod.Put, $"/api/admin/users/{targetId}/disable", suToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Single(u => u.Id == targetId).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Disable_SelfDisable_Returns400()
    {
        var (id, _, token) = await CreateUserAsync(UserRole.SuperAdmin, "self-disable");

        var response = await _client.SendAsync(Authed(
            HttpMethod.Put, $"/api/admin/users/{id}/disable", token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_AsSuperAdmin_HardDeletesUser()
    {
        var (_, _, suToken) = await CreateUserAsync(UserRole.SuperAdmin, "deleter");
        var (targetId, _, _) = await CreateUserAsync(UserRole.User, "delete-target");

        var response = await _client.SendAsync(Authed(
            HttpMethod.Delete, $"/api/admin/users/{targetId}", suToken));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Any(u => u.Id == targetId).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Self_Returns400()
    {
        var (id, _, token) = await CreateUserAsync(UserRole.SuperAdmin, "self-delete");

        var response = await _client.SendAsync(Authed(
            HttpMethod.Delete, $"/api/admin/users/{id}", token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
    private record UserDto(Guid Id, string Email, string DisplayName, string? CarLicensePlate, string Role, bool HasAzureAd, Guid? PreferredLocationId);
}
