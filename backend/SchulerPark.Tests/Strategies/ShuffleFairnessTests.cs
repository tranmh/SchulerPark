namespace SchulerPark.Tests.Strategies;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Models;
using SchulerPark.Infrastructure.Services.Strategies;
using Xunit;

// Bug #20: shuffles must be an unbiased Fisher–Yates (Random.Shuffle), seedable for audit —
// not the biased OrderBy(_ => rng.Next()). With a fixed seed the winners are exactly the first
// N of the Fisher–Yates permutation; the old OrderBy produces a different order.
public class ShuffleFairnessTests
{
    [Fact]
    public void PureRandom_winners_match_seeded_fisher_yates()
    {
        var candidates = Enumerable.Range(0, 6)
            .Select(_ => new Booking { Id = Guid.NewGuid(), UserId = Guid.NewGuid() })
            .ToList();
        var slots = Enumerable.Range(0, 2)
            .Select(i => new ParkingSlot { Id = Guid.NewGuid(), SlotNumber = $"S{i}" })
            .ToList();

        const int seed = 20260716;

        // The strategy shuffles candidates first; replicate that exact permutation.
        var expected = candidates.ToArray();
        new Random(seed).Shuffle(expected);
        var expectedWinners = expected.Take(slots.Count).Select(b => b.Id).ToHashSet();

        var results = new PureRandomStrategy(new Random(seed))
            .Execute(candidates, slots, new List<LotteryHistory>());
        var actualWinners = results.Where(r => r.Won).Select(r => r.BookingId).ToHashSet();

        Assert.Equal(slots.Count, actualWinners.Count);
        Assert.Equal(expectedWinners, actualWinners);
    }

    [Fact]
    public void PureRandom_is_deterministic_for_a_given_seed()
    {
        var candidates = Enumerable.Range(0, 8)
            .Select(_ => new Booking { Id = Guid.NewGuid(), UserId = Guid.NewGuid() })
            .ToList();
        var slots = Enumerable.Range(0, 3)
            .Select(i => new ParkingSlot { Id = Guid.NewGuid(), SlotNumber = $"S{i}" })
            .ToList();

        static List<Guid> Winners(IEnumerable<LotteryResult> r) =>
            r.Where(x => x.Won).Select(x => x.BookingId).ToList();

        var first = Winners(new PureRandomStrategy(new Random(42)).Execute(candidates, slots, new()));
        var again = Winners(new PureRandomStrategy(new Random(42)).Execute(candidates, slots, new()));

        Assert.Equal(first, again);   // auditable: same seed -> same outcome
    }
}
