namespace SchulerPark.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Jobs;
using SchulerPark.Infrastructure.Services;
using Xunit;

// Places winners first-fit, but blows up for one location — simulates a lottery strategy/
// placement failure confined to a single site.
file sealed class FailingForLocationPlacer(Guid failingLocationId) : ISlotPlacer
{
    public Dictionary<Guid, Guid> Place(
        IReadOnlyList<Booking> winners, IReadOnlyList<ParkingSlot> available,
        Location location, IReadOnlyList<GridCell> cells,
        IReadOnlyDictionary<Guid, ParkingSlot> preferredSlotsById)
    {
        if (location.Id == failingLocationId)
            throw new InvalidOperationException("boom: placement failed");

        var map = new Dictionary<Guid, Guid>();
        for (var i = 0; i < winners.Count && i < available.Count; i++)
            map[winners[i].Id] = available[i].Id;
        return map;
    }
}

/// <summary>
/// Phase 20 WP1 3.5: a failing lottery is reported (summary, admin mail, failed job) and
/// the watchdog self-heals a lottery that never ran. Real PostgreSQL: the lottery uses
/// serializable transactions and ExecuteUpdate, which the InMemory provider cannot run.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class LotteryObservabilityTests
{
    private readonly PostgresFixture _fx;
    public LotteryObservabilityTests(PostgresFixture fx) => _fx = fx;

    private static readonly IOptions<AppSettings> AppOptions = Options.Create(new AppSettings { BaseUrl = "http://test" });

    private async Task<(Guid GoodLocation, Guid BadLocation, Guid AdminId)> SeedAsync(DateOnly date)
    {
        var good = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await using var seed = _fx.NewContext();
        seed.Add(new Location { Id = good, Name = $"Good-{good:N}", Address = "A" });
        seed.Add(new Location { Id = bad, Name = $"Bad-{bad:N}", Address = "A" });
        seed.Add(new ParkingSlot { Id = Guid.NewGuid(), LocationId = good, SlotNumber = "G1" });
        seed.Add(new ParkingSlot { Id = Guid.NewGuid(), LocationId = bad, SlotNumber = "B1" });
        seed.Users.Add(new User { Id = adminId, Email = $"admin-{adminId:N}@x.de", DisplayName = "Admin", Role = UserRole.Admin, PreferredLanguage = "en" });

        foreach (var locationId in new[] { good, bad })
        {
            var userId = Guid.NewGuid();
            seed.Users.Add(new User { Id = userId, Email = $"u-{userId:N}@x.de", DisplayName = "U" });
            seed.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(), UserId = userId, LocationId = locationId,
                Date = date, TimeSlot = TimeSlot.Morning, Status = BookingStatus.Pending
            });
        }
        await seed.SaveChangesAsync();
        return (good, bad, adminId);
    }

    [SkippableFact]
    public async Task RunAll_OneLocationThrows_OthersComplete_AndSummaryReportsIt()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var date = new DateOnly(2027, 3, 10);
        var (good, bad, _) = await SeedAsync(date);

        await using var db = _fx.NewContext();
        var service = new LotteryService(db, NullLogger<LotteryService>.Instance,
            new CapturingEmailService(), new RecordingPushService(), new FailingForLocationPlacer(bad));

        var summary = await service.RunAllLotteriesAsync(date);

        // Only the bad location's Morning slot (it has pending bookings) fails; its Afternoon
        // slot has no demand and records an empty run. The good location completes both.
        Assert.Contains(summary.Failures, f => f.LocationId == bad && f.TimeSlot == TimeSlot.Morning && f.Error.Contains("boom"));
        Assert.DoesNotContain(summary.Failures, f => f.LocationId == good);
        Assert.True(summary.HasFailures);

        await using var check = _fx.NewContext();
        Assert.True(await check.LotteryRuns.AnyAsync(r => r.LocationId == good && r.Date == date && r.TimeSlot == TimeSlot.Morning));
        Assert.False(await check.LotteryRuns.AnyAsync(r => r.LocationId == bad && r.Date == date && r.TimeSlot == TimeSlot.Morning));
        var goodBooking = await check.Bookings.SingleAsync(b => b.LocationId == good && b.Date == date);
        Assert.Equal(BookingStatus.Won, goodBooking.Status);
        var badBooking = await check.Bookings.SingleAsync(b => b.LocationId == bad && b.Date == date);
        Assert.Equal(BookingStatus.Pending, badBooking.Status);   // untouched, retryable
    }

    [SkippableFact]
    public async Task LotteryJob_OnFailure_AlertsAdmins_AndThrows()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var date = new DateOnly(2027, 4, 15);
        var (_, bad, adminId) = await SeedAsync(date);
        var email = new CapturingEmailService();

        await using var db = _fx.NewContext();
        var lottery = new LotteryService(db, NullLogger<LotteryService>.Instance,
            email, new RecordingPushService(), new FailingForLocationPlacer(bad));
        var notifier = new AdminNotifier(db, email, AppOptions, NullLogger<AdminNotifier>.Instance);
        // 12:00 UTC the day before → Berlin "tomorrow" is the target date.
        var clock = new MutableTimeProvider { UtcNow = new DateTimeOffset(date.AddDays(-1).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero) };
        var job = new LotteryJob(lottery, notifier, clock, NullLogger<LotteryJob>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(attempt: 1));

        Assert.Contains("boom", ex.Message);
        var adminEmail = (await db.Users.FindAsync(adminId))!.Email;
        Assert.Contains(email.AdminAlerts, a => a.Email == adminEmail && a.Subject.Contains("failed"));
        Assert.Contains(email.AdminAlerts, a => a.Paragraphs.Any(p => p.Contains("boom")));
    }

    [SkippableFact]
    public async Task Watchdog_RunsMissingLottery_SweepsStalePending_AndAlertsAdmins()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var target = new DateOnly(2027, 5, 20);
        var (good, bad, adminId) = await SeedAsync(target);
        var email = new CapturingEmailService();

        // A Pending booking dated in the past that no lottery ever touched.
        Guid staleId;
        await using (var seed = _fx.NewContext())
        {
            var userId = Guid.NewGuid();
            seed.Users.Add(new User { Id = userId, Email = $"s-{userId:N}@x.de", DisplayName = "S" });
            var stale = new Booking
            {
                Id = Guid.NewGuid(), UserId = userId, LocationId = good,
                Date = new DateOnly(2020, 1, 6), TimeSlot = TimeSlot.Morning, Status = BookingStatus.Pending
            };
            seed.Bookings.Add(stale);
            await seed.SaveChangesAsync();
            staleId = stale.Id;
        }

        await using var db = _fx.NewContext();
        var lottery = new LotteryService(db, NullLogger<LotteryService>.Instance,
            email, new RecordingPushService(), new FailingForLocationPlacer(Guid.Empty));
        var notifier = new AdminNotifier(db, email, AppOptions, NullLogger<AdminNotifier>.Instance);
        var clock = new MutableTimeProvider { UtcNow = new DateTimeOffset(target.AddDays(-1).ToDateTime(new TimeOnly(21, 30)), TimeSpan.Zero) };
        var watchdog = new LotteryWatchdogJob(db, lottery, notifier, clock, NullLogger<LotteryWatchdogJob>.Instance);

        // 21:30 UTC = 23:30 Berlin (CEST) → the evening run targets tomorrow = `target`.
        await watchdog.ExecuteAsync();

        await using var check = _fx.NewContext();
        Assert.True(await check.LotteryRuns.AnyAsync(r => r.LocationId == good && r.Date == target && r.TimeSlot == TimeSlot.Morning));
        Assert.True(await check.LotteryRuns.AnyAsync(r => r.LocationId == bad && r.Date == target && r.TimeSlot == TimeSlot.Morning));
        Assert.DoesNotContain(await check.Bookings.Where(b => b.Date == target).ToListAsync(), b => b.Status == BookingStatus.Pending);
        Assert.Equal(BookingStatus.Lost, (await check.Bookings.FindAsync(staleId))!.Status);

        var adminEmail = (await check.Users.FindAsync(adminId))!.Email;
        Assert.Contains(email.AdminAlerts, a => a.Email == adminEmail && a.Subject.Contains("watchdog", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task Watchdog_WhenEverythingRan_StaysQuiet()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var target = new DateOnly(2027, 6, 21);
        var (_, _, _) = await SeedAsync(target);
        var email = new CapturingEmailService();

        await using var db = _fx.NewContext();
        var lottery = new LotteryService(db, NullLogger<LotteryService>.Instance,
            email, new RecordingPushService(), new FailingForLocationPlacer(Guid.Empty));
        await lottery.RunAllLotteriesAsync(target);

        var notifier = new AdminNotifier(db, email, AppOptions, NullLogger<AdminNotifier>.Instance);
        var clock = new MutableTimeProvider { UtcNow = new DateTimeOffset(target.AddDays(-1).ToDateTime(new TimeOnly(21, 30)), TimeSpan.Zero) };
        var watchdog = new LotteryWatchdogJob(db, lottery, notifier, clock, NullLogger<LotteryWatchdogJob>.Instance);

        // Other tests in this collection may have left stale Pending rows behind; only the
        // admin alert for THIS target date matters here, so count alerts before/after.
        var before = email.AdminAlerts.Count;
        await watchdog.ExecuteAsync();
        Assert.DoesNotContain(email.AdminAlerts.Skip(before), a => a.Subject.Contains(target.ToString("dd.MM.yyyy")) && a.Paragraphs.Any(p => p.Contains("no lottery run")));
    }
}
