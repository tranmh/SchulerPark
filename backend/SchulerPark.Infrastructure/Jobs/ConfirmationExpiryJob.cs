namespace SchulerPark.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

/// <summary>
/// Runs every 15 minutes (WP4 — deadlines are arbitrary stored instants now, not fixed hours):
/// <list type="bullet">
/// <item>Won bookings past their <see cref="Booking.ConfirmationDeadline"/>: for every
/// location/date/slot the waitlist is counted once and only that many overdue winners expire
/// (latest booking first), each told by mail + push (2.6) with the slot handed to the waitlist.
/// The remaining overdue winners are kept and become Confirmed — taking their slot would help
/// nobody — and are told that too (<see cref="Booking.AutoConfirmReason"/> = kept_nobody_waiting).</item>
/// <item>Once the slot has ended (only reachable after an outage) nobody can act any more, so the
/// same rule is applied silently: no promotion, no mail, no push; kept rows carry kept_slot_ended.</item>
/// <item>Won bookings within an hour of their deadline get one reminder (mail + push), tracked by
/// <see cref="Booking.ReminderSentAt"/> so a second run never repeats it (2.4).</item>
/// <item>Waitlisted bookings whose slot has ended become Lost — the day is over (2.7).</item>
/// </list>
/// </summary>
// Bug #1: block overlapping runs — 30-minute lock timeout.
[DisableConcurrentExecution(30 * 60)]
public class ConfirmationExpiryJob
{
    private static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;
    private readonly ILogger<ConfirmationExpiryJob> _logger;
    private readonly IEmailService _emailService;
    private readonly IPushNotificationService _pushService;
    private readonly IWaitlistService _waitlistService;
    private readonly TimeProvider _time;

    public ConfirmationExpiryJob(AppDbContext db, ILogger<ConfirmationExpiryJob> logger,
        IEmailService emailService, IPushNotificationService pushService,
        IWaitlistService waitlistService, TimeProvider time)
    {
        _db = db;
        _logger = logger;
        _emailService = emailService;
        _pushService = pushService;
        _waitlistService = waitlistService;
        _time = time;
    }

    public async Task ExecuteAsync()
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var wonBookings = await _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .Where(b => b.Status == BookingStatus.Won)
            .ToListAsync();

        var expired = new List<Booking>();
        var kept = new List<Booking>();
        var reminded = new List<Booking>();
        var slotsToPromote = new List<(Guid LocationId, DateOnly Date, TimeSlot TimeSlot, Guid SlotId)>();

        // Bug #44: no "please confirm" reminder past the deadline — the link would just fail
        // with "deadline has passed". Overdue winners are decided below; the rest may be reminded.
        var overdue = wonBookings.Where(b => DeadlineHelper.IsDeadlinePassed(b, now)).ToList();
        foreach (var booking in wonBookings.Except(overdue).Where(b => IsReminderDue(b, now)))
        {
            // Remind while the user can still act — in the hour before the deadline.
            booking.ReminderSentAt = now;
            reminded.Add(booking);
        }

        // One waitlist count per location/date/slot, consumed as winners expire: with three
        // overdue winners and one waitlister exactly one slot changes hands, the other two
        // winners keep theirs. Nothing is saved yet, so a per-booking query would see the same
        // lone waitlister three times.
        var waiting = await CountWaitingAsync(overdue);

