using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class BookingTests
{
    private readonly HttpClient _client;

    public BookingTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string Token, Guid UserId)> RegisterAndGetTokenAsync(string? email = null)
    {
        email ??= $"test-{Guid.NewGuid():N}@schuler.de";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            displayName = "Test User",
            password = "Test1234!"
        });
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return (auth!.AccessToken, auth.User.Id);
    }

    private HttpRequestMessage CreateAuthedRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task GetLocations_WithAuth_ReturnsOk()
    {
        var (token, _) = await RegisterAndGetTokenAsync();

        var request = CreateAuthedRequest(HttpMethod.Get, "/api/locations", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLocations_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/locations");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateBooking_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/bookings", new
        {
            locationId = Guid.NewGuid(),
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd"),
            timeSlot = "Morning"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyBookings_ReturnsEmptyForNewUser()
    {
        var (token, _) = await RegisterAndGetTokenAsync();

        var request = CreateAuthedRequest(HttpMethod.Get, "/api/bookings/my", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"totalCount\":0");
    }

    [Fact]
    public async Task AdminEndpoint_AsRegularUser_Returns403()
    {
        var (token, _) = await RegisterAndGetTokenAsync();

        var request = CreateAuthedRequest(HttpMethod.Get, "/api/admin/locations", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
    private record UserDto(Guid Id, string Email, string DisplayName, string? CarLicensePlate, string Role, bool HasAzureAd);
}
