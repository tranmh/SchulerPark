namespace SchulerPark.Infrastructure.Jobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Helpers;
using SchulerPark.Core.Interfaces;
using SchulerPark.Infrastructure.Data;

/// <summary>
/// Phase 20 WP1 3.5: catches a lottery that never ran (app down at 22:00, all retries
/// exhausted). Scheduled at 23:30 (target = tomorrow) and 05:00 (target = today) Berlin.
/// For every active location × time slot with Pending bookings but no LotteryRun row it
/// runs that slot's lottery itself, then sweeps Pending bookings dated in the past to
/// Lost. Admins are told whenever it had to intervene.
/// </summary>
[DisableConcurrentExecution(30 * 60)]
public class LotteryWatchdogJob
{
    private readonly AppDbContext _db;
    private readonly ILotteryService _lottery;
    private readonly IAdminNotifier _adminNotifier;
    private readonly TimeProvider _time;
    private readonly ILogger<LotteryWatchdogJob> _logger;

    public LotteryWatchdogJob(AppDbContext db, ILotteryService lottery, IAdminNotifier adminNotifier,
        TimeProvider time, ILogger<LotteryWatchdogJob> logger)
    {
        _db = db;
        _lottery = lottery;
        _adminNotifier = adminNotifier;
        _time = time;
        _logger = logger;
    }

    /// <summary>Hangfire entry point: picks the target date from the Berlin wall clock.</summary>
    public Task ExecuteAsync()
    {
        var berlinNow = DeadlineHelper.ToBerlin(_time.GetUtcNow().UtcDateTime);
        var today = DateOnly.FromDateTime(berlinNow);
        // Evening run (after the 22:00 lottery) checks tomorrow; the early-morning run checks today.
        var target = berlinNow.Hour >= 12 ? today.AddDays(1) : today;
        return ExecuteAsync(target);
    }

    public async Task ExecuteAsync(DateOnly targetDate)
    {
        var today = DeadlineHelper.BerlinToday(_time.GetUtcNow().UtcDateTime);

        // 1. Slots with demand but no run → self-heal.
        var pendingSlots = await _db.Bookings
            .Where(b => b.Date == targetDate && b.Status == BookingStatus.Pending && b.Location.IsActive)
            .Select(b => new { b.LocationId, b.Location.Name, b.TimeSlot })
            .Distinct()
            .ToListAsync();

        var ranSlots = await _db.LotteryRuns
            .Where(r => r.Date == targetDate)
            .Select(r => new { r.LocationId, r.TimeSlot })
            .ToListAsync();
        var ran = ranSlots.Select(r => (r.LocationId, r.TimeSlot)).ToHashSet();

        var healed = new List<string>();
        foreach (var slot in pendingSlots.Where(s => !ran.Contains((s.LocationId, s.TimeSlot))))
        {
            _logger.LogWarning("Watchdog: no lottery run for {Location} {Date} {Slot}; running it now.",
                slot.Name, targetDate, slot.TimeSlot);
            try
            {
                await _lottery.RunLotteryForSlotAsync(slot.LocationId, targetDate, slot.TimeSlot);
                healed.Add($"{slot.Name} — {slot.TimeSlot}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog: lottery for {Location} {Date} {Slot} failed again.",
                    slot.Name, targetDate, slot.TimeSlot);
                healed.Add($"{slot.Name} — {slot.TimeSlot} (FAILED: {ex.Message})");
            }
        }

        // 2. Pending bookings whose day has come and gone were never drawn — close them out.
        // Loaded + updated (not ExecuteUpdate) so the InMemory test provider can run this too.
        var stale = await _db.Bookings
            .Where(b => b.Date < today && b.Status == BookingStatus.Pending)
            .ToListAsync();
        foreach (var booking in stale)
            booking.Status = BookingStatus.Lost;
        if (stale.Count > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogWarning("Watchdog: swept {Count} stale Pending booking(s) to Lost.", stale.Count);
        }

        if (healed.Count == 0 && stale.Count == 0)
        {
            _logger.LogInformation("Watchdog: nothing to do for {Date}.", targetDate);
            return;
        }

        await _adminNotifier.LotteryWatchdogInterventionAsync(targetDate, healed, stale.Count);
    }
}
