namespace SchulerPark.Tests.Integration;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Services;
using Xunit;

// Throws an unrecoverable (non-DbUpdate) error on the Nth INSERT into Bookings, to force a
// mid-week failure for the atomicity test (#53).
file sealed class FailOnNthBookingInsertInterceptor(int failOn) : DbCommandInterceptor
{
    private int _count;

    private void MaybeThrow(DbCommand command)
    {
        if (!command.CommandText.Contains("INSERT INTO \"Bookings\"")) return;
        if (++_count == failOn)
            throw new InvalidOperationException($"Injected unrecoverable failure on Bookings insert #{failOn}.");
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        MaybeThrow(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken ct = default)
    {
        MaybeThrow(command);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}

// Lottery hasn't run → booking stays Pending (the normal week-booking path).
file sealed class NotApplicableDirectAssignment : IDirectAssignmentService
{
    public Task<DirectAssignmentOutcome> ApplyAsync(Booking booking)
        => Task.FromResult(DirectAssignmentOutcome.NotApplicable);
}

file sealed class NoopWeekWaitlist : IWaitlistService
{
    public Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, TimeSlot timeSlot, Guid freedSlotId)
        => Task.CompletedTask;
}

file sealed class NoopWeekPush : IPushNotificationService
{
    public Task SendLotteryWonAsync(Booking booking) => Task.CompletedTask;
    public Task SendLotteryLostAsync(Booking booking) => Task.CompletedTask;
    public Task SendWaitlistWonAsync(Booking booking) => Task.CompletedTask;
    public Task SendBookingDirectlyConfirmedAsync(Booking booking) => Task.CompletedTask;
    public Task SendBookingWaitlistedAsync(Booking booking) => Task.CompletedTask;
}

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class BookingServiceDbTests
{
    private readonly PostgresFixture _fx;
    public BookingServiceDbTests(PostgresFixture fx) => _fx = fx;

    // A Monday comfortably in the future (1–2 weeks out) so all Mon–Fri days are bookable
    // (> today, < 1 month) regardless of the UTC/Berlin date boundary.
    private static DateOnly SafeFutureMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var offset = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(offset == 0 ? 7 : offset).AddDays(7);
    }

    // ---- Bug #53: an unrecoverable failure mid-week must roll the whole week back ----
    [SkippableFact]
    public async Task Week_booking_rolls_back_entirely_when_a_day_fails()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var locationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var monday = SafeFutureMonday();

        await using (var seed = _fx.NewContext())
        {
            seed.Add(new Location { Id = locationId, Name = $"L-{locationId:N}", Address = "A" });
            seed.Add(new ParkingSlot { Id = Guid.NewGuid(), LocationId = locationId, SlotNumber = "S1" });
            seed.Users.Add(new User { Id = userId, Email = $"u-{userId:N}@x.de", DisplayName = "U" });
            await seed.SaveChangesAsync();
        }

        var email = new CapturingEmailService();
        await using var db = _fx.NewContext(new FailOnNthBookingInsertInterceptor(4)); // fail on day 4
        var service = new BookingService(
            db, new NoopWeekWaitlist(), new NotApplicableDirectAssignment(), email, new NoopWeekPush());

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.CreateWeekBookingAsync(userId, locationId, monday, TimeSlot.Morning));

        await using var check = _fx.NewContext();
        var count = await check.Bookings.CountAsync(b => b.UserId == userId);

        // RED (no enclosing transaction): days 1–3 already committed → count == 3.
        // GREEN (one transaction): the whole week rolled back → count == 0.
        Assert.Equal(0, count);
        Assert.Empty(email.Sent); // no notifications for a week that never committed
    }
}
