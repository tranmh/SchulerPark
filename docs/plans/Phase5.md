# Phase 5: Lottery System

## Context
Users create bookings with status=Pending. A daily lottery at 10 PM (Europe/Berlin) assigns parking slots for the next day. When demand <= supply, everyone wins. When demand > supply, the configured algorithm picks winners. Three strategies: PureRandom, WeightedHistory (default), RoundRobin. This phase is backend-only — no frontend changes.

---

## Implementation Plan

### Step 1: Core Models & Interfaces

**New:** `Core/Models/LotteryResult.cs`
```csharp
public record LotteryResult(Guid BookingId, Guid UserId, bool Won, Guid? AssignedSlotId);
```

**New:** `Core/Interfaces/ILotteryStrategy.cs`
```csharp
public interface ILotteryStrategy
{
    List<LotteryResult> Execute(
        List<Booking> candidates, List<ParkingSlot> availableSlots, List<LotteryHistory> history);
}
```
- History passed as parameter (not injected) — strategies stay pure/testable

**New:** `Core/Interfaces/ILotteryService.cs`
```csharp
public interface ILotteryService
{
    Task RunLotteryForSlotAsync(Guid locationId, DateOnly date, TimeSlot timeSlot);
    Task RunAllLotteriesAsync(DateOnly date);
}
```

### Step 2: Three Strategy Implementations

All in `Infrastructure/Services/Strategies/`:

| Strategy | Algorithm |
|----------|-----------|
| **PureRandomStrategy** | Shuffle candidates, assign top N to randomly ordered slots |
| **WeightedHistoryStrategy** | Weight = 1.0 + (consecutiveLosses * 0.5), weighted random selection. Consecutive losses counted backward from most recent history. Resets on win. |
| **RoundRobinStrategy** | Sort by last win date ascending (never-won first), ties broken randomly. Take top N. |

Key design: strategies determine WHO wins, not WHICH slot. Slot assignment is always random.

### Step 3: LotteryService (Orchestrator)

**New:** `Infrastructure/Services/LotteryService.cs`

`RunLotteryForSlotAsync(locationId, date, timeSlot)`:
1. **Idempotency**: skip if LotteryRun already exists for (location, date, timeSlot)
2. Fetch pending bookings — if none, record empty run and return
3. Fetch available slots (active, not blocked on that date)
4. Fetch Location.DefaultAlgorithm
5. Fetch LotteryHistory for candidate users at this location
6. If demand <= supply: everyone wins, random slot assignment (no strategy needed)
7. If demand > supply: resolve strategy, execute
8. Apply results: update booking Status (Won/Lost), set ParkingSlotId for winners
9. Write LotteryHistory records for all participants
10. Write LotteryRun record
11. Single `SaveChangesAsync()` — atomic transaction

`RunAllLotteriesAsync(date)`:
- Iterate all active locations × both time slots
- Try/catch per location/slot — failure doesn't block others

**Concurrency safety**: LotteryRun unique index (LocationId, Date, TimeSlot) is the DB-level guard.

### Step 4: Hangfire Job

**New:** `Infrastructure/Jobs/LotteryJob.cs`
- Computes tomorrow's date in Europe/Berlin
- Delegates to `ILotteryService.RunAllLotteriesAsync(tomorrow)`

### Step 5: Program.cs — Hangfire Configuration

**Modify:** `Api/Program.cs`
- Register `ILotteryService` as scoped
- Configure Hangfire with PostgreSQL storage (reuses existing connection string)
- Add Hangfire server
- Register recurring job: `"0 22 * * *"` with Europe/Berlin timezone
- Hangfire dashboard at `/hangfire` (development only)

### Step 6: Admin Lottery Controller

**New:** `Api/Controllers/LotteryController.cs`
- `POST /api/lottery/run?date=` — run all locations for a date
- `POST /api/lottery/run/{locationId}?date=&timeSlot=` — run specific location/slot
- Protected by `[Authorize(Policy = "AdminOnly")]`

### Step 7: Unit Tests (xUnit)

**New project:** `backend/SchulerPark.Tests/SchulerPark.Tests.csproj`
- xUnit + FluentAssertions
- References SchulerPark.Core and SchulerPark.Infrastructure

**New:** `SchulerPark.Tests/Strategies/PureRandomStrategyTests.cs`
- All candidates appear in results
- Winner count = min(candidates, slots)
- Losers have no assigned slot

**New:** `SchulerPark.Tests/Strategies/WeightedHistoryStrategyTests.cs`
- User with 3 consecutive losses has weight 2.5 (verify via deterministic inputs)
- User with a recent win resets to weight 1.0
- All candidates appear in results

**New:** `SchulerPark.Tests/Strategies/RoundRobinStrategyTests.cs`
- Never-won user gets priority over recent winner
- Ties are broken (both users in results, one wins)
- All candidates appear in results

Test approach: Create in-memory entity lists (no DB needed since strategies are pure).

---

## Decisions

- **Hangfire dashboard**: Dev only (no production auth needed)
- **LotteryHistory recording**: Always — even when demand <= supply (everyone wins). This ensures WeightedHistory correctly resets consecutive-loss counters.
- **Phase 5 plan**: Save to `docs/plans/Phase5.md`
- **Tests**: Include xUnit test project with strategy tests

---

## Files Summary

| Action | File |
|--------|------|
| NEW | `Core/Models/LotteryResult.cs` |
| NEW | `Core/Interfaces/ILotteryStrategy.cs` |
| NEW | `Core/Interfaces/ILotteryService.cs` |
| NEW | `Infrastructure/Services/Strategies/PureRandomStrategy.cs` |
| NEW | `Infrastructure/Services/Strategies/WeightedHistoryStrategy.cs` |
| NEW | `Infrastructure/Services/Strategies/RoundRobinStrategy.cs` |
| NEW | `Infrastructure/Services/LotteryService.cs` |
| NEW | `Infrastructure/Jobs/LotteryJob.cs` |
| NEW | `Api/Controllers/LotteryController.cs` |
| MODIFY | `Api/Program.cs` |
| NEW | `SchulerPark.Tests/SchulerPark.Tests.csproj` |
| NEW | `SchulerPark.Tests/Strategies/PureRandomStrategyTests.cs` |
| NEW | `SchulerPark.Tests/Strategies/WeightedHistoryStrategyTests.cs` |
| NEW | `SchulerPark.Tests/Strategies/RoundRobinStrategyTests.cs` |
| NEW | `docs/plans/Phase5.md` |

---

## Verification

- [ ] `dotnet build` succeeds (solution including test project)
- [ ] `dotnet test` — all strategy tests pass
- [ ] App starts, Hangfire dashboard visible at `/hangfire`
- [ ] Recurring job "daily-lottery" registered
- [ ] Manual trigger: `POST /api/lottery/run?date=2026-04-09` (admin only)
- [ ] 2 bookings / 5 slots → both Won with assigned ParkingSlotId
- [ ] 5 bookings / 2 slots → 2 Won, 3 Lost
- [ ] LotteryRun + LotteryHistory records created (including everyone-wins case)
- [ ] Re-run for same (location, date, timeSlot) is a no-op
- [ ] Blocked dates produce 0 available slots
- [ ] Non-admin gets 403 on lottery endpoints
