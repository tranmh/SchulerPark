using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Entities;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

[Collection("Integration")]
public class ProfilePreferredSlotTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProfilePreferredSlotTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"pref-{Guid.NewGuid():N}@schuler.de",
            displayName = "Pref User",
            password = "Test1234!"
        });
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    private async Task<(Guid LocationId, Guid SlotId)> SeedLocationAndSlotAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var location = new Location
        {
            Id = Guid.NewGuid(),
            Name = $"Loc-{Guid.NewGuid():N}",
            Address = "Somewhere",
            IsActive = true,
            DefaultAlgorithm = SchulerPark.Core.Enums.LotteryAlgorithm.PureRandom
        };
        var slot = new ParkingSlot
        {
            Id = Guid.NewGuid(),
            LocationId = location.Id,
            SlotNumber = "A-1",
            IsActive = true
        };
        db.Locations.Add(location);
        db.ParkingSlots.Add(slot);
        await db.SaveChangesAsync();
        return (location.Id, slot.Id);
    }

    private HttpRequestMessage Authed(HttpMethod m, string url, string token, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Fact]
    public async Task UpdateProfile_AcceptsValidPreferredSlot()
    {
        var token = await RegisterAndGetTokenAsync();
        var (locId, slotId) = await SeedLocationAndSlotAsync();

        var response = await _client.SendAsync(Authed(HttpMethod.Put, "/api/profile", token, new
        {
            displayName = "Pref User",
            carLicensePlate = (string?)null,
            preferredLocationId = locId,
            preferredSlotId = slotId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(slotId.ToString());
    }

    [Fact]
    public async Task UpdateProfile_RejectsSlotWithoutLocation()
    {
        var token = await RegisterAndGetTokenAsync();
        var (_, slotId) = await SeedLocationAndSlotAsync();

        var response = await _client.SendAsync(Authed(HttpMethod.Put, "/api/profile", token, new
        {
            displayName = "Pref User",
            carLicensePlate = (string?)null,
            preferredLocationId = (Guid?)null,
            preferredSlotId = slotId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProfile_RejectsSlotFromDifferentLocation()
    {
        var token = await RegisterAndGetTokenAsync();
        var (_, slotA) = await SeedLocationAndSlotAsync();
        var (locB, _) = await SeedLocationAndSlotAsync();

        var response = await _client.SendAsync(Authed(HttpMethod.Put, "/api/profile", token, new
        {
            displayName = "Pref User",
            carLicensePlate = (string?)null,
            preferredLocationId = locB,
            preferredSlotId = slotA
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record AuthResponse(string AccessToken, DateTime ExpiresAt);
}
