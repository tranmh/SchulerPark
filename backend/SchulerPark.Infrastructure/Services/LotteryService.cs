namespace SchulerPark.Infrastructure.Services;

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services.Strategies;

public class LotteryService : ILotteryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<LotteryService> _logger;
    private readonly IEmailService _emailService;
    private readonly IPushNotificationService _pushService;
    private readonly ISlotPlacer _placer;

    public LotteryService(AppDbContext db, ILogger<LotteryService> logger,
        IEmailService emailService, IPushNotificationService pushService,
        ISlotPlacer placer)
    {
        _db = db;
        _logger = logger;
        _emailService = emailService;
        _pushService = pushService;
        _placer = placer;
    }

    public async Task RunAllLotteriesAsync(DateOnly date)
    {
        var locations = await _db.Locations
            .Where(l => l.IsActive)
            .ToListAsync();

        foreach (var location in locations)
        {
            foreach (var timeSlot in Enum.GetValues<TimeSlot>())
            {
                try
                {
                    await RunLotteryForSlotAsync(location.Id, date, timeSlot);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lottery failed for location {LocationId} on {Date} {TimeSlot}",
                        location.Id, date, timeSlot);
                }
            }
        }
    }

    public async Task RunLotteryForSlotAsync(Guid locationId, DateOnly date, TimeSlot timeSlot)
    {
        // Bug #10: retry a run that hits a serialization/deadlock conflict, so a concurrent
        // write can't wedge it. On the retry the conflicting row is visible and processed.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunLotteryForSlotOnceAsync(locationId, date, timeSlot);
                return;
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "Serialization conflict on lottery for {LocationId} {Date} {TimeSlot}; retrying (attempt {Attempt}).",
                    locationId, date, timeSlot, attempt);
            }
        }
    }

    private async Task RunLotteryForSlotOnceAsync(Guid locationId, DateOnly date, TimeSlot timeSlot)
    {
        // Start each attempt from a clean tracker so a prior aborted attempt's state is discarded.
        _db.ChangeTracker.Clear();

        // Bug #10: read + assign + record run inside one Serializable transaction so two
        // concurrent runs for the same slot can't both process it (double-assignment).
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        // 1. Idempotency check
        var alreadyRan = await _db.LotteryRuns.AnyAsync(lr =>
            lr.LocationId == locationId && lr.Date == date && lr.TimeSlot == timeSlot);
        if (alreadyRan)
        {
            _logger.LogInformation("Lottery already ran for {LocationId} {Date} {TimeSlot}, skipping.",
                locationId, date, timeSlot);
            await tx.RollbackAsync();
            return;
        }

        // 2. Fetch pending bookings (include User + PreferredSlot for placement)
        var pendingBookings = await _db.Bookings
            .Include(b => b.User)
            .Where(b => b.LocationId == locationId && b.Date == date
                && b.TimeSlot == timeSlot && b.Status == BookingStatus.Pending)
            .ToListAsync();

        // 3. Fetch location (with grid cells for placement) + algorithm
        var location = await _db.Locations
            .Include(l => l.GridCells)
            .FirstOrDefaultAsync(l => l.Id == locationId);
        var algorithm = location!.DefaultAlgorithm;

        List<ParkingSlot> availableSlots = [];
        List<LotteryResult> results = [];

        if (pendingBookings.Count == 0)
        {
            _logger.LogInformation("No pending bookings for {LocationId} {Date} {TimeSlot}.",
                locationId, date, timeSlot);
            RecordRun(locationId, date, timeSlot, algorithm, 0, 0);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            await SweepStrandedPendingAsync(locationId, date, timeSlot);
            return;
        }

        // 4. Fetch available slots (active, not blocked)
        availableSlots = await SlotAvailabilityHelper.GetUnblockedActiveSlotsAsync(_db, locationId, date);

        // 5. Fetch history for candidate users
        var candidateUserIds = pendingBookings.Select(b => b.UserId).Distinct().ToList();
        var history = await _db.LotteryHistories
            .Where(h => candidateUserIds.Contains(h.UserId) && h.LocationId == locationId)
            .OrderByDescending(h => h.Date)
            .ToListAsync();

        // 6. Execute lottery — first pick winners, then place slots via preference-aware placer.
        List<Booking> winners;
        List<Booking> losers;
        if (availableSlots.Count == 0)
        {
            winners = [];
            losers = [.. pendingBookings];
        }
        else if (pendingBookings.Count <= availableSlots.Count)
        {
            winners = [.. pendingBookings];
            losers = [];
        }
        else
        {
            // Oversubscribed — strategy picks winners (we discard its slot assignments).
            var strategy = ResolveStrategy(algorithm);
            var strategyResults = strategy.Execute(pendingBookings, availableSlots, history);
            var winnerIds = strategyResults.Where(r => r.Won).Select(r => r.BookingId).ToHashSet();
            winners = pendingBookings.Where(b => winnerIds.Contains(b.Id)).ToList();
            losers = pendingBookings.Where(b => !winnerIds.Contains(b.Id)).ToList();
        }

        // Eager-load preferred slots referenced by winners (used by Pass 2 of the placer).
        var preferredSlotIds = winners
            .Select(w => w.User.PreferredSlotId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var preferredSlots = preferredSlotIds.Count == 0
            ? new Dictionary<Guid, ParkingSlot>()
            : (await _db.ParkingSlots
                .Where(s => preferredSlotIds.Contains(s.Id))
                .ToListAsync())
                .ToDictionary(s => s.Id);

        var gridCells = location.GridCells.ToList();
        var placements = winners.Count == 0
            ? []
            : _placer.Place(winners, availableSlots, location, gridCells, preferredSlots);

        results = winners.Select(b => new LotteryResult(
                b.Id, b.UserId, true,
                placements.TryGetValue(b.Id, out var sid) ? sid : null))
            .Concat(losers.Select(b => new LotteryResult(b.Id, b.UserId, false, null)))
            .ToList();

        // 7. Apply results
        foreach (var result in results)
        {
            var booking = pendingBookings.First(b => b.Id == result.BookingId);
            booking.Status = result.Won ? BookingStatus.Won : BookingStatus.Lost;
            booking.ParkingSlotId = result.AssignedSlotId;

            _db.LotteryHistories.Add(new LotteryHistory
            {
                Id = Guid.NewGuid(),
                UserId = result.UserId,
                LocationId = locationId,
                Date = date,
                TimeSlot = timeSlot,
                Won = result.Won
            });
        }

        // 8. Record the run
        RecordRun(locationId, date, timeSlot, algorithm,
            pendingBookings.Count, availableSlots.Count);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Bug #10: sweep any bookings that landed as Pending during the run (after our read
        // snapshot, so invisible to this transaction) to Lost — otherwise they are stranded
        // forever, since the recorded run blocks any re-run. Runs after commit in its own
        // statement so it sees rows committed concurrently. (#56 tracks smarter routing.)
        await SweepStrandedPendingAsync(locationId, date, timeSlot);

        _logger.LogInformation(
            "Lottery completed for {LocationId} {Date} {TimeSlot}: {Winners} winners, {Losers} losers out of {Total} bookings.",
            locationId, date, timeSlot,
            results.Count(r => r.Won), results.Count(r => !r.Won), results.Count);

        // 9. Send email notifications (fire-and-forget, errors logged inside EmailService)
        foreach (var result in results)
        {
            var booking = pendingBookings.First(b => b.Id == result.BookingId);
            booking.Location = location!;
            if (result.AssignedSlotId.HasValue)
                booking.ParkingSlot = availableSlots.FirstOrDefault(s => s.Id == result.AssignedSlotId.Value);

            if (result.Won)
            {
                _ = _emailService.SendLotteryWonAsync(booking);
                _ = _pushService.SendLotteryWonAsync(booking);
            }
            else
            {
                _ = _emailService.SendLotteryLostAsync(booking);
                _ = _pushService.SendLotteryLostAsync(booking);
            }
        }
    }

    // Mark any still-Pending booking for this slot as Lost so nothing is stranded.
    private async Task SweepStrandedPendingAsync(Guid locationId, DateOnly date, TimeSlot timeSlot)
    {
        var swept = await _db.Bookings
            .Where(b => b.LocationId == locationId && b.Date == date
                && b.TimeSlot == timeSlot && b.Status == BookingStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Lost));

        if (swept > 0)
            _logger.LogWarning(
                "Swept {Count} stranded Pending booking(s) to Lost for {LocationId} {Date} {TimeSlot}.",
                swept, locationId, date, timeSlot);
    }

    // True if the exception (or any inner) is a PostgreSQL serialization failure / deadlock.
    private static bool IsSerializationFailure(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException pg &&
                (pg.SqlState == PostgresErrorCodes.SerializationFailure
                 || pg.SqlState == PostgresErrorCodes.DeadlockDetected))
                return true;
        return false;
    }

    private void RecordRun(Guid locationId, DateOnly date, TimeSlot timeSlot,
        LotteryAlgorithm algorithm, int totalBookings, int availableSlots)
    {
        _db.LotteryRuns.Add(new LotteryRun
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            Date = date,
            TimeSlot = timeSlot,
            Algorithm = algorithm,
            RanAt = DateTime.UtcNow,
            TotalBookings = totalBookings,
            AvailableSlots = availableSlots
        });
    }

    private static ILotteryStrategy ResolveStrategy(LotteryAlgorithm algorithm) => algorithm switch
    {
        LotteryAlgorithm.PureRandom => new PureRandomStrategy(),
        LotteryAlgorithm.WeightedHistory => new WeightedHistoryStrategy(),
        LotteryAlgorithm.RoundRobin => new RoundRobinStrategy(),
        _ => new WeightedHistoryStrategy()
    };
}
