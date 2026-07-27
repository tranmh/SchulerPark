namespace SchulerPark.Infrastructure.Services.Strategies;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;

public class PureRandomStrategy : ILotteryStrategy
{
    private readonly Random _rng;

    // Bug #20: RNG is injectable so the (unbiased) shuffle is seedable/auditable in tests.
    public PureRandomStrategy(Random? rng = null) => _rng = rng ?? Random.Shared;

    public List<LotteryResult> Execute(
        List<Booking> candidates,
        List<ParkingSlot> availableSlots,
        List<LotteryHistory> history)
    {
        // Bug #20: Fisher–Yates (Random.Shuffle) instead of the biased OrderBy(_ => rng.Next()).
        var shuffled = candidates.ToArray();
        _rng.Shuffle(shuffled);
        var slotArr = availableSlots.ToArray();
        _rng.Shuffle(slotArr);
        var slotQueue = new Queue<ParkingSlot>(slotArr);

        return shuffled.Select(b => new LotteryResult(
            b.Id,
            b.UserId,
            Won: slotQueue.Count > 0,
            AssignedSlotId: slotQueue.Count > 0 ? slotQueue.Dequeue().Id : null
        )).ToList();
    }
}
