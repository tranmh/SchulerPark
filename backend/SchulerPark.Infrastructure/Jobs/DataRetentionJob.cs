namespace SchulerPark.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Infrastructure.Data;

// Bug #1: block overlapping runs — 30-minute lock timeout.
[DisableConcurrentExecution(30 * 60)]
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

        // 1. Delete old bookings (older than 1 year).
        // Bug #17: key on Date (the booking day), the same dimension as lottery-history
        // retention below — not CreatedAt — so a booking and its history are pruned together.
        var oldBookings = await _db.Bookings
            .Where(b => b.Date < cutoffDate)
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
            // Bug #8: delete each user's children AND their row inside one transaction.
            // Previously the child ExecuteDeletes auto-committed while the row removal was
            // deferred to a single trailing SaveChanges — a mid-batch fault then left users
            // with their data gone but the row still present. Per-user transactions make
            // each hard-delete all-or-nothing.
            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.Bookings.Where(b => b.UserId == user.Id).ExecuteDeleteAsync();
            await _db.LotteryHistories.Where(h => h.UserId == user.Id).ExecuteDeleteAsync();
            await _db.RefreshTokens.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();
            await _db.BlockedDays.Where(b => b.BlockedByUserId == user.Id).ExecuteDeleteAsync();
            // Bug #12: erase push subscriptions explicitly (GDPR auditability). The FK cascade
            // would remove them implicitly today, but the deletion code should visibly erase all
            // personal data, and this stays correct if the cascade is ever tightened/loosened.
            await _db.PushSubscriptions.Where(p => p.UserId == user.Id).ExecuteDeleteAsync();
            await _db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
            await tx.CommitAsync();
        }

        if (usersToDelete.Count > 0)
            _logger.LogInformation("Hard-deleted {Count} users past 30-day grace period.", usersToDelete.Count);

        // LotteryRun records are kept (aggregate stats, no personal data)
        _logger.LogInformation("Data retention job completed.");
    }
}
