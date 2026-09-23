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
/// <item>Won bookings past their <see cref="Booking.ConfirmationDeadline"/> expire, the user is
/// told (mail + push, 2.6) and the slot goes to the waitlist.</item>
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
        var reminded = new List<Booking>();
        var slotsToPromote = new List<(Guid LocationId, DateOnly Date, TimeSlot TimeSlot, Guid SlotId)>();

        foreach (var booking in wonBookings)
        {
            if (DeadlineHelper.IsDeadlinePassed(booking, now))
            {
                // Bug #44: deadline has passed — expire and free the slot. No "please confirm"
                // reminder here: the link would just fail with "deadline has passed".
                var freedSlotId = booking.ParkingSlotId;
                booking.Status = BookingStatus.Expired;
                booking.ParkingSlotId = null;
                expired.Add(booking);

                if (freedSlotId.HasValue)
                    slotsToPromote.Add((booking.LocationId, booking.Date, booking.TimeSlot, freedSlotId.Value));
            }
            else if (IsReminderDue(booking, now))
            {
                // Bug #44: remind while the user can still act — in the hour before the deadline.
                booking.ReminderSentAt = now;
                reminded.Add(booking);
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

        if (expired.Count + reminded.Count + staleWaitlisted.Count > 0)
            await _db.SaveChangesAsync();

        foreach (var booking in reminded)
        {
            _ = _emailService.SendConfirmationReminderAsync(booking);
            _ = _pushService.SendConfirmationReminderAsync(booking);
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation("Expired {Count} unconfirmed Won booking(s).", expired.Count);
            foreach (var booking in expired)
            {
                _ = _emailService.SendBookingExpiredAsync(booking);
                _ = _pushService.SendBookingExpiredAsync(booking);
            }

            // Promote waitlisted users for each freed slot
            foreach (var (locationId, date, timeSlot, slotId) in slotsToPromote)
                await _waitlistService.TryPromoteWaitlistAsync(locationId, date, timeSlot, slotId);
        }
        else
        {
            _logger.LogInformation("No Won bookings past confirmation deadline.");
        }

        if (staleWaitlisted.Count > 0)
            _logger.LogInformation("Closed {Count} Waitlisted booking(s) whose slot has ended as Lost.", staleWaitlisted.Count);
    }

    private static bool IsReminderDue(Booking booking, DateTime now)
    {
        if (booking.ReminderSentAt.HasValue || booking.ConfirmationDeadline is not { } deadline)
            return false;
        var untilDeadline = deadline - now;
        return untilDeadline > TimeSpan.Zero && untilDeadline <= ReminderLeadTime;
    }
}
