namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Data;
using SchulerPark.Infrastructure.Services.Strategies;

public class WaitlistService : IWaitlistService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IPushNotificationService _pushService;
    private readonly BookingSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<WaitlistService> _logger;

    public WaitlistService(AppDbContext db, IEmailService emailService,
        IPushNotificationService pushService, IOptions<BookingSettings> settings,
        TimeProvider time, ILogger<WaitlistService> logger)
    {
        _db = db;
        _emailService = emailService;
        _pushService = pushService;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    public async Task TryPromoteWaitlistAsync(Guid locationId, DateOnly date, TimeSlot timeSlot, Guid freedSlotId)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        // WP4 2.3: a slot that frees up is worth handing out until the slot's day-part is over
        // (not just until the confirmation deadline, which left a dead zone of several hours).
        if (now >= DeadlineHelper.SlotEndUtc(date, timeSlot))
        {
            _logger.LogInformation("Waitlist skip: slot has ended for {LocationId} {Date} {TimeSlot}.",
                locationId, date, timeSlot);
            return;
        }

        // Guard: check the freed slot isn't already reassigned (race condition)
        var slotTaken = await _db.Bookings.AnyAsync(b =>
            b.ParkingSlotId == freedSlotId && b.Date == date && b.TimeSlot == timeSlot
            && (b.Status == BookingStatus.Won || b.Status == BookingStatus.Confirmed));
        if (slotTaken)
        {
            _logger.LogInformation("Waitlist skip: freed slot {SlotId} already reassigned.", freedSlotId);
            return;
        }

        // All Waitlisted bookings for the same location+date+timeSlot (WP1 3.3: never
        // promote a booking whose owner has been disabled or deleted in the meantime)
        var candidates = await _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Location)
            .Where(b => b.LocationId == locationId && b.Date == date
                && b.TimeSlot == timeSlot && b.Status == BookingStatus.Waitlisted
                && b.User.DeletedAt == null)
            .ToListAsync();

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Waitlist skip: no Waitlisted bookings for {LocationId} {Date} {TimeSlot}.",
                locationId, date, timeSlot);
            return;
        }

        var history = await LoadHistoryAsync(candidates.Select(b => b.UserId), locationId);
        var promoted = Rank(candidates, history, locationId, freedSlotId).First();

        // WP4 (decision D1): with enough time left the winner confirms as usual; a slot that
        // frees up shortly before or after the deadline is handed over as Confirmed — nobody
        // asleep at 06:30 can react to a ten-minute window, and the slot would stay empty.
        var autoConfirm = DeadlineHelper.IsInsideConfirmationWindow(date, timeSlot, now, _settings);
        promoted.ParkingSlotId = freedSlotId;
        promoted.ReminderSentAt = null;
        if (autoConfirm)
        {
            promoted.Status = BookingStatus.Confirmed;
            promoted.ConfirmedAt = now;
            promoted.ConfirmationDeadline = null;
            promoted.AutoConfirmReason = "waitlist_late_promotion";
        }
        else
        {
            promoted.Status = BookingStatus.Won;
            promoted.ConfirmationDeadline = DeadlineHelper.ComputeDeadline(date, timeSlot, now, _settings);
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (BookingPersistence.IsSlotConflict(ex))
        {
            // A concurrent direct assignment grabbed the freed slot first.
            _logger.LogInformation("Waitlist skip: freed slot {SlotId} taken concurrently.", freedSlotId);
            promoted.Status = BookingStatus.Waitlisted;
            promoted.ParkingSlotId = null;
            promoted.ConfirmedAt = null;
            promoted.ConfirmationDeadline = null;
            promoted.AutoConfirmReason = null;
            return;
        }

        // Load slot for email template
        await _db.Entry(promoted).Reference(b => b.ParkingSlot).LoadAsync();

        _logger.LogInformation(
            "Waitlist promoted booking {BookingId} for user {UserId} at {LocationId} {Date} {TimeSlot} as {Status}.",
            promoted.Id, promoted.UserId, locationId, date, timeSlot, promoted.Status);

        if (autoConfirm)
        {
            _ = _emailService.SendWaitlistAutoConfirmedAsync(promoted);
            _ = _pushService.SendWaitlistAutoConfirmedAsync(promoted);
        }
        else
        {
            _ = _emailService.SendWaitlistWonAsync(promoted);
            _ = _pushService.SendWaitlistWonAsync(promoted);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetWaitlistPositionsAsync(IReadOnlyCollection<Booking> bookings)
    {
        var result = new Dictionary<Guid, int>();
        var mine = bookings.Where(b => b.Status == BookingStatus.Waitlisted).ToList();
        if (mine.Count == 0)
            return result;

        foreach (var group in mine.GroupBy(b => (b.LocationId, b.Date, b.TimeSlot)))
        {
            var (locationId, date, timeSlot) = group.Key;
            var queue = await _db.Bookings
                .Include(b => b.User)
                .Where(b => b.LocationId == locationId && b.Date == date
                    && b.TimeSlot == timeSlot && b.Status == BookingStatus.Waitlisted
                    && b.User.DeletedAt == null)
                .ToListAsync();
            var history = await LoadHistoryAsync(queue.Select(b => b.UserId), locationId);

            var position = 0;
            foreach (var booking in Rank(queue, history, locationId, preferredSlotId: null))
            {
                position++;
                if (group.Any(b => b.Id == booking.Id))
                    result[booking.Id] = position;
            }
        }
        return result;
    }

    private Task<List<LotteryHistory>> LoadHistoryAsync(IEnumerable<Guid> userIds, Guid locationId)
    {
        var ids = userIds.Distinct().ToList();
        return _db.LotteryHistories
            .Where(h => ids.Contains(h.UserId) && h.LocationId == locationId)
            .OrderByDescending(h => h.Date)
            .ToListAsync();
    }

    /// <summary>
    /// Promotion order: a user whose preferred slot is exactly the freed one first, then the
    /// heavier lottery weight (more recent losses), then the earlier booking. Without a
    /// concrete freed slot (<paramref name="preferredSlotId"/> null) the first term is skipped —
    /// that is the "approximate position" shown to users.
    /// </summary>
    internal static IEnumerable<Booking> Rank(IEnumerable<Booking> candidates, List<LotteryHistory> history,
        Guid locationId, Guid? preferredSlotId) =>
        candidates
            .OrderByDescending(b => preferredSlotId.HasValue && b.User?.PreferredSlotId == preferredSlotId)
            .ThenByDescending(b => WeightedHistoryStrategy.CalculateWeight(b.UserId, locationId, history))
            .ThenBy(b => b.CreatedAt);
}
