using System.Net.Http.Json;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// Registration requires email verification before login works; this helper
/// runs the full register → verify (via captured email) → login flow.
/// </summary>
public static class AuthTestHelper
{
    public record AuthUserDto(Guid Id, string Email, string DisplayName, string? CarLicensePlate,
        string Role, bool HasAzureAd, Guid? PreferredLocationId, Guid? PreferredSlotId, bool HasPassword = false,
        string? FirstName = null, string? LastName = null, string? Company = null);
    public record AuthResult(string AccessToken, DateTime ExpiresAt, AuthUserDto User);

    public const string DefaultPassword = "Test1234!";

    public const string DefaultCompany = "Test GmbH";

    /// <summary>
    /// Phase 21: registration takes first name, surname and company. <paramref name="displayName"/>
    /// is split at the first space ("Anna Muster" → Anna / Muster); a single word becomes the
    /// first name with surname "Tester", so the derived DisplayName is "Word Tester".
    /// </summary>
    public static async Task RegisterAsync(HttpClient client, string email,
        string displayName = "Test User", string password = DefaultPassword, string company = DefaultCompany)
    {
        var (firstName, lastName) = SplitName(displayName);
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            firstName,
            lastName,
            company,
            password
        });
        response.EnsureSuccessStatusCode();
    }

    public static (string FirstName, string LastName) SplitName(string displayName)
    {
        var parts = displayName.Trim().Split(' ', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], "Tester");
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
