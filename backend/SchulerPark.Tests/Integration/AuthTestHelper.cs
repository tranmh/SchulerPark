using System.Net.Http.Json;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Registration requires email verification before login works; this helper
/// runs the full register → verify (via captured email) → login flow.
/// </summary>
public static class AuthTestHelper
{
    public record AuthUserDto(Guid Id, string Email, string DisplayName, string? CarLicensePlate,
        string Role, bool HasAzureAd, Guid? PreferredLocationId, Guid? PreferredSlotId);
    public record AuthResult(string AccessToken, DateTime ExpiresAt, AuthUserDto User);

    public const string DefaultPassword = "Test1234!";

    public static async Task RegisterAsync(HttpClient client, string email,
        string displayName = "Test User", string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName,
            password
        });
        response.EnsureSuccessStatusCode();
    }

    public static async Task VerifyEmailAsync(CustomWebApplicationFactory factory, HttpClient client, string email)
    {
        var token = factory.Emails.VerificationTokenFor(email)
            ?? throw new InvalidOperationException($"No verification email captured for {email}");
        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        response.EnsureSuccessStatusCode();
    }

    public static async Task<AuthResult> LoginAsync(HttpClient client, string email, string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    public static async Task<AuthResult> RegisterVerifiedAsync(
        CustomWebApplicationFactory factory, HttpClient client,
        string? email = null, string displayName = "Test User", string password = DefaultPassword)
    {
        email ??= $"test-{Guid.NewGuid():N}@schuler.de";
        await RegisterAsync(client, email, displayName, password);
        await VerifyEmailAsync(factory, client, email);
        return await LoginAsync(client, email, password);
    }
}
