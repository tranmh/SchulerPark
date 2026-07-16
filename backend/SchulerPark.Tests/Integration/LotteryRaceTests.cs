namespace SchulerPark.Tests.Integration;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Services;
using Xunit;

// Simulates the #10 race: right after the lottery reads the pending bookings, a new
// Pending booking for the same slot is inserted+committed on a separate connection
// (as if a user POSTed it mid-run). It is invisible to the lottery's snapshot, so only
// the post-commit sweep can catch it.
file sealed class InsertStragglerAfterPendingReadInterceptor(
    string connectionString, Guid stragglerUserId, Guid locationId, DateOnly date)
    : DbCommandInterceptor
{
    private bool _done;

    private async Task MaybeInsertAsync(DbCommand command, CancellationToken ct)
    {
        if (_done) return;
        // The step-2 pending read is the only Bookings query that also joins Users.
        if (!command.CommandText.Contains("FROM \"Bookings\"")
            || !command.CommandText.Contains("\"Users\"")) return;
        _done = true;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var insert = conn.CreateCommand();
        insert.CommandText =
            "INSERT INTO \"Bookings\" (\"UserId\", \"LocationId\", \"Date\", \"TimeSlot\", \"Status\") " +
            "VALUES (@u, @l, @d, 'Morning', 'Pending')";
        insert.Parameters.AddWithValue("u", stragglerUserId);
        insert.Parameters.AddWithValue("l", locationId);
        insert.Parameters.AddWithValue("d", date);
        await insert.ExecuteNonQueryAsync(ct);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
    {
        await MaybeInsertAsync(command, ct);
        return await base.ReaderExecutedAsync(command, eventData, result, ct);
    }
}

file sealed class NoopPushService : IPushNotificationService
{
    public Task SendLotteryWonAsync(Booking booking) => Task.CompletedTask;
    public Task SendLotteryLostAsync(Booking booking) => Task.CompletedTask;
    public Task SendWaitlistWonAsync(Booking booking) => Task.CompletedTask;
    public Task SendBookingDirectlyConfirmedAsync(Booking booking) => Task.CompletedTask;
    public Task SendBookingWaitlistedAsync(Booking booking) => Task.CompletedTask;
}

// Assigns each winner to the first available slot (enough for a single-slot test).
file sealed class FirstFitSlotPlacer : ISlotPlacer
{
    public Dictionary<Guid, Guid> Place(
        IReadOnlyList<Booking> winners, IReadOnlyList<ParkingSlot> available,
        Location location, IReadOnlyList<GridCell> cells,
        IReadOnlyDictionary<Guid, ParkingSlot> preferredSlotsById)
    {
        var map = new Dictionary<Guid, Guid>();
        for (var i = 0; i < winners.Count && i < available.Count; i++)
            map[winners[i].Id] = available[i].Id;
        return map;
    }
}

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class LotteryRaceTests
{
    private readonly PostgresFixture _fx;
    public LotteryRaceTests(PostgresFixture fx) => _fx = fx;

    // ---- Bug #10: a booking created during the run must not be left stuck Pending ----
    [SkippableFact]
    public async Task Booking_created_during_run_is_not_left_pending()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var date = new DateOnly(2026, 10, 1);
        var locationId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();   // the straggler, inserted mid-run
        Guid bookingA;

        await using (var seed = _fx.NewContext())
        {
            seed.Add(new Location { Id = locationId, Name = "L", Address = "A" });
            seed.Add(new ParkingSlot { Id = Guid.NewGuid(), LocationId = locationId, SlotNumber = "S1" });
            seed.Users.Add(new User { Id = userA, Email = $"a-{userA:N}@x.de", DisplayName = "A" });
            seed.Users.Add(new User { Id = userB, Email = $"b-{userB:N}@x.de", DisplayName = "B" });

            var ba = new Booking
            {
                Id = Guid.NewGuid(),
                UserId = userA,
                LocationId = locationId,
                Date = date,
                TimeSlot = TimeSlot.Morning,
                Status = BookingStatus.Pending,
            };
            seed.Bookings.Add(ba);
            await seed.SaveChangesAsync();
            bookingA = ba.Id;
        }

        var interceptor = new InsertStragglerAfterPendingReadInterceptor(
            _fx.ConnectionString, userB, locationId, date);
        await using var db = _fx.NewContext(interceptor);

        var service = new LotteryService(
            db, NullLogger<LotteryService>.Instance,
            new CapturingEmailService(), new NoopPushService(), new FirstFitSlotPlacer());

        await service.RunLotteryForSlotAsync(locationId, date, TimeSlot.Morning);

        await using var check = _fx.NewContext();
        var statuses = await check.Bookings
            .Where(b => b.LocationId == locationId && b.Date == date)
            .ToDictionaryAsync(b => b.UserId, b => b.Status);

        // A won the single slot; B (created mid-run) must be resolved, not stranded Pending.
        Assert.Equal(BookingStatus.Won, statuses[userA]);
        Assert.True(statuses.ContainsKey(userB), "straggler booking was not inserted");
        Assert.NotEqual(BookingStatus.Pending, statuses[userB]);

        Assert.True(await check.LotteryRuns.AnyAsync(r =>
            r.LocationId == locationId && r.Date == date && r.TimeSlot == TimeSlot.Morning));
    }
}
