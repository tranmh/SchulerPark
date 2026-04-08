namespace SchulerPark.Infrastructure.Jobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Infrastructure.Data;

public class ConfirmationExpiryJob
{
    private readonly AppDbContext _db;
    private readonly ILogger<ConfirmationExpiryJob> _logger;

    public ConfirmationExpiryJob(AppDbContext db, ILogger<ConfirmationExpiryJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var wonBookings = await _db.Bookings
            .Where(b => b.Status == BookingStatus.Won)
            .ToListAsync();

        var expiredCount = 0;
        foreach (var booking in wonBookings)
        {
            if (DeadlineHelper.IsDeadlinePassed(booking.Date, booking.TimeSlot))
            {
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
