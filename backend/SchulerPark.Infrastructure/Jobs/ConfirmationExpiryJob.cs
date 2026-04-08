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

    public ConfirmationExpiryJob(AppDbContext db, ILogger<ConfirmationExpiryJob> logger, IEmailService emailService)
    {
        _db = db;
        _logger = logger;
        _emailService = emailService;
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
        foreach (var booking in wonBookings)
        {
            if (DeadlineHelper.IsDeadlinePassed(booking.Date, booking.TimeSlot))
            {
                // Send reminder email before expiring
                _ = _emailService.SendConfirmationReminderAsync(booking);

                booking.Status = BookingStatus.Expired;
                expiredCount++;
            }
        }

        if (expiredCount > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Expired {Count} unconfirmed Won bookings.", expiredCount);
        }
        else
        {
            _logger.LogInformation("No Won bookings past confirmation deadline.");
        }
    }
}
