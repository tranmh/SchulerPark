namespace SchulerPark.Infrastructure.Jobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Infrastructure.Data;

public class DataRetentionJob
{
    private readonly AppDbContext _db;
    private readonly ILogger<DataRetentionJob> _logger;

    public DataRetentionJob(AppDbContext db, ILogger<DataRetentionJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var cutoffDate = DateOnly.FromDateTime(cutoff);

        // 1. Delete old bookings (older than 1 year)
        var oldBookings = await _db.Bookings
            .Where(b => b.CreatedAt < cutoff)
            .ExecuteDeleteAsync();
        if (oldBookings > 0)
            _logger.LogInformation("Deleted {Count} bookings older than 1 year.", oldBookings);

        // 2. Delete old lottery history (older than 1 year)
        var oldHistory = await _db.LotteryHistories
            .Where(h => h.Date < cutoffDate)
            .ExecuteDeleteAsync();
        if (oldHistory > 0)
            _logger.LogInformation("Deleted {Count} lottery history entries older than 1 year.", oldHistory);

        // 3. Hard-delete soft-deleted users after 30-day grace period
        var deletionCutoff = DateTime.UtcNow.AddDays(-30);
        var usersToDelete = await _db.Users
            .Where(u => u.DeletedAt != null && u.DeletedAt < deletionCutoff)
            .ToListAsync();

        foreach (var user in usersToDelete)
        {
            // Delete remaining user data
            await _db.Bookings.Where(b => b.UserId == user.Id).ExecuteDeleteAsync();
            await _db.LotteryHistories.Where(h => h.UserId == user.Id).ExecuteDeleteAsync();
            await _db.RefreshTokens.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();
            await _db.BlockedDays.Where(b => b.BlockedByUserId == user.Id).ExecuteDeleteAsync();
            _db.Users.Remove(user);
        }

        if (usersToDelete.Count > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Hard-deleted {Count} users past 30-day grace period.", usersToDelete.Count);
        }

        // LotteryRun records are kept (aggregate stats, no personal data)
        _logger.LogInformation("Data retention job completed.");
    }
}
