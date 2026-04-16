namespace SchulerPark.Infrastructure.Jobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

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
                _ = _emailService.SendConfirmationReminderAsync(booking);

                var freedSlotId = booking.ParkingSlotId;
                booking.Status = BookingStatus.Expired;
                booking.ParkingSlotId = null;
                expiredCount++;

                if (freedSlotId.HasValue)
                {
                    slotsToPromote.Add((booking.LocationId, booking.Date, booking.TimeSlot, freedSlotId.Value));
                }
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
}
