# Preferred Parking Slot + Nearest-Available Fallback

## Context

SchulerPark already lets a user set a **preferred location** (`User.PreferredLocationId`); if the preferred location is unavailable on a given date, `BookingService.ResolveLocationAsync` picks a random alternative.

Users now also want to pin a **preferred parking slot** inside their preferred location (e.g., "slot A-12, closest to the entrance"). When a user wins the daily lottery, they should get that slot if it's free; if someone else takes it first, they should get the **nearest available** slot on the grid rather than a random one.

The slot grid already exists (Phase 11): `ParkingSlot.GridRow` / `GridColumn`, plus `GridCell` entities marking obstacles, roads, and entrances per location. Distance-based placement is a natural fit.

Outcome: a user sets a preferred location + preferred slot in their profile; the lottery honors it; admin-level changes (blocked days, inactive slots) degrade gracefully to nearest-available.

## Design summary

- **Add `User.PreferredSlotId`** (nullable FK → `ParkingSlot`, `OnDelete: SetNull`).
- **Invariant**: if `PreferredSlotId` is set, `PreferredLocationId` must be set and equal to the slot's `LocationId`. Enforced in the profile update handler (app-layer validation, no DB check constraint — too noisy at this phase).
- **Do NOT auto-null** `PreferredSlotId` when `PreferredLocationId` changes. Validate at write time and reject mismatches. The frontend naturally resets the slot picker when location changes, so mismatches only appear for hand-crafted requests.
- **Integration strategy — Option B (post-pass rewrite)**: leave `ILotteryStrategy` and the three existing strategies untouched. After the strategy returns winners, discard its `AssignedSlotId` values and run a new preference-aware **slot placement** step. The same placement step also runs in the undersubscribed branch. The unused `AssignedSlotId` field on `LotteryResult` is deprecated (kept for now; strategies keep filling it harmlessly).
- **Algorithm**: two variants behind a single `ISlotDistanceMetric` interface so we can ship Manhattan now and swap in BFS later without re-plumbing. Details in the [Algorithms](#algorithms) section below.
- **Waitlist** (`WaitlistService.TryPromoteWaitlistAsync`): small separate tweak — when a specific slot frees, prefer Lost users whose `PreferredSlotId == freedSlotId`. Only a `ThenBy` on the ranking query; not a shared abstraction with the lottery placement.

---

## Algorithms

Both algorithms implement the same interface and are interchangeable. The placement pipeline (direct-match pass → nearest pass → random pass) is identical — only the distance metric differs.

```csharp
// backend/SchulerPark.Core/Interfaces/ISlotDistanceMetric.cs  (new)
public interface ISlotDistanceMetric
{
    // Returns int.MaxValue if unreachable or coords missing.
    int Distance(ParkingSlot from, ParkingSlot to, Location location, IReadOnlyList<GridCell> cells);
}
```

### Approach A — Manhattan (ship first)

Distance = `|Δrow| + |Δcol|`. Ignores `GridCell` obstacles/roads entirely — walks "as the crow flies" through the grid.

```
int Distance(ParkingSlot a, ParkingSlot b, ...):
    if a.GridRow is null or a.GridCol is null
       or b.GridRow is null or b.GridCol is null:
        return int.MaxValue
    return |a.GridRow - b.GridRow| + |a.GridCol - b.GridCol|
```

- **Pros**: ~5 lines, zero deps on `GridCell`, O(1) per pair, deterministic.
- **Cons**: two slots equidistant by Manhattan can differ substantially by actual walking distance when obstacles sit between them.
- **When it's good enough**: small lots (Weingarten, Netphen) where the grid is mostly open parking. Given lot sizes (tens of slots), the practical error is small.

### Approach B — BFS obstacle-aware (upgrade path)

Distance = shortest path on the grid, 4-connected, through passable cells. Impassable: `CellType.Obstacle`. Passable: `Empty`, `Road`, `Entrance`, `Label`. Slots themselves are treated as passable source/target cells.

```
int Distance(ParkingSlot a, ParkingSlot b, Location loc, cells):
    if any coord missing: return int.MaxValue
    Build R×C boolean passable[] from cells (default passable unless marked Obstacle).
    BFS from (a.GridRow, a.GridCol) over {N, S, E, W} neighbors that are passable.
    Return distance on first dequeue of (b.GridRow, b.GridCol); else int.MaxValue.
```

- **Pros**: reflects real walking distance; respects physical barriers (fences, buildings).
- **Cons**: O(R·C) per query, need to build/cache `passable[]`. Cache per-location per-lottery-run (rebuild on lottery start, O(R·C) once).
- **When to switch**: once Manhattan produces visibly weird assignments in production, or once a location's grid has enough obstacles that Manhattan loses meaning. Swap via DI:

```csharp
services.AddScoped<ISlotDistanceMetric, ManhattanDistanceMetric>();
// later: services.AddScoped<ISlotDistanceMetric, BfsDistanceMetric>();
```

### Shared placement pipeline (identical for both metrics)

Called from both lottery branches (`demand ≤ supply` and `demand > supply`). Takes the winning booking list and the available-slot pool, returns a `Dictionary<Guid, Guid>` (bookingId → slotId).

```
place(winners, available, location, cells, metric):
    # Stable order: oldest booking first → deterministic & explainable.
    winnersOrdered = winners.OrderBy(w => w.CreatedAt)
    assigned = {}

    # Pass 1 — exact preferred-slot match.
    for w in winnersOrdered:
        if w.User.PreferredSlotId is null: continue
        p = available.FirstOrDefault(s => s.Id == w.User.PreferredSlotId)
        if p != null and p.LocationId == location.Id:
            assigned[w.Id] = p.Id
            available.Remove(p)

    # Pass 2 — nearest to preferred (for winners who didn't get Pass 1).
    for w in winnersOrdered where w.Id not in assigned and w.User.PreferredSlotId is not null:
        pref = db.ParkingSlots.Find(w.User.PreferredSlotId)   # may be inactive / null
        if pref is null or pref.LocationId != location.Id: continue
        best = available.Where(s => metric.Distance(pref, s, ...) < int.MaxValue)
                        .OrderBy(s => metric.Distance(pref, s, ...))
                        .ThenBy(s => s.GridRow).ThenBy(s => s.GridColumn).ThenBy(s => s.SlotNumber)
                        .FirstOrDefault()
        if best != null:
            assigned[w.Id] = best.Id
            available.Remove(best)

    # Pass 3 — random for everyone else (preserves current behavior).
    shuffled = available.OrderBy(_ => rng.Next()).ToQueue()
    for w in winnersOrdered where w.Id not in assigned:
        assigned[w.Id] = shuffled.Dequeue().Id

    return assigned
```

Tiebreakers are deterministic: `(distance, row, col, SlotNumber)`. Tests stay stable.

---

## Files to modify / add

### Backend

- **Add** `backend/SchulerPark.Core/Interfaces/ISlotDistanceMetric.cs` — interface above.
- **Add** `backend/SchulerPark.Infrastructure/Services/Placement/ManhattanDistanceMetric.cs` — trivial implementation.
- **Add** `backend/SchulerPark.Infrastructure/Services/Placement/BfsDistanceMetric.cs` — obstacle-aware variant (ship later, or ship both and default-register Manhattan).
- **Add** `backend/SchulerPark.Core/Interfaces/ISlotPlacer.cs` + `backend/SchulerPark.Infrastructure/Services/Placement/PreferenceAwareSlotPlacer.cs` — holds the 3-pass pipeline. Constructor injects `ISlotDistanceMetric`.
- **Modify** `backend/SchulerPark.Core/Entities/User.cs` — add `Guid? PreferredSlotId` + `ParkingSlot? PreferredSlot` navigation.
- **Modify** `backend/SchulerPark.Infrastructure/Data/Configurations/UserConfiguration.cs` — add `HasOne(u => u.PreferredSlot).WithMany().HasForeignKey(u => u.PreferredSlotId).OnDelete(DeleteBehavior.SetNull);`.
- **Add migration** — `dotnet ef migrations add AddUserPreferredSlot` in `SchulerPark.Infrastructure`. Adds nullable FK column.
- **Modify** `backend/SchulerPark.Api/DTOs/Profile/UpdateProfileRequest.cs` — add `Guid? PreferredSlotId`.
- **Modify** `backend/SchulerPark.Api/DTOs/Auth/UserDto.cs` — add `Guid? PreferredSlotId`.
- **Modify** `backend/SchulerPark.Api/Controllers/ProfileController.cs`:
  - In `UpdateProfile`, after validating `PreferredLocationId`: if `request.PreferredSlotId.HasValue`, require `request.PreferredLocationId.HasValue` AND that the slot exists, is active, and `slot.LocationId == request.PreferredLocationId.Value`. Throw `ValidationException` on mismatch.
  - Persist `user.PreferredSlotId = request.PreferredSlotId;`.
  - Include `PreferredSlotId` in `ToDto`.
- **Modify** `backend/SchulerPark.Infrastructure/Services/LotteryService.cs` (lines 113–133):
  - Inject `ISlotPlacer` via constructor.
  - For both the "demand ≤ supply" and "demand > supply" branches: compute winners, then call `_placer.Place(winners, availableSlots, location, cells)` to produce the final slot assignments. Discard any `AssignedSlotId` the strategy filled in.
  - Eager-load `b.User` (already done) + also eager-load `PreferredSlot` via `.Include(b => b.User).ThenInclude(u => u.PreferredSlot)` so Pass 2 doesn't re-query per winner. Load `GridCell`s once per location at the start of `RunLotteryForSlotAsync`.
- **Modify** `backend/SchulerPark.Infrastructure/Services/WaitlistService.cs` (`TryPromoteWaitlistAsync`, ~line 69-72): in the candidate ranking, add `.ThenByDescending(u => u.User.PreferredSlotId == freedSlotId)` as the first tiebreaker after the primary ordering, so a user who specifically preferred this freed slot jumps ahead of equal-weight peers.
- **DI registration** in `backend/SchulerPark.Api/Program.cs`: `AddScoped<ISlotDistanceMetric, ManhattanDistanceMetric>()` and `AddScoped<ISlotPlacer, PreferenceAwareSlotPlacer>()`.

### Frontend

- **Modify** `frontend/src/types/auth.ts` + `frontend/src/types/profile.ts` — add `preferredSlotId?: string | null` to `UserDto` / `UpdateProfileRequest`.
- **Modify** `frontend/src/pages/Profile/ProfilePage.tsx`:
  - After the existing preferred-location dropdown, render a **preferred-slot** dropdown that is **disabled when no preferred location is selected**.
  - When the user changes preferred location, reset `preferredSlotId` to `null` in local form state (prevents the write-time backend error).
  - Source slot options from `GET /api/admin/locations/{id}/slots` (or a new public endpoint `GET /api/locations/{id}/slots` if admin-only is not suitable — prefer creating the public read-only endpoint).
- **Optional**: a small grid preview next to the slot dropdown, highlighting the selected slot, reusing the grid-rendering component from the booking page.

### Tests

- **Unit** (`backend/SchulerPark.Tests/Unit/`):
  - `ManhattanDistanceMetricTests.cs` — same row, same col, diagonals, missing coords.
  - `BfsDistanceMetricTests.cs` — open grid (should match Manhattan), grid with obstacles (should differ), unreachable target.
  - `PreferenceAwareSlotPlacerTests.cs` — single winner with preferred free; two winners with same preferred (first-CreatedAt wins); preferred inactive / not in pool falls through to nearest; all-random fallback when nobody has preference.
- **Integration** (`backend/SchulerPark.Tests/Integration/`):
  - `AuthTests.cs` / new `ProfilePreferredSlotTests.cs` — `PUT /api/profile` accepts + persists a valid slot; rejects a slot from a different location; rejects a slot with no `PreferredLocationId`.
  - Extend `BookingTests.cs` with a lottery test: seed two users both preferring slot A; run lottery; assert winner A gets slot A, winner B gets the Manhattan-nearest available slot.
- **E2E** (`e2e/tests/preferred-slot.spec.ts`, mirror the existing `preferred-location.spec.ts`):
  - Profile page exposes slot dropdown, persists selection.
  - Changing preferred location resets the slot dropdown.
  - API-level: booking flows through to lottery and assigned slot matches expectation.

---

## Verification

1. **Backend build + tests**: `cd backend && dotnet build && dotnet test` — all existing tests must stay green; new unit + integration tests pass.
2. **Migration applies cleanly**: start Docker stack, confirm `dotnet ef database update` produced the new column (or EF auto-apply at startup).
3. **Manual profile flow**: log in via frontend (`http://localhost:5173` or `http://localhost:8080` in docker), set preferred location + preferred slot, reload — selections persist.
4. **Lottery behavior — happy path**: seed two users preferring the same slot in a location with >2 active slots. Manually trigger lottery via `POST /api/lottery/run?date=<tomorrow>`. Confirm DB: one booking has the preferred slot, the other has the slot that is Manhattan-nearest to the preferred slot (inspect via `GET /api/bookings/my` or SQL).
5. **Lottery behavior — fallbacks**: (a) preferred slot inactive → winner still gets nearest slot, no error. (b) preferred slot belongs to a different location (data manually corrupted) → winner falls through to random, warning logged.
6. **Playwright E2E**: `cd e2e && npm test` — all 32 existing + new preferred-slot specs pass against `http://localhost:8080`.
7. **Waitlist tweak**: cancel a Won booking whose slot was preferred by another Lost user for the same date → confirm that Lost user is promoted ahead of other equal-weight candidates.

## What's explicitly out of scope (defer)

- **BFS distance metric implementation** — ship the class stub + interface; default-register Manhattan. Flip over once we have real production data on misassignments.
- **DB-level check constraint** for `PreferredSlotId ↔ PreferredLocationId` consistency — app-layer validation + unit test is enough until the feature stabilizes.
- **Preference-aware winner selection** (boosting odds of winning if you named a slot). Preference is a placement concern, not a selection one; conflating them invites gameability.
- **Admin UI** for auditing preferred slots. Admin already has slot management; not needed for v1.
