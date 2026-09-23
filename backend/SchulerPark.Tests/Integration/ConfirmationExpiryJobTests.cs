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
        var user = new User { Id = Guid.NewGuid(), Email = $"u-{Guid.NewGuid():N}@x.de", DisplayName = "U" };
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LocationId = loc.Id,
            Date = date ?? Day,
            TimeSlot = slot,
            Status = status,
            ConfirmationDeadline = status == BookingStatus.Won ? deadline ?? Deadline : null,
        };
        await using var seed = _fx.NewContext();
        seed.AddRange(loc, user);
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

    [SkippableFact]
    public async Task PastDeadline_Expires_NotifiesUser_AndSendsNoReminder()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var booking = await SeedAsync(BookingStatus.Won);

        var (email, push) = await RunAsync(Deadline.AddMinutes(1));

        var after = await ReloadAsync(booking.Id);
        Assert.Equal(BookingStatus.Expired, after.Status);
        Assert.Contains(email.Sent, e => e == ("BookingExpired", booking.Id));
        Assert.Contains(push.Sent, e => e == ("BookingExpired", booking.Id));
        // The core of #44: no "please confirm" reminder for a booking being expired.
        Assert.DoesNotContain(email.Sent, e => e.Type == "ConfirmationReminder");
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
