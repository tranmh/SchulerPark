using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data;

namespace SchulerPark.Tests.Integration;

/// <summary>Shared seeding for the Phase 20 integration tests (InMemory factory).</summary>
public static class AdminTestHelper
{
    /// <summary>Registers + verifies a local user, promotes it to <paramref name="role"/> and logs in.</summary>
    public static async Task<AuthTestHelper.AuthResult> CreateUserWithRoleAsync(
        CustomWebApplicationFactory factory, HttpClient client, UserRole role, string label = "user")
    {
        var email = $"{label}-{Guid.NewGuid():N}@schuler.de";
        await AuthTestHelper.RegisterAsync(client, email, label);
        await AuthTestHelper.VerifyEmailAsync(factory, client, email);

        if (role != UserRole.User)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.Role = role;
            await db.SaveChangesAsync();
        }

        return await AuthTestHelper.LoginAsync(client, email);
    }

    public static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    public static async Task<(Guid LocationId, List<Guid> SlotIds)> SeedLocationAsync(
        CustomWebApplicationFactory factory, int slotCount, string? name = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var location = new Location
        {
            Id = Guid.NewGuid(),
            Name = name ?? $"Loc-{Guid.NewGuid():N}",
            Address = "Somewhere",
            IsActive = true,
            DefaultAlgorithm = LotteryAlgorithm.PureRandom
        };
        db.Locations.Add(location);
        var slotIds = new List<Guid>();
        for (var i = 1; i <= slotCount; i++)
        {
            var slot = new ParkingSlot
            {
                Id = Guid.NewGuid(),
                LocationId = location.Id,
                SlotNumber = $"P{i:000}",
                IsActive = true
            };
            db.ParkingSlots.Add(slot);
            slotIds.Add(slot.Id);
        }
        await db.SaveChangesAsync();
        return (location.Id, slotIds);
    }

    public static async Task SeedLotteryRunAsync(CustomWebApplicationFactory factory, Guid locationId, DateOnly date, TimeSlot timeSlot)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.LotteryRuns.Add(new LotteryRun
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Date = date,
            TimeSlot = timeSlot,
            Algorithm = LotteryAlgorithm.PureRandom,
            RanAt = DateTime.UtcNow,
            TotalBookings = 0,
            AvailableSlots = 0
        });
        await db.SaveChangesAsync();
    }

    public static async Task<Guid> SeedBookingAsync(CustomWebApplicationFactory factory, Guid userId, Guid locationId,
        Guid? slotId, DateOnly date, TimeSlot timeSlot, BookingStatus status, DateTime? createdAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LocationId = locationId,
            ParkingSlotId = slotId,
            Date = date,
            TimeSlot = timeSlot,
            Status = status,
            ConfirmedAt = status == BookingStatus.Confirmed ? DateTime.UtcNow : null,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    public static async Task<Booking> GetBookingAsync(CustomWebApplicationFactory factory, Guid bookingId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
    }

    public static async Task WithDbAsync(CustomWebApplicationFactory factory, Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    /// <summary>A weekday at least <paramref name="daysAhead"/> days out (Berlin/UTC boundary safe).</summary>
    public static DateOnly FutureDate(int daysAhead = 5) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysAhead));
}
