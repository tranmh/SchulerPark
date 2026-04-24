namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services.Strategies;

public class WaitlistService : IWaitlistService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IPushNotificationService _pushService;
    private readonly ILogger<WaitlistService> _logger;

    public WaitlistService(AppDbContext db, IEmailService emailService,
        IPushNotificationService pushService, ILogger<WaitlistService> logger)
    {
        _db = db;
        _emailService = emailService;
        _pushService = pushService;
        _logger = logger;
    }

    public async Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, TimeSlot timeSlot, Guid freedSlotId)
    {
        // Don't promote if the deadline has already passed
        if (DeadlineHelper.IsDeadlinePassed(date, timeSlot))
        {
            _logger.LogInformation("Waitlist skip: deadline passed for {LocationId} {Date} {TimeSlot}.",
                locationId, date, timeSlot);
            return;
        }

        // Guard: check the freed slot isn't already reassigned (race condition)
        var slotTaken = await _db.Bookings.AnyAsync(b =>
            b.ParkingSlotId == freedSlotId && b.Date == date && b.TimeSlot == timeSlot
            && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired);
        if (slotTaken)
        {
            _logger.LogInformation("Waitlist skip: freed slot {SlotId} already reassigned.", freedSlotId);
            return;
        }

        // Find all Lost bookings for the same location+date+timeSlot
        var lostBookings = await _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Location)
            .Where(b => b.LocationId == locationId && b.Date == date
                && b.TimeSlot == timeSlot && b.Status == BookingStatus.Lost)
            .ToListAsync();

        if (lostBookings.Count == 0)
        {
            _logger.LogInformation("Waitlist skip: no Lost bookings for {LocationId} {Date} {TimeSlot}.",
                locationId, date, timeSlot);
            return;
        }

        // Calculate priority using WeightedHistoryStrategy weights
        var candidateUserIds = lostBookings.Select(b => b.UserId).Distinct().ToList();
        var history = await _db.LotteryHistories
            .Where(h => candidateUserIds.Contains(h.UserId) && h.LocationId == locationId)
            .OrderByDescending(h => h.Date)
            .ToListAsync();

        var promoted = lostBookings
            .OrderByDescending(b => b.User.PreferredSlotId == freedSlotId) // boost users who preferred this exact slot
            .ThenByDescending(b => WeightedHistoryStrategy.CalculateWeight(b.UserId, locationId, history))
            .ThenBy(b => b.CreatedAt) // earliest booking wins ties
            .First();

        // Promote to Won
        promoted.Status = BookingStatus.Won;
        promoted.ParkingSlotId = freedSlotId;
        await _db.SaveChangesAsync();

        // Load slot for email template
        await _db.Entry(promoted).Reference(b => b.ParkingSlot).LoadAsync();

        _logger.LogInformation(
            "Waitlist promoted booking {BookingId} for user {UserId} at {LocationId} {Date} {TimeSlot}.",
            promoted.Id, promoted.UserId, locationId, date, timeSlot);

        _ = _emailService.SendWaitlistWonAsync(promoted);
        _ = _pushService.SendWaitlistWonAsync(promoted);
    }
}
