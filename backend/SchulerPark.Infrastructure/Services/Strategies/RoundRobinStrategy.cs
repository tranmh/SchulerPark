namespace SchulerPark.Infrastructure.Services.Strategies;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;

public class RoundRobinStrategy : ILotteryStrategy
{
    private readonly Random _rng;

    // Bug #20: RNG is injectable so the (unbiased) shuffle is seedable/auditable in tests.
    public RoundRobinStrategy(Random? rng = null) => _rng = rng ?? Random.Shared;

    public List<LotteryResult> Execute(
        List<Booking> candidates,
        List<ParkingSlot> availableSlots,
        List<LotteryHistory> history)
    {
        // Bug #20: Fisher–Yates instead of the biased OrderBy(_ => rng.Next()).
        var slotArr = availableSlots.ToArray();
        _rng.Shuffle(slotArr);
        var slotQueue = new Queue<ParkingSlot>(slotArr);

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

        // Bug #20: pre-shuffle (Fisher–Yates), then a STABLE sort — equal-priority
        // candidates keep their uniformly-random relative order (was a biased ThenBy(rng.Next())).
        var shuffledCandidates = candidates.ToArray();
        _rng.Shuffle(shuffledCandidates);
        var sorted = shuffledCandidates
            .OrderBy(b => lastWinDates[b.Id].HasValue ? 1 : 0) // never-won first
            .ThenBy(b => lastWinDates[b.Id] ?? DateOnly.MinValue) // oldest win next
            .ToList();

        return sorted.Select(b => new LotteryResult(
            b.Id,
            b.UserId,
            Won: slotQueue.Count > 0,
            AssignedSlotId: slotQueue.Count > 0 ? slotQueue.Dequeue().Id : null
        )).ToList();
    }
}
