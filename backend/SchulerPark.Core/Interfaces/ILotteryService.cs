namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Enums;
using SchulerPark.Core.Models;

public interface ILotteryService
{
    Task RunLotteryForSlotAsync(Guid locationId, DateOnly date, TimeSlot timeSlot);

    /// <summary>
    /// Runs every active location × time slot for the date. Keeps going past a failing
    /// slot and reports the failures in the summary instead of swallowing them (WP1 3.5).
    /// </summary>
    Task<LotteryRunSummary> RunAllLotteriesAsync(DateOnly date);
}
