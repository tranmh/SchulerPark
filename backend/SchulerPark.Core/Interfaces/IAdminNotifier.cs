namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Models;

/// <summary>
/// Fan-out of operational alerts to every active Admin/SuperAdmin, each in their own
/// language (WP1 3.5). Generalises the Phase 18 "pending user" admin mail.
/// </summary>
public interface IAdminNotifier
{
    /// <summary>A verified external registration awaits approval.</summary>
    Task PendingUserAsync(User pendingUser);

    /// <summary>The nightly lottery threw for at least one location × slot.</summary>
    Task LotteryFailedAsync(LotteryRunSummary summary, int attempt);

    /// <summary>The watchdog had to run missing lotteries and/or sweep stale Pending bookings.</summary>
    Task LotteryWatchdogInterventionAsync(DateOnly targetDate, IReadOnlyList<string> healedSlots, int sweptPending);
}
