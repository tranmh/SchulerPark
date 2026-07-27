namespace SchulerPark.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

// Bug #1: block overlapping runs — 30-minute lock timeout.
[DisableConcurrentExecution(30 * 60)]
public class ConfirmationExpiryJob
{
    private readonly AppDbContext _db;
    private readonly ILogger<ConfirmationExpiryJob> _logger;
    private readonly IEmailService _emailService;
    private readonly IWaitlistService _waitlistService;

    public ConfirmationExpiryJob(AppDbContext db, ILogger<ConfirmationExpiryJob> logger,
        IEmailService emailService, IWaitlistService waitlistService)
    {
        _db = db;
        _logger = logger;
        _emailService = emailService;
        _waitlistService = waitlistService;
    }

    public async Task ExecuteAsync()
    {
        var wonBookings = await _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Location)
            .Include(b => b.ParkingSlot)
            .Where(b => b.Status == BookingStatus.Won)
            .ToListAsync();

        var expiredCount = 0;
        var slotsToPromote = new List<(Guid LocationId, DateOnly Date, TimeSlot TimeSlot, Guid SlotId)>();

        foreach (var booking in wonBookings)
        {
            if (DeadlineHelper.IsDeadlinePassed(booking.Date, booking.TimeSlot))
            {
                // Bug #44: deadline has passed — expire and free the slot. Do NOT send a
                // "please confirm" reminder here: the booking is already being expired, so the
                // link would just fail with "deadline has passed". The reminder now fires
                // before the deadline (the else-branch below).
                var freedSlotId = booking.ParkingSlotId;
                booking.Status = BookingStatus.Expired;
                booking.ParkingSlotId = null;
                expiredCount++;

                if (freedSlotId.HasValue)
                {
                    slotsToPromote.Add((booking.LocationId, booking.Date, booking.TimeSlot, freedSlotId.Value));
                }
            }
            else if (IsReminderDue(booking))
            {
                // Bug #44: remind while the user can still act — in the hour before the deadline.
                _ = _emailService.SendConfirmationReminderAsync(booking);
            }
        }

        if (expiredCount > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Expired {Count} unconfirmed Won bookings.", expiredCount);

            // Promote waitlisted users for each freed slot
            foreach (var (locationId, date, timeSlot, slotId) in slotsToPromote)
            {
                await _waitlistService.TryPromoteWaitlistAsync(locationId, date, timeSlot, slotId);
            }
        }
        else
        {
            _logger.LogInformation("No Won bookings past confirmation deadline.");
        }
    }

    // The job runs hourly, so a booking crosses this one-hour, pre-deadline band on a single
    // run → exactly one reminder, with no persisted "reminder sent" flag (no schema change).
    private static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(1);

    private static bool IsReminderDue(Booking booking)
    {
        var untilDeadline =
            DeadlineHelper.GetConfirmationDeadline(booking.Date, booking.TimeSlot) - DateTime.UtcNow;
        return untilDeadline > TimeSpan.Zero && untilDeadline <= ReminderLeadTime;
    }
}
