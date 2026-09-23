namespace SchulerPark.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Infrastructure.Data.Migrations;
using Xunit;

/// <summary>
/// Phase 20 WP4 data migrations, run against the real Postgres schema. The fixture applies
/// all migrations before any row exists, so each test seeds rows and re-runs the exact SQL
/// the migration executes (exposed as a constant on the migration class).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class Wp4MigrationDataTests
{
    private readonly PostgresFixture _fx;
    public Wp4MigrationDataTests(PostgresFixture fx) => _fx = fx;

    private async Task<Guid> SeedAsync(BookingStatus status, DateOnly date, TimeSlot slot, DateTime? deadline = null)
    {
        var loc = new Location { Id = Guid.NewGuid(), Name = $"L-{Guid.NewGuid():N}", Address = "A" };
        var user = new User { Id = Guid.NewGuid(), Email = $"u-{Guid.NewGuid():N}@x.de", DisplayName = "U" };
        var booking = new Booking
        {
            Id = Guid.NewGuid(), UserId = user.Id, LocationId = loc.Id,
            Date = date, TimeSlot = slot, Status = status, ConfirmationDeadline = deadline
        };
        await using var seed = _fx.NewContext();
        seed.AddRange(loc, user);
        seed.Bookings.Add(booking);
        await seed.SaveChangesAsync();
        return booking.Id;
    }

    [SkippableFact]
    public async Task Backfill_SetsBerlinDefaults_OnWonRowsOnly()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var summer = new DateOnly(2027, 7, 1);    // CEST: 07:00 → 05:00Z, 13:00 → 11:00Z
        var winter = new DateOnly(2027, 1, 15);   // CET:  07:00 → 06:00Z
        var wonMorningSummer = await SeedAsync(BookingStatus.Won, summer, TimeSlot.Morning);
        var wonAfternoonSummer = await SeedAsync(BookingStatus.Won, summer, TimeSlot.Afternoon);
        var wonMorningWinter = await SeedAsync(BookingStatus.Won, winter, TimeSlot.Morning);
        var existing = new DateTime(2027, 7, 1, 6, 30, 0, DateTimeKind.Utc);
        var wonWithDeadline = await SeedAsync(BookingStatus.Won, summer, TimeSlot.Morning, existing);
        var confirmed = await SeedAsync(BookingStatus.Confirmed, summer, TimeSlot.Morning);

        await using var db = _fx.NewContext();
        await db.Database.ExecuteSqlRawAsync(AddBookingConfirmationDeadline.BackfillSql);

        await using var check = _fx.NewContext();
        async Task<DateTime?> DeadlineOf(Guid id) => (await check.Bookings.FindAsync(id))!.ConfirmationDeadline?.ToUniversalTime();
        Assert.Equal(new DateTime(2027, 7, 1, 5, 0, 0, DateTimeKind.Utc), await DeadlineOf(wonMorningSummer));
        Assert.Equal(new DateTime(2027, 7, 1, 11, 0, 0, DateTimeKind.Utc), await DeadlineOf(wonAfternoonSummer));
        Assert.Equal(new DateTime(2027, 1, 15, 6, 0, 0, DateTimeKind.Utc), await DeadlineOf(wonMorningWinter));
        Assert.Equal(existing, await DeadlineOf(wonWithDeadline));   // untouched
        Assert.Null(await DeadlineOf(confirmed));                    // not Won
    }

    [SkippableFact]
    public async Task LostRows_WithAFutureDate_BecomeWaitlisted_PastOnesStayLost()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var future = await SeedAsync(BookingStatus.Lost, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), TimeSlot.Morning);
        var today = await SeedAsync(BookingStatus.Lost, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2)), TimeSlot.Afternoon);
        var past = await SeedAsync(BookingStatus.Lost, new DateOnly(2020, 1, 6), TimeSlot.Morning);
        var cancelled = await SeedAsync(BookingStatus.Cancelled, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), TimeSlot.Morning);

        await using var db = _fx.NewContext();
        await db.Database.ExecuteSqlRawAsync(AddWaitlistedStatus.UpSql);

        await using var check = _fx.NewContext();
        Assert.Equal(BookingStatus.Waitlisted, (await check.Bookings.FindAsync(future))!.Status);
        Assert.Equal(BookingStatus.Waitlisted, (await check.Bookings.FindAsync(today))!.Status);
        Assert.Equal(BookingStatus.Lost, (await check.Bookings.FindAsync(past))!.Status);
        Assert.Equal(BookingStatus.Cancelled, (await check.Bookings.FindAsync(cancelled))!.Status);
    }
}
