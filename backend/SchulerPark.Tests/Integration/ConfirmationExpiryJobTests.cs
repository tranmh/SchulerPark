namespace SchulerPark.Tests.Integration;

using Microsoft.Extensions.Logging.Abstractions;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Jobs;
using Xunit;

file sealed class NoopExpiryWaitlist : IWaitlistService
{
    public Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, TimeSlot timeSlot, Guid freedSlotId)
        => Task.CompletedTask;
}

// Bug #44: the expiry job must not send a "please confirm" reminder for a booking it is
// simultaneously expiring (the link would just fail "deadline has passed"). The reminder
// now fires only in the hour before the deadline. The exact in-window send depends on the
// wall clock (deadlines are fixed at 06:00/13:00 Berlin), so these two deterministic cases
// pin the behaviour that actually mattered: no misleading reminder on expiry, and no
// premature reminder for a booking whose deadline is far away.
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ConfirmationExpiryJobTests
{
    private readonly PostgresFixture _fx;
    public ConfirmationExpiryJobTests(PostgresFixture fx) => _fx = fx;

    private static (Location, User, Booking) Seed(DateOnly date)
    {
        var loc = new Location { Id = Guid.NewGuid(), Name = $"L-{Guid.NewGuid():N}", Address = "A" };
        var user = new User { Id = Guid.NewGuid(), Email = $"u-{Guid.NewGuid():N}@x.de", DisplayName = "U" };
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LocationId = loc.Id,
            Date = date,
            TimeSlot = TimeSlot.Morning,
            Status = BookingStatus.Won,
        };
        return (loc, user, booking);
    }

    [SkippableFact]
    public async Task Expiring_booking_gets_no_confirmation_reminder()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        // Deadline is 06:00 Berlin on a date well in the past → definitely passed.
        var (loc, user, booking) = Seed(new DateOnly(2020, 1, 6));
        await using (var seed = _fx.NewContext())
        {
            seed.AddRange(loc, user);
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
        }

        var email = new CapturingEmailService();
        await using var db = _fx.NewContext();
        var job = new ConfirmationExpiryJob(
            db, NullLogger<ConfirmationExpiryJob>.Instance, email, new NoopExpiryWaitlist());

        await job.ExecuteAsync();

        await using var check = _fx.NewContext();
        var status = (await check.Bookings.FindAsync(booking.Id))!.Status;

        Assert.Equal(BookingStatus.Expired, status);
        // The core of #44: no "please confirm" reminder for a booking being expired.
        Assert.DoesNotContain(email.Sent, e => e.Type == "ConfirmationReminder");
    }

    [SkippableFact]
    public async Task Future_booking_outside_reminder_window_is_untouched()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        // Deadline ~300 days away → well outside the 1-hour pre-deadline reminder window.
        var farFuture = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300);
        var (loc, user, booking) = Seed(farFuture);
        await using (var seed = _fx.NewContext())
        {
            seed.AddRange(loc, user);
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
        }

        var email = new CapturingEmailService();
        await using var db = _fx.NewContext();
        var job = new ConfirmationExpiryJob(
            db, NullLogger<ConfirmationExpiryJob>.Instance, email, new NoopExpiryWaitlist());

        await job.ExecuteAsync();

        await using var check = _fx.NewContext();
        var status = (await check.Bookings.FindAsync(booking.Id))!.Status;

        Assert.Equal(BookingStatus.Won, status);   // not expired
        Assert.Empty(email.Sent);                   // no premature reminder
    }
}
