using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services;
using static SchulerPark.Tests.Integration.AdminTestHelper;

namespace SchulerPark.Tests.Integration;

/// <summary>
/// §5 backfill: waitlist promotion order with three candidates — a preferred-slot match
/// beats a heavier lottery weight, which beats an earlier CreatedAt.
/// </summary>
[Collection("Integration")]
public class WaitlistOrderingTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WaitlistOrderingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task PromoteAsync(Guid locationId, DateOnly date, Guid slotId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new WaitlistService(db, _factory.Emails, new RecordingPushService(), TestOptions.Booking, _factory.Clock, NullLogger<WaitlistService>.Instance);
        await service.TryPromoteWaitlistAsync(locationId, date, TimeSlot.Morning, slotId);
    }

    private async Task AddLossesAsync(Guid userId, Guid locationId, int count)
    {
        await WithDbAsync(_factory, async db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.LotteryHistories.Add(new LotteryHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    LocationId = locationId,
                    Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1 - i)),
                    TimeSlot = TimeSlot.Morning,
                    Won = false
                });
            }
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Promotion_PrefersSlotMatch_ThenWeight_ThenCreatedAt()
    {
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var date = FutureDate(6);
        var freedSlot = slotIds[0];

        var earliest = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "earliest");
        var heavy = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "heavy");
        var preferrer = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "preferrer");

        // heavy has 4 consecutive losses (weight 3.0); preferrer prefers exactly the freed slot.
        await AddLossesAsync(heavy.User.Id, locationId, 4);
        await WithDbAsync(_factory, async db =>
        {
            var u = await db.Users.FindAsync(preferrer.User.Id);
            u!.PreferredLocationId = locationId;
            u.PreferredSlotId = freedSlot;
            await db.SaveChangesAsync();
        });

        var t0 = DateTime.UtcNow.AddHours(-3);
        var earliestId = await SeedBookingAsync(_factory, earliest.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Waitlisted, t0);
        var heavyId = await SeedBookingAsync(_factory, heavy.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Waitlisted, t0.AddHours(1));
        var preferrerId = await SeedBookingAsync(_factory, preferrer.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Waitlisted, t0.AddHours(2));

        // 1st promotion: the preferred-slot match wins despite being the latest and lightest.
        await PromoteAsync(locationId, date, freedSlot);
        (await GetBookingAsync(_factory, preferrerId)).Status.Should().Be(BookingStatus.Won);
        (await GetBookingAsync(_factory, heavyId)).Status.Should().Be(BookingStatus.Waitlisted);

        // Free the slot again: now the heavier weight beats the earlier CreatedAt.
        await WithDbAsync(_factory, async db =>
        {
            var b = await db.Bookings.FindAsync(preferrerId);
            b!.Status = BookingStatus.Cancelled;
            b.ParkingSlotId = null;
            await db.SaveChangesAsync();
        });
        await PromoteAsync(locationId, date, freedSlot);
        (await GetBookingAsync(_factory, heavyId)).Status.Should().Be(BookingStatus.Won);
        (await GetBookingAsync(_factory, earliestId)).Status.Should().Be(BookingStatus.Waitlisted);

        // Free once more: the last candidate standing is promoted by CreatedAt.
        await WithDbAsync(_factory, async db =>
        {
            var b = await db.Bookings.FindAsync(heavyId);
            b!.Status = BookingStatus.Cancelled;
            b.ParkingSlotId = null;
            await db.SaveChangesAsync();
        });
        await PromoteAsync(locationId, date, freedSlot);
        (await GetBookingAsync(_factory, earliestId)).Status.Should().Be(BookingStatus.Won);
    }

    [Fact]
    public async Task Promotion_EqualWeight_PicksEarliestCreated()
    {
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var date = FutureDate(6);

        var a = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "a");
        var b = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "b");
        var t0 = DateTime.UtcNow.AddHours(-2);
        var laterId = await SeedBookingAsync(_factory, a.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Waitlisted, t0.AddMinutes(30));
        var earlierId = await SeedBookingAsync(_factory, b.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Waitlisted, t0);

        await PromoteAsync(locationId, date, slotIds[0]);

        (await GetBookingAsync(_factory, earlierId)).Status.Should().Be(BookingStatus.Won);
        (await GetBookingAsync(_factory, laterId)).Status.Should().Be(BookingStatus.Waitlisted);
    }

    [Fact]
    public async Task Promotion_SkipsDeletedUsers()
    {
        var (locationId, slotIds) = await SeedLocationAsync(_factory, 1);
        var date = FutureDate(6);

        var gone = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "gone");
        var alive = await CreateUserWithRoleAsync(_factory, _client, UserRole.User, "alive");
        var t0 = DateTime.UtcNow.AddHours(-2);
        var goneId = await SeedBookingAsync(_factory, gone.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Waitlisted, t0);
        var aliveId = await SeedBookingAsync(_factory, alive.User.Id, locationId, null, date, TimeSlot.Morning, BookingStatus.Waitlisted, t0.AddHours(1));
        await WithDbAsync(_factory, async db =>
        {
            var u = await db.Users.FindAsync(gone.User.Id);
            u!.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });

        await PromoteAsync(locationId, date, slotIds[0]);

        (await GetBookingAsync(_factory, aliveId)).Status.Should().Be(BookingStatus.Won);
        (await GetBookingAsync(_factory, goneId)).Status.Should().Be(BookingStatus.Waitlisted);
    }
}