        // Deterministic order for who loses when there are fewer waiters than overdue winners:
        // the most recently created booking gives way first.
        foreach (var booking in overdue.OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id))
        {
            var key = (booking.LocationId, booking.Date, booking.TimeSlot);
            var slotEnded = SlotEnded(booking, now);

            if (waiting.TryGetValue(key, out var waiters) && waiters > 0)
            {
                waiting[key] = waiters - 1;

                var freedSlotId = booking.ParkingSlotId;
                booking.Status = BookingStatus.Expired;
                booking.ParkingSlotId = null;
                expired.Add(booking);

                if (freedSlotId.HasValue && !slotEnded)
                    slotsToPromote.Add((booking.LocationId, booking.Date, booking.TimeSlot, freedSlotId.Value));
            }
            else
            {
                // Nobody would get the slot if we took it away, so the winner keeps it. The
                // deadline is moot now (same as WaitlistService's auto-confirm).
                booking.Status = BookingStatus.Confirmed;
                booking.ConfirmedAt = now;
                booking.ConfirmationDeadline = null;
                booking.AutoConfirmReason = slotEnded ? "kept_slot_ended" : "kept_nobody_waiting";
                kept.Add(booking);
            }
        }

        // WP4 2.7: a waitlist entry whose slot has ended can no longer be promoted.
        var today = DeadlineHelper.BerlinToday(now);
        var staleWaitlisted = (await _db.Bookings
                .Where(b => b.Status == BookingStatus.Waitlisted && b.Date <= today)
                .ToListAsync())
            .Where(b => now >= DeadlineHelper.SlotEndUtc(b.Date, b.TimeSlot))
            .ToList();
        foreach (var booking in staleWaitlisted)
            booking.Status = BookingStatus.Lost;

        if (expired.Count + kept.Count + reminded.Count + staleWaitlisted.Count > 0)
            await _db.SaveChangesAsync();

        foreach (var booking in reminded)
        {
            _ = _emailService.SendConfirmationReminderAsync(booking);
            _ = _pushService.SendConfirmationReminderAsync(booking);
        }

        // After slot end (outage recovery) the day is over for everyone involved: status is
        // still settled, but no mail or push — "your spot expired" at 12:01 for a morning the
        // user probably parked through would only confuse.
        if (kept.Count > 0)
        {
            _logger.LogInformation("Kept {Count} unconfirmed Won booking(s) as Confirmed — nobody was waiting ({Silent} past slot end).",
                kept.Count, kept.Count(b => SlotEnded(b, now)));
            foreach (var booking in kept.Where(b => !SlotEnded(b, now)))
            {
                _ = _emailService.SendUnconfirmedBookingKeptAsync(booking);
                _ = _pushService.SendUnconfirmedBookingKeptAsync(booking);
            }
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation("Expired {Count} unconfirmed Won booking(s) ({Silent} past slot end).",
                expired.Count, expired.Count(b => SlotEnded(b, now)));
            foreach (var booking in expired.Where(b => !SlotEnded(b, now)))
            {
                _ = _emailService.SendBookingExpiredAsync(booking);
                _ = _pushService.SendBookingExpiredAsync(booking);
            }

            // Promote waitlisted users for each freed slot
            foreach (var (locationId, date, timeSlot, slotId) in slotsToPromote)
                await _waitlistService.TryPromoteWaitlistAsync(locationId, date, timeSlot, slotId);
        }

        if (overdue.Count == 0)
            _logger.LogInformation("No Won bookings past confirmation deadline.");

        if (staleWaitlisted.Count > 0)
            _logger.LogInformation("Closed {Count} Waitlisted booking(s) whose slot has ended as Lost.", staleWaitlisted.Count);
    }

    private static bool SlotEnded(Booking booking, DateTime now) =>
        now >= DeadlineHelper.SlotEndUtc(booking.Date, booking.TimeSlot);

    /// <summary>
    /// Waitlisted bookings per location/date/slot for the days the overdue winners are on — the
    /// same candidate set WaitlistService promotes from (owner not deleted).
    /// </summary>
    private async Task<Dictionary<(Guid LocationId, DateOnly Date, TimeSlot TimeSlot), int>> CountWaitingAsync(
        List<Booking> overdue)
    {
        if (overdue.Count == 0)
            return new Dictionary<(Guid, DateOnly, TimeSlot), int>();

        var dates = overdue.Select(b => b.Date).Distinct().ToList();
        var waitlisted = await _db.Bookings
            .Where(b => b.Status == BookingStatus.Waitlisted && dates.Contains(b.Date) && b.User.DeletedAt == null)
            .Select(b => new { b.LocationId, b.Date, b.TimeSlot })
            .ToListAsync();

        return waitlisted
            .GroupBy(b => (b.LocationId, b.Date, b.TimeSlot))
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static bool IsReminderDue(Booking booking, DateTime now)
    {
        if (booking.ReminderSentAt.HasValue || booking.ConfirmationDeadline is not { } deadline)
            return false;
        var untilDeadline = deadline - now;
        return untilDeadline > TimeSpan.Zero && untilDeadline <= ReminderLeadTime;
    }
}
