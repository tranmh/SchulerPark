namespace SchulerPark.Tests.Integration;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Services;
using Xunit;

// Simulates a stale read: the first placement hands out `staleSlotId` (which another
// booking already holds in the DB) regardless of the free list; later calls place first-fit.
file sealed class StaleFirstPlacer(Guid staleSlotId) : ISlotPlacer
{
    public int Calls { get; private set; }

    public Dictionary<Guid, Guid> Place(
        IReadOnlyList<Booking> winners, IReadOnlyList<ParkingSlot> available,
        Location location, IReadOnlyList<GridCell> cells,
        IReadOnlyDictionary<Guid, ParkingSlot> preferredSlotsById)
    {
        Calls++;
        var map = new Dictionary<Guid, Guid>();
        if (Calls == 1)
        {
            foreach (var w in winners) map[w.Id] = staleSlotId;
            return map;
        }
        for (var i = 0; i < winners.Count && i < available.Count; i++)
            map[winners[i].Id] = available[i].Id;
        return map;
    }
}

// Right before the waitlist promotion's UPDATE hits the DB, a competing Won booking on the
// freed slot is committed on a separate connection — the race the 23505 handler exists for.
file sealed class InsertCompetitorBeforeUpdateInterceptor(
    string connectionString, Guid userId, Guid locationId, Guid slotId, DateOnly date) : DbCommandInterceptor
{
    private bool _done;

    private async Task MaybeInsertAsync(DbCommand command, CancellationToken ct)
    {
        if (_done || !command.CommandText.Contains("UPDATE \"Bookings\"")) return;
        _done = true;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var insert = conn.CreateCommand();
        insert.CommandText =
            "INSERT INTO \"Bookings\" (\"UserId\", \"LocationId\", \"ParkingSlotId\", \"Date\", \"TimeSlot\", \"Status\") " +
            "VALUES (@u, @l, @s, @d, 'Morning', 'Won')";
        insert.Parameters.AddWithValue("u", userId);
        insert.Parameters.AddWithValue("l", locationId);
        insert.Parameters.AddWithValue("s", slotId);
        insert.Parameters.AddWithValue("d", date);
        await insert.ExecuteNonQueryAsync(ct);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        await MaybeInsertAsync(command, ct);
        return await base.ReaderExecutingAsync(command, eventData, result, ct);
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        await MaybeInsertAsync(command, ct);
        return await base.NonQueryExecutingAsync(command, eventData, result, ct);
    }
}

/// <summary>
/// §5 backfill: the filtered unique slot index is the DB backstop against two writers
/// grabbing one slot. Direct assignment retries onto another slot; the waitlist promotion
/// backs off. Both need real PostgreSQL (the InMemory provider enforces no indexes).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class SlotConflictRetryTests
{
    private readonly PostgresFixture _fx;
    public SlotConflictRetryTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly FutureDate() => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

    private async Task<(Guid LocationId, Guid SlotA, Guid SlotB, Guid UserId, Guid OtherUserId)> SeedAsync(DateOnly date)
    {
        var locationId = Guid.NewGuid();
        var slotA = Guid.NewGuid();
        var slotB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var seed = _fx.NewContext();
        seed.Add(new Location { Id = locationId, Name = $"L-{locationId:N}", Address = "A" });
        seed.Add(new ParkingSlot { Id = slotA, LocationId = locationId, SlotNumber = "A" });
        seed.Add(new ParkingSlot { Id = slotB, LocationId = locationId, SlotNumber = "B" });
        seed.Users.Add(new User { Id = userId, Email = $"u-{userId:N}@x.de", DisplayName = "U" });
        seed.Users.Add(new User { Id = otherId, Email = $"o-{otherId:N}@x.de", DisplayName = "O" });
        seed.LotteryRuns.Add(new LotteryRun
        {
            Id = Guid.NewGuid(), LocationId = locationId, Date = date, TimeSlot = TimeSlot.Morning,
            Algorithm = LotteryAlgorithm.PureRandom, RanAt = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        return (locationId, slotA, slotB, userId, otherId);
    }

    [SkippableFact]
    public async Task DirectAssignment_SlotTakenConcurrently_RetriesOntoAnotherSlot()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var date = FutureDate();
        var (locationId, slotA, slotB, userId, otherId) = await SeedAsync(date);

        // Someone already holds slot A (committed) — the stale placer will still hand it out first.
        await using (var seed = _fx.NewContext())
        {
            seed.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(), UserId = otherId, LocationId = locationId, ParkingSlotId = slotA,
                Date = date, TimeSlot = TimeSlot.Morning, Status = BookingStatus.Confirmed, ConfirmedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.NewContext();
        var placer = new StaleFirstPlacer(slotA);
        var direct = new DirectAssignmentService(db, placer, NullLogger<DirectAssignmentService>.Instance);
        var email = new CapturingEmailService();
        var service = new BookingService(db, new NoopWaitlistService(), direct, email, new RecordingPushService(),
            Options.Create(new BookingSettings()), TimeProvider.System);

        var (booking, _) = await service.CreateBookingAsync(userId, locationId, date, TimeSlot.Morning);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(slotB, booking.ParkingSlotId);
        Assert.Equal(2, placer.Calls);   // first attempt collided, second landed
        Assert.Contains(email.Sent, e => e.Type == "DirectlyConfirmed" && e.BookingId == booking.Id);

        await using var check = _fx.NewContext();
        var saved = await check.Bookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(slotB, saved.ParkingSlotId);
    }

    [SkippableFact]
    public async Task WaitlistPromotion_SlotTakenConcurrently_BacksOffWithoutError()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var date = FutureDate();
        var (locationId, slotA, _, userId, otherId) = await SeedAsync(date);

        Guid lostId;
        await using (var seed = _fx.NewContext())
        {
            var lost = new Booking
            {
                Id = Guid.NewGuid(), UserId = userId, LocationId = locationId,
                Date = date, TimeSlot = TimeSlot.Morning, Status = BookingStatus.Waitlisted
            };
            seed.Bookings.Add(lost);
            await seed.SaveChangesAsync();
            lostId = lost.Id;
        }

        var interceptor = new InsertCompetitorBeforeUpdateInterceptor(_fx.ConnectionString, otherId, locationId, slotA, date);
        await using var db = _fx.NewContext(interceptor);
        var email = new CapturingEmailService();
        var service = new WaitlistService(db, email, new RecordingPushService(), TestOptions.Booking, TimeProvider.System, NullLogger<WaitlistService>.Instance);

        // The guard sees a free slot, the UPDATE then hits the unique index → handled, no throw.
        await service.TryPromoteWaitlistAsync(locationId, date, TimeSlot.Morning, slotA);

        await using var check = _fx.NewContext();
        var lostAfter = await check.Bookings.SingleAsync(b => b.Id == lostId);
        Assert.Equal(BookingStatus.Waitlisted, lostAfter.Status);
        Assert.Null(lostAfter.ParkingSlotId);
        Assert.Equal(1, await check.Bookings.CountAsync(b => b.ParkingSlotId == slotA && b.Date == date && b.Status == BookingStatus.Won));
        Assert.DoesNotContain(email.Sent, e => e.Type == "WaitlistWon");
    }
}
