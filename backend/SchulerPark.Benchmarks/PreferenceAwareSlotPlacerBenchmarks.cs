namespace SchulerPark.Benchmarks;

using BenchmarkDotNet.Attributes;
using SchulerPark.Core.Entities;
using SchulerPark.Infrastructure.Services.Placement;

// P6: Pass 2 runs a full BFS per (winner, free slot), then again for the tie-break.
// Every winner prefers a slot that is not free, so all of them go through Pass 2.
[MemoryDiagnoser]
public class PreferenceAwareSlotPlacerBenchmarks
{
    [Params(8, 16, 32)]
    public int GridSize;

    private Location _location = null!;
    private List<ParkingSlot> _available = null!;
    private List<GridCell> _cells = null!;
    private List<Booking> _winners = null!;
    private Dictionary<Guid, ParkingSlot> _preferred = null!;

    [GlobalSetup]
    public void Setup()
    {
        _location = new Location { Id = Guid.NewGuid(), Name = "Bench", GridRows = GridSize, GridColumns = GridSize };
        _cells = new List<GridCell>();
        var slots = new List<ParkingSlot>();
        for (var r = 0; r < GridSize; r++)
            for (var c = 0; c < GridSize; c++)
            {
                if (r % 3 == 1) continue; // driving lanes stay free cells
                slots.Add(new ParkingSlot
                {
                    Id = Guid.NewGuid(),
                    LocationId = _location.Id,
                    SlotNumber = $"{r:D3}-{c:D3}",
                    GridRow = r,
                    GridColumn = c,
                });
            }

        // Every other slot is taken (the preferred ones); the rest are free.
        var taken = slots.Where((_, i) => i % 2 == 0).ToList();
        _available = slots.Where((_, i) => i % 2 == 1).ToList();
        _preferred = taken.ToDictionary(s => s.Id);

        var winnerCount = _available.Count / 2;
        _winners = new List<Booking>(winnerCount);
        for (var i = 0; i < winnerCount; i++)
        {
            var user = new User { Id = Guid.NewGuid(), PreferredSlotId = taken[i % taken.Count].Id };
            _winners.Add(new Booking
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                LocationId = _location.Id,
                CreatedAt = new DateTime(2026, 9, 1).AddMinutes(i),
            });
        }
    }

    [Benchmark]
    public Dictionary<Guid, Guid> Place() =>
        new PreferenceAwareSlotPlacer(new BfsDistanceMetric(), new Random(7))
            .Place(_winners, _available, _location, _cells, _preferred);
}
