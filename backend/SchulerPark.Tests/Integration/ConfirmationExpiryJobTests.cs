namespace SchulerPark.Tests.Integration;

using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Infrastructure.Jobs;
using Xunit;

/// <summary>
/// Phase 20 WP4: the expiry job works off the stored <see cref="Booking.ConfirmationDeadline"/>
/// and a pinned clock. Reminder once (mail + push), expiry tells the user (mail + push, Bug #44:
/// never a "please confirm" for a booking being expired), Waitlisted past slot end → Lost.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ConfirmationExpiryJobTests
{
    private readonly PostgresFixture _fx;
    public ConfirmationExpiryJobTests(PostgresFixture fx) => _fx = fx;

    private static readonly DateOnly Day = new(2027, 6, 10);   // CEST: 07:00 Berlin = 05:00 UTC
    private static readonly DateTime Deadline = new(2027, 6, 10, 5, 0, 0, DateTimeKind.Utc);

    private async Task<Booking> SeedAsync(BookingStatus status, TimeSlot slot = TimeSlot.Morning, DateTime? deadline = null, DateOnly? date = null)
    {
        var loc = new Location { Id = Guid.NewGuid(), Name = $"L-{Guid.NewGuid():N}", Address = "A" };
        await using var seed = _fx.NewContext();
        seed.Locations.Add(loc);
        await seed.SaveChangesAsync();
        return await SeedAsync(loc.Id, status, slot, deadline, date, createdAt: null);
    }

    /// <summary>A booking at an existing location (so several can share location/date/slot).</summary>
    private async Task<Booking> SeedAsync(Guid locationId, BookingStatus status, TimeSlot slot = TimeSlot.Morning,
        DateTime? deadline = null, DateOnly? date = null, DateTime? createdAt = null)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"u-{Guid.NewGuid():N}@x.de", DisplayName = "U" };
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LocationId = locationId,
            Date = date ?? Day,
            TimeSlot = slot,
            Status = status,
            ConfirmationDeadline = status == BookingStatus.Won ? deadline ?? Deadline : null,
            CreatedAt = createdAt ?? new DateTime(2027, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        await using var seed = _fx.NewContext();
        seed.Users.Add(user);
        seed.Bookings.Add(booking);
        await seed.SaveChangesAsync();
        return booking;
    }

    private async Task<(CapturingEmailService Email, RecordingPushService Push)> RunAsync(DateTime utcNow)
    {
        var email = new CapturingEmailService();
        var push = new RecordingPushService();
        await using var db = _fx.NewContext();
        var clock = new MutableTimeProvider { UtcNow = new DateTimeOffset(utcNow, TimeSpan.Zero) };
        var job = new ConfirmationExpiryJob(db, NullLogger<ConfirmationExpiryJob>.Instance,
            email, push, new NoopWaitlistService(), clock);
        await job.ExecuteAsync();
        return (email, push);
    }

    private async Task<Booking> ReloadAsync(Guid id)
    {
        await using var check = _fx.NewContext();
        return (await check.Bookings.FindAsync(id))!;
    }

    /// <summary>Another user waiting for the same location, date and slot as <paramref name="won"/>.</summary>
    private async Task SeedWaitlisterAsync(Booking won)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"w-{Guid.NewGuid():N}@x.de", DisplayName = "W" };
        await using var seed = _fx.NewContext();
        seed.Users.Add(user);
        seed.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(), UserId = user.Id, LocationId = won.LocationId,
            Date = won.Date, TimeSlot = won.TimeSlot, Status = BookingStatus.Waitlisted
        });
        await seed.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task PastDeadline_WithWaitlister_Expires_NotifiesUser_AndSendsNoReminder()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);
        await SeedWaitlisterAsync(booking);

        var (email, push) = await RunAsync(Deadline.AddMinutes(1));

        var after = await ReloadAsync(booking.Id);
        Assert.Equal(BookingStatus.Expired, after.Status);
        Assert.Null(after.ParkingSlotId);
        Assert.Contains(email.Sent, e => e == ("BookingExpired", booking.Id));
        Assert.Contains(push.Sent, e => e == ("BookingExpired", booking.Id));
        // The core of #44: no "please confirm" reminder for a booking being expired.
        Assert.DoesNotContain(email.Sent, e => e.Type == "ConfirmationReminder");
    }

    [SkippableFact]
    public async Task PastDeadline_NobodyWaiting_KeepsTheBooking_AsConfirmed()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);

        var (email, push) = await RunAsync(Deadline.AddMinutes(1));

        var after = await ReloadAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, after.Status);
        Assert.Equal(Deadline.AddMinutes(1), after.ConfirmedAt!.Value.ToUniversalTime());
        // Audit: a system confirmation, not a click; the deadline is moot (as in WaitlistService).
        Assert.Equal("kept_nobody_waiting", after.AutoConfirmReason);
        Assert.Null(after.ConfirmationDeadline);
        Assert.Contains(email.Sent, e => e == ("UnconfirmedKept", booking.Id));
        Assert.Contains(push.Sent, e => e == ("UnconfirmedKept", booking.Id));
        Assert.DoesNotContain(email.Sent, e => e.Type == "BookingExpired");
    }

    [SkippableFact]
    public async Task PastDeadline_ThreeWinners_OneWaitlister_OnlyTheNewestExpires()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var oldest = await SeedAsync(BookingStatus.Won);
        var middle = await SeedAsync(oldest.LocationId, BookingStatus.Won, createdAt: oldest.CreatedAt.AddHours(1));
        var newest = await SeedAsync(oldest.LocationId, BookingStatus.Won, createdAt: oldest.CreatedAt.AddHours(2));
        await SeedWaitlisterAsync(oldest);

        var (email, _) = await RunAsync(Deadline.AddMinutes(1));

        // One waiter → exactly one slot changes hands, and it is the most recent booking's.
        Assert.Equal(BookingStatus.Expired, (await ReloadAsync(newest.Id)).Status);
        Assert.Equal(BookingStatus.Confirmed, (await ReloadAsync(middle.Id)).Status);
        Assert.Equal(BookingStatus.Confirmed, (await ReloadAsync(oldest.Id)).Status);
        Assert.Contains(email.Sent, e => e == ("BookingExpired", newest.Id));
        Assert.Contains(email.Sent, e => e == ("UnconfirmedKept", middle.Id));
        Assert.Contains(email.Sent, e => e == ("UnconfirmedKept", oldest.Id));
        Assert.Equal(1, email.Sent.Count(e => e.Type == "BookingExpired"));
    }

    [SkippableFact]
    public async Task PastDeadline_WaitlisterAtOtherLocation_DoesNotCount()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);
        var elsewhere = await SeedAsync(BookingStatus.Won);   // different location, same day/slot
        await SeedWaitlisterAsync(elsewhere);

        await RunAsync(Deadline.AddMinutes(1));

        Assert.Equal(BookingStatus.Confirmed, (await ReloadAsync(booking.Id)).Status);
        Assert.Equal(BookingStatus.Expired, (await ReloadAsync(elsewhere.Id)).Status);
    }

    [SkippableFact]
    public async Task PastDeadline_SlotOver_NobodyWaiting_KeptSilently()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);

        // 12:01 Berlin, first run after an outage: the morning is over, the user most likely parked.
        var (email, push) = await RunAsync(DeadlineHelper.SlotEndUtc(Day, TimeSlot.Morning).AddMinutes(1));

        var after = await ReloadAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, after.Status);
        Assert.Equal("kept_slot_ended", after.AutoConfirmReason);
        Assert.Empty(email.Sent);
        Assert.Empty(push.Sent);
    }

    [SkippableFact]
    public async Task PastDeadline_SlotOver_WithWaitlister_ExpiresSilently()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);
        await SeedWaitlisterAsync(booking);

        var (email, push) = await RunAsync(DeadlineHelper.SlotEndUtc(Day, TimeSlot.Morning).AddMinutes(1));

        // Status is settled, but a "your spot expired" mail for a morning that is over helps nobody.
        Assert.Equal(BookingStatus.Expired, (await ReloadAsync(booking.Id)).Status);
        Assert.Empty(email.Sent);
        Assert.Empty(push.Sent);
    }

    [SkippableFact]
    public async Task InsideReminderWindow_RemindsOnce_ByMailAndPush()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);

        // 06:15 Berlin — 45 minutes before the 07:00 deadline.
        var (email1, push1) = await RunAsync(Deadline.AddMinutes(-45));
        Assert.Contains(email1.Sent, e => e == ("ConfirmationReminder", booking.Id));
        Assert.Contains(push1.Sent, e => e == ("ConfirmationReminder", booking.Id));
        var after1 = await ReloadAsync(booking.Id);
        Assert.Equal(BookingStatus.Won, after1.Status);
        Assert.NotNull(after1.ReminderSentAt);

        // 15 minutes later the job runs again: still Won, but no second reminder.
        var (email2, _) = await RunAsync(Deadline.AddMinutes(-30));
        Assert.DoesNotContain(email2.Sent, e => e.Type == "ConfirmationReminder" && e.BookingId == booking.Id);
        Assert.Equal(BookingStatus.Won, (await ReloadAsync(booking.Id)).Status);
    }

    [SkippableFact]
    public async Task FarFromDeadline_IsUntouched()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);

        // The evening before: well outside the 1-hour pre-deadline reminder window.
        var (email, _) = await RunAsync(Deadline.AddHours(-8));

        var after = await ReloadAsync(booking.Id);
        Assert.Equal(BookingStatus.Won, after.Status);
        Assert.Null(after.ReminderSentAt);
        Assert.DoesNotContain(email.Sent, e => e.BookingId == booking.Id);
    }

    [SkippableFact]
    public async Task LateWin_UsesItsOwnStoredDeadline_NotTheDefault()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        // Promoted at 06:30 with a 2 h window → stored deadline 08:30 Berlin = 06:30 UTC.
        var late = await SeedAsync(BookingStatus.Won, deadline: new DateTime(2027, 6, 10, 6, 30, 0, DateTimeKind.Utc));

        // 07:45 Berlin: past the 07:00 default, but this booking is still alive and 45 min before
        // its own deadline → reminder, not expiry.
        var (email, _) = await RunAsync(new DateTime(2027, 6, 10, 5, 45, 0, DateTimeKind.Utc));

        Assert.Equal(BookingStatus.Won, (await ReloadAsync(late.Id)).Status);
        Assert.Contains(email.Sent, e => e == ("ConfirmationReminder", late.Id));
    }

    [SkippableFact]
    public async Task Waitlisted_PastSlotEnd_BecomesLost_ButNotBefore()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var morning = await SeedAsync(BookingStatus.Waitlisted, TimeSlot.Morning);
        var afternoon = await SeedAsync(BookingStatus.Waitlisted, TimeSlot.Afternoon);

        // 12:30 Berlin = 10:30 UTC: the Morning slot has ended, the Afternoon one is running.
        await RunAsync(DeadlineHelper.SlotEndUtc(Day, TimeSlot.Morning).AddMinutes(30));

        Assert.Equal(BookingStatus.Lost, (await ReloadAsync(morning.Id)).Status);
        Assert.Equal(BookingStatus.Waitlisted, (await ReloadAsync(afternoon.Id)).Status);
    }
}
