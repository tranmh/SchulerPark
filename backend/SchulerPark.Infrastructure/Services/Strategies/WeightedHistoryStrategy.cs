namespace SchulerPark.Infrastructure.Services.Strategies;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Models;

public class WeightedHistoryStrategy : ILotteryStrategy
{
    public List<LotteryResult> Execute(
        List<Booking> candidates,
        List<ParkingSlot> availableSlots,
        List<LotteryHistory> history)
    {
        var rng = Random.Shared;
        var slotQueue = new Queue<ParkingSlot>(
            availableSlots.OrderBy(_ => rng.Next()));
        var results = new List<LotteryResult>();

        // Build weights: consecutive losses per user at this location
        var weights = candidates.ToDictionary(
            b => b.Id,
            b => CalculateWeight(b.UserId, b.LocationId, history));

        // Weighted random selection
        var remaining = candidates.ToList();
        while (slotQueue.Count > 0 && remaining.Count > 0)
        {
            var totalWeight = remaining.Sum(b => weights[b.Id]);
            var roll = rng.NextDouble() * totalWeight;
            double cumulative = 0;
            Booking? winner = null;

            foreach (var b in remaining)
            {
                cumulative += weights[b.Id];
                if (roll <= cumulative)
                {
                    winner = b;
                    break;
                }
            }
            winner ??= remaining[^1]; // safety fallback

            results.Add(new LotteryResult(winner.Id, winner.UserId, true, slotQueue.Dequeue().Id));
            remaining.Remove(winner);
        }

        // Losers
        foreach (var b in remaining)
            results.Add(new LotteryResult(b.Id, b.UserId, false, null));

        return results;
    }

    internal static double CalculateWeight(Guid userId, Guid locationId, List<LotteryHistory> history)
    {
        var userHistory = history
            .Where(h => h.UserId == userId && h.LocationId == locationId)
            .OrderByDescending(h => h.Date)
            .ThenByDescending(h => h.TimeSlot)
            .ToList();

        int consecutiveLosses = 0;
        foreach (var h in userHistory)
        {
            if (h.Won) break;
            consecutiveLosses++;
        }

        return 1.0 + (consecutiveLosses * 0.5);
    }
}
