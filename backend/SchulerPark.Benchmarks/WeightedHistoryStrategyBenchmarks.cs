namespace SchulerPark.Benchmarks;

using BenchmarkDotNet.Attributes;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using SchulerPark.Core.Models;
using SchulerPark.Infrastructure.Services.Strategies;

// P7: CalculateWeight re-filters and sorts the whole history per candidate,
// and the draw loop re-sums weights and List.Removes per awarded slot.
[MemoryDiagnoser]
public class WeightedHistoryStrategyBenchmarks
{
    [Params(20, 200, 1000)]
    public int Candidates;

    private List<Booking> _candidates = null!;
    private List<ParkingSlot> _slots = null!;
    private List<LotteryHistory> _history = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        var locationId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 1);
        _candidates = new List<Booking>(Candidates);
        _history = new List<LotteryHistory>();
        for (var i = 0; i < Candidates; i++)
        {
            var userId = Guid.NewGuid();
            _candidates.Add(new Booking
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LocationId = locationId,
                Date = date,
                TimeSlot = (TimeSlot)0,
                CreatedAt = new DateTime(2026, 9, 1).AddMinutes(i),
            });
            // 20 history rows per user: history grows with the candidate count.
            for (var d = 1; d <= 20; d++)
                _history.Add(new LotteryHistory
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    LocationId = locationId,
                    Date = date.AddDays(-d),
                    TimeSlot = (TimeSlot)0,
                    Won = rng.Next(4) == 0,
                });
        }
        // Oversubscribed: half the candidates get a slot.
        _slots = Enumerable.Range(0, Candidates / 2)
            .Select(i => new ParkingSlot { Id = Guid.NewGuid(), LocationId = locationId, SlotNumber = $"S{i}" })
            .ToList();
    }

    [Benchmark]
    public List<LotteryResult> Execute() =>
        new WeightedHistoryStrategy(new Random(7)).Execute(_candidates, _slots, _history);
}
