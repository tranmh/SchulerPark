namespace SchulerPark.Infrastructure.Services.Strategies;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;

public class PureRandomStrategy : ILotteryStrategy
{
    public List<LotteryResult> Execute(
        List<Booking> candidates,
        List<ParkingSlot> availableSlots,
        List<LotteryHistory> history)
    {
        var rng = Random.Shared;
        var shuffled = candidates.OrderBy(_ => rng.Next()).ToList();
        var slotQueue = new Queue<ParkingSlot>(
            availableSlots.OrderBy(_ => rng.Next()));

        return shuffled.Select(b => new LotteryResult(
            b.Id,
            b.UserId,
            Won: slotQueue.Count > 0,
            AssignedSlotId: slotQueue.Count > 0 ? slotQueue.Dequeue().Id : null
        )).ToList();
    }
}
