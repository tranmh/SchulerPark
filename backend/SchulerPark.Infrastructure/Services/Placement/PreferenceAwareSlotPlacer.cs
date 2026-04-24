namespace SchulerPark.Infrastructure.Services.Placement;

using SchulerPark.Core.Entities;
using SchulerPark.Core.Interfaces;

public class PreferenceAwareSlotPlacer : ISlotPlacer
{
    private readonly ISlotDistanceMetric _metric;

    public PreferenceAwareSlotPlacer(ISlotDistanceMetric metric)
    {
        _metric = metric;
    }

    public Dictionary<Guid, Guid> Place(
        IReadOnlyList<Booking> winners,
        IReadOnlyList<ParkingSlot> available,
        Location location,
        IReadOnlyList<GridCell> cells,
        IReadOnlyDictionary<Guid, ParkingSlot> preferredSlotsById)
    {
        var assigned = new Dictionary<Guid, Guid>(winners.Count);
        var pool = available.ToList();
        var ordered = winners.OrderBy(w => w.CreatedAt).ToList();

        // Pass 1 — exact preferred-slot match.
        foreach (var w in ordered)
        {
            var prefId = w.User?.PreferredSlotId;
            if (prefId is null) continue;
            var match = pool.FirstOrDefault(s => s.Id == prefId.Value && s.LocationId == location.Id);
            if (match is null) continue;
            assigned[w.Id] = match.Id;
            pool.Remove(match);
        }

        // Pass 2 — nearest to preferred (for winners who didn't get Pass 1).
        foreach (var w in ordered)
        {
            if (assigned.ContainsKey(w.Id)) continue;
            var prefId = w.User?.PreferredSlotId;
            if (prefId is null) continue;

            if (!preferredSlotsById.TryGetValue(prefId.Value, out var pref)) continue;
            if (pref.LocationId != location.Id) continue;

            ParkingSlot? best = null;
            var bestDist = int.MaxValue;
            foreach (var s in pool)
            {
                var d = _metric.Distance(pref, s, location, cells);
                if (d >= bestDist) continue;
                bestDist = d;
                best = s;
            }
            // Resolve ties deterministically: (distance, row, col, slotNumber).
            if (best is not null && bestDist < int.MaxValue)
            {
                best = pool
                    .Where(s => _metric.Distance(pref, s, location, cells) == bestDist)
                    .OrderBy(s => s.GridRow ?? int.MaxValue)
                    .ThenBy(s => s.GridColumn ?? int.MaxValue)
                    .ThenBy(s => s.SlotNumber, StringComparer.Ordinal)
                    .First();
                assigned[w.Id] = best.Id;
                pool.Remove(best);
            }
        }

        // Pass 3 — random for everyone else (preserves current behavior).
        var rng = Random.Shared;
        var shuffled = new Queue<ParkingSlot>(pool.OrderBy(_ => rng.Next()));
        foreach (var w in ordered)
        {
            if (assigned.ContainsKey(w.Id)) continue;
            if (shuffled.Count == 0) break;
            assigned[w.Id] = shuffled.Dequeue().Id;
        }

        return assigned;
    }
}
