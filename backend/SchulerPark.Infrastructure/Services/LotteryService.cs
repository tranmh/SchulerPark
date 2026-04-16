namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    public LotteryService(AppDbContext db, ILogger<LotteryService> logger,
        IEmailService emailService, IPushNotificationService pushService)
    {
        _db = db;
        _logger = logger;
        _emailService = emailService;
        _pushService = pushService;
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
        // 1. Idempotency check
        var alreadyRan = await _db.LotteryRuns.AnyAsync(lr =>
            lr.LocationId == locationId && lr.Date == date && lr.TimeSlot == timeSlot);
        if (alreadyRan)
        {
            _logger.LogInformation("Lottery already ran for {LocationId} {Date} {TimeSlot}, skipping.",
                locationId, date, timeSlot);
            return;
        }

        // 2. Fetch pending bookings (include User for email notifications)
        var pendingBookings = await _db.Bookings
            .Include(b => b.User)
            .Where(b => b.LocationId == locationId && b.Date == date
                && b.TimeSlot == timeSlot && b.Status == BookingStatus.Pending)
            .ToListAsync();

        // 3. Fetch location for algorithm
        var location = await _db.Locations.FindAsync(locationId);
        var algorithm = location!.DefaultAlgorithm;

        if (pendingBookings.Count == 0)
        {
            _logger.LogInformation("No pending bookings for {LocationId} {Date} {TimeSlot}.",
                locationId, date, timeSlot);
            RecordRun(locationId, date, timeSlot, algorithm, 0, 0);
            await _db.SaveChangesAsync();
            return;
        }

        // 4. Fetch available slots (active, not blocked)
        var isLocationBlocked = await _db.BlockedDays.AnyAsync(b =>
            b.LocationId == locationId && b.Date == date && b.ParkingSlotId == null);

        List<ParkingSlot> availableSlots;
        if (isLocationBlocked)
        {
            availableSlots = [];
        }
        else
        {
            var blockedSlotIds = await _db.BlockedDays
                .Where(b => b.LocationId == locationId && b.Date == date && b.ParkingSlotId != null)
                .Select(b => b.ParkingSlotId!.Value)
                .ToListAsync();

            availableSlots = await _db.ParkingSlots
                .Where(s => s.LocationId == locationId && s.IsActive
                    && !blockedSlotIds.Contains(s.Id))
                .ToListAsync();
        }

        // 5. Fetch history for candidate users
        var candidateUserIds = pendingBookings.Select(b => b.UserId).Distinct().ToList();
        var history = await _db.LotteryHistories
            .Where(h => candidateUserIds.Contains(h.UserId) && h.LocationId == locationId)
            .OrderByDescending(h => h.Date)
            .ToListAsync();

        // 6. Execute lottery
        List<LotteryResult> results;
        if (availableSlots.Count == 0)
        {
            // All lose
            results = pendingBookings.Select(b =>
                new LotteryResult(b.Id, b.UserId, false, null)).ToList();
        }
        else if (pendingBookings.Count <= availableSlots.Count)
        {
            // Everyone wins — assign slots randomly
            var rng = Random.Shared;
            var shuffledSlots = new Queue<ParkingSlot>(availableSlots.OrderBy(_ => rng.Next()));
            results = pendingBookings.Select(b =>
                new LotteryResult(b.Id, b.UserId, true, shuffledSlots.Dequeue().Id)).ToList();
        }
        else
        {
            // Oversubscribed — run strategy
            var strategy = ResolveStrategy(algorithm);
            results = strategy.Execute(pendingBookings, availableSlots, history);
        }

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
