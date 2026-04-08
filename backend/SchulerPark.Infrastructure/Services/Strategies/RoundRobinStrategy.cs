namespace SchulerPark.Infrastructure.Services.Strategies;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;

public class RoundRobinStrategy : ILotteryStrategy
{
    public List<LotteryResult> Execute(
        List<Booking> candidates,
        List<ParkingSlot> availableSlots,
        List<LotteryHistory> history)
    {
        var rng = Random.Shared;
        var slotQueue = new Queue<ParkingSlot>(
            availableSlots.OrderBy(_ => rng.Next()));

        // Last win date per user at relevant location
        var lastWinDates = candidates.ToDictionary(
            b => b.Id,
            b =>
            {
                var lastWin = history
                    .Where(h => h.UserId == b.UserId && h.LocationId == b.LocationId && h.Won)
                    .MaxBy(h => h.Date);
                return lastWin?.Date;
            });

        var sorted = candidates
            .OrderBy(b => lastWinDates[b.Id].HasValue ? 1 : 0) // never-won first
            .ThenBy(b => lastWinDates[b.Id] ?? DateOnly.MinValue) // oldest win next
            .ThenBy(_ => rng.Next()) // random tiebreaker
            .ToList();

        return sorted.Select(b => new LotteryResult(
            b.Id,
            b.UserId,
            Won: slotQueue.Count > 0,
            AssignedSlotId: slotQueue.Count > 0 ? slotQueue.Dequeue().Id : null
        )).ToList();
    }
}
