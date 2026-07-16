# TDD Bug-Fix Plan — first 20 findings (#1–#20)

**Source:** `Bugs-SchulerPark-master.md`
**Approach:** TDD — write the failing ("red") test first, make it pass with the minimal fix ("green"), then refactor.
**Baseline:** Verified against the latest `master` (Phase 17) on 2026-07-15.

## Status of #1–#20 (what actually needs doing)

| # | Title (short) | Status | In this plan |
|---|---------------|--------|:---:|
| #1 | No `DisableConcurrentExecution` on Hangfire jobs | ✅ **Done** | ✅ |
| #2 | Double-booking: slot uniqueness + concurrency token | ✅ **Done** — index (Phase 17) + xmin rowversion now added | ✅ (remnant) |
| #3 | Placeholder JWT secret, no startup guard | ✅ Fixed (Phase 16/17) | — |
| #4 | Soft-deleted users retain access | ✅ Fixed | — |
| #5 | Account enumeration / login timing oracle | ✅ Fixed | — |
| #6 | No auth rate limiting | ✅ Fixed | — |
| #7 | Cleartext transport / HSTS | ✅ Fixed | — |
| #8 | DataRetentionJob not atomic | ✅ **Done** | ✅ |
| #9 | Soft-delete vs unfiltered unique Email index | ✅ **Done** | ✅ |
| #10 | Lottery race leaves bookings stuck `Pending` | ✅ **Done** | ✅ |
| #11 | Push enable/disable swallows errors | ✅ **Done** | ✅ |
| #12 | Retention never purges push subscriptions | ✅ **Done** | ✅ |
| #13 | No password-strength/input validation | ✅ Fixed | — |
| #14 | Unescaped user fields in HTML email | ✅ **Done** | ✅ |
| #15 | `admin.yml` bootstrap creds in cleartext | ✅ Fixed (by design; documented) | — |
| #16 | DB default password `changeme` in configs | ✅ **Done** — prod (Phase 17) + dev/appsettings/compose now fail-fast | ✅ (remnant) |
| #17 | Retention prunes bookings on `CreatedAt`, history on `Date` | ✅ **Done** | ✅ |
| #18 | ParkingSlot delete (`SetNull`) → slot-less active booking | ✅ **Done** | ✅ |
| #19 | `BlockedDay` allows duplicate blocks | ✅ **Done** | ✅ |
| #20 | Biased shuffle `OrderBy(_ => rng.Next())` in lottery | ✅ **Done** | ✅ |

**Net work: 11 open + 2 partial = 13 fixes.** The 7 fixed bugs are listed only so the numbering is unambiguous — no action.

## Progress log (local working copy `SchulerPark-latest`, not yet committed)

| Bug | Done | Test(s) | Notes |
|-----|:---:|---------|-------|
| #1 | ✅ | `RecurringJobConcurrencyTests` (3) | `[DisableConcurrentExecution(30*60)]` on all 3 jobs |
| #9 | ✅ | `DbIntegrityTests.Reusing_email_of_soft_deleted_user_succeeds` | Filtered unique Email index. **Global query filter deliberately NOT added** — it collides with the admin list/enable-deleted-user flows; report itself calls the index "a safe standalone fix". |
| #18 | ✅ | `DbIntegrityTests.Deleting_slot_with_active_booking_is_blocked` | FK → `Restrict`. No code hard-deletes slots (decommission via `IsActive`), so zero ripple. |
| #19 | ✅ | `DbIntegrityTests.Duplicate_location_block_is_rejected` | Two filtered unique indexes. |
| #2 (xmin) | ✅ | `DbIntegrityTests.Concurrent_update_of_same_booking_throws_concurrency` | `UseXminAsConcurrencyToken` was removed in Npgsql 10 → mapped the `xmin`/`xid` system column manually in `OnModelCreating`, gated to `Database.IsNpgsql()`. Migration `AddBookingConcurrencyToken` is a **no-op** (xmin is a system column; scaffolded AddColumn would fail). |
| #8 | ✅ | `DataRetentionAtomicityTests.Mid_batch_failure_leaves_no_partially_deleted_user` | Per-user `BeginTransaction`/`Commit` in `DataRetentionJob`; user row now deleted via `ExecuteDelete` inside the tx (was `Remove` + trailing `SaveChanges`). Test injects a fault via an EF command interceptor. No retrying execution strategy in `Program.cs`, so explicit transactions are safe. |
| #10 | ✅ | `LotteryRaceTests.Booking_created_during_run_is_not_left_pending` | `Serializable` tx + retry on `40001`/`40P01` around read→assign→record, **plus a post-commit sweep** of stranded `Pending` → `Lost`. Key finding: a lone concurrent insert is a single rw-edge, which PG SSI does **not** abort — so the sweep (not serialization retry) is the deterministic guarantee (verified: test goes red with the sweep disabled). Smarter routing than `Lost` tracked as #56. |
| #12 | ✅ | `DataRetentionTests.Hard_delete_explicitly_erases_push_subscriptions` | Explicit `PushSubscriptions` `ExecuteDelete` in the per-user tx. Cascade already erased them, so the test asserts the job **issues** the delete (recording interceptor) — red without the line. GDPR auditability. |
| #14 | ✅ | `EmailServiceHtmlEncodingTests` (2) | `WebUtility.HtmlEncode` via internal `Enc`/`Greeting` helpers + encoded `BookingDetailsTable`; applied to `DisplayName`, `Location.Name`, `SlotNumber` across all templates. |
| #17 | ✅ | `DataRetentionTests.Old_booking_is_pruned_by_date_even_if_created_recently` | Prune bookings on `Date` (not `CreatedAt`) — same dimension as history. Red-verified. |
| #20 | ✅ | `ShuffleFairnessTests` (2) | `Random.Shuffle` (Fisher–Yates) replaces `OrderBy(_ => rng.Next())` in all 3 strategies + the placer; RoundRobin's biased `ThenBy(rng.Next())` tiebreak replaced by pre-shuffle + stable sort. RNG injectable (ctor) for seedable/auditable tests. |
| #11 | ✅ | `usePushNotifications.test.ts` (3) | `subscribeToPush` now propagates errors (was swallowed as `false`); hook returns a discriminated `{ok}`/`{ok:false, reason:'unsupported'\|'denied'\|'error'}`; `ProfilePage` shows a real error only on `'error'`. Frontend/Vitest. |
| #16 | ✅ | `StartupGuardsTests` (6) | Removed `changeme`: `appsettings` `Default` blanked, dev/override/e2e compose switched to `${DB_PASSWORD:?...}` fail-fast. Added an env-gated startup guard (`StartupGuards.IsUnsafeDbConnectionString`) rejecting a missing/`changeme` connection string outside Dev/Testing. |

**Migration:** `20260715142200_BlockedDayUnique_SlotRestrict_EmailFilter` (covers #9/#18/#19).
**Test infra added:** `PostgresFixture` (Testcontainers, graceful Docker-skip) + `Xunit.SkippableFact`; see `docs/testing-integration-tests.md`.
**Suite:** backend **105 passed** with Docker up (8 integration); 97 passed + 8 skipped without Docker. Frontend **24 passed** (Vitest).

### ✅ All of #1–#20 complete (13 fixes: 11 open + 2 partial). 21 new tests added (84→105 backend, 21→24 frontend).

---

## Prerequisite — ONE fixture required (P2)

DB-integrity bugs (#2-remnant, #8, #9, #10, #18, #19) can only be proven against **real PostgreSQL** — the EF InMemory provider silently ignores unique/filtered indexes, FK `Restrict`, `xmin`, and real transactions. The other 7 fixes (#1, #11, #12, #14, #16, #17, #20) run on pure-unit / in-process / Vitest harnesses and need nothing new.

```csharp
// backend/SchulerPark.Tests/Integration/PostgresFixture.cs
// NuGet: Testcontainers.PostgreSql  (+ existing Npgsql.EntityFrameworkCore.PostgreSQL)
using Microsoft.EntityFrameworkCore;
using SchulerPark.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();          // exercise the real schema/indexes
    }
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```
Tag DB tests `[Collection("Postgres")]` + `[Trait("Category","Integration")]`.
Split runs: `dotnet test --filter "Category!=Integration"` (fast, no Docker) vs `--filter "Category=Integration"` (needs Docker Desktop).

---

## Phase 1 — Concurrency & data integrity (backend)

Ship the three schema changes **#9 / #18 / #19 (+ optional #2 rowversion) in ONE migration**.

### #1 — Prevent overlapping Hangfire runs
**Fix:** `[DisableConcurrentExecution(30 * 60)]` + `using Hangfire;` on the three job classes (`LotteryJob`, `ConfirmationExpiryJob`, `DataRetentionJob`).
**Test** (pure reflection, no infra):
```csharp
[Theory]
[InlineData(typeof(LotteryJob))]
[InlineData(typeof(ConfirmationExpiryJob))]
[InlineData(typeof(DataRetentionJob))]
public void RecurringJobs_DisableConcurrentExecution(Type job)
{
    var attr = job.GetCustomAttribute<DisableConcurrentExecutionAttribute>();
    Assert.NotNull(attr);   // red: currently null on all three
}
```

### #9 — Soft-delete vs unique Email index  *(P2, migration)*
**Fix:** make the Email unique index filtered on `DeletedAt IS NULL` and add `HasQueryFilter(u => u.DeletedAt == null)` in `UserConfiguration.cs`. **Ripple:** add `IgnoreQueryFilters()` to the retention job's user query so it can still hard-delete soft-deleted rows.
```csharp
[Collection("Postgres")] [Trait("Category","Integration")]
public class SoftDeleteEmailTests(PostgresFixture fx)
{
    [Fact] public async Task Reusing_email_of_soft_deleted_user_succeeds()
    {
        await using var db = fx.NewContext();
        db.Users.Add(new User { Email = "a@x.de", DeletedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.Users.Add(new User { Email = "a@x.de" });        // live re-registration
        await db.SaveChangesAsync();                         // red: unique-violation today
        Assert.Equal(1, await db.Users.CountAsync());        // query filter hides the deleted one
    }
}
```

### #18 — ParkingSlot delete must not orphan a live booking  *(P2, migration)*
**Fix:** change the booking→slot FK `OnDelete` from `SetNull` to `Restrict` in `BookingConfiguration.cs`.
```csharp
[Fact] public async Task Deleting_slot_with_active_booking_throws()
{
    await using var db = fx.NewContext();
    // arrange slot + confirmed booking on it, SaveChanges
    db.ParkingSlots.Remove(theSlot);
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); // red: silently sets FK null today
}
```

### #19 — BlockedDay duplicates  *(P2, migration)*
**Fix:** two filtered unique indexes in `BlockedDayConfiguration.cs` — one for whole-location blocks (`ParkingSlotId IS NULL`), one for per-slot blocks (`ParkingSlotId IS NOT NULL`).
```csharp
[Fact] public async Task Duplicate_location_block_throws()
{
    await using var db = fx.NewContext();
    db.BlockedDays.Add(new BlockedDay { LocationId = 1, Date = d, ParkingSlotId = null });
    await db.SaveChangesAsync();
    db.BlockedDays.Add(new BlockedDay { LocationId = 1, Date = d, ParkingSlotId = null });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());  // red today
}
```

### #2 remnant — add optimistic-concurrency token  *(P2, same migration)*
**Fix:** add an `xmin` rowversion (`Property<uint>("xmin").IsRowVersion()`) to `Booking` — the filtered unique slot index already shipped, but two racing confirmations can still clobber each other.
```csharp
[Fact] public async Task Concurrent_update_of_same_booking_throws_concurrency()
{
    await using var a = fx.NewContext(); await using var b = fx.NewContext();
    var ba = await a.Bookings.FindAsync(id);  var bb = await b.Bookings.FindAsync(id);
    ba.Status = BookingStatus.Confirmed; await a.SaveChangesAsync();
    bb.Status = BookingStatus.Cancelled;
    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync()); // red today
}
```

### #8 — Make DataRetentionJob atomic  *(P2)*
**Fix:** wrap each user's child-deletes + row-delete in a per-user `BeginTransaction`/`Commit` in `DataRetentionJob`. A mid-batch fault must never leave a user whose children are deleted but whose row survives.
```csharp
[Fact] public async Task Fault_midway_leaves_no_partially_deleted_user()
{
    // seed 3 stale users; inject a fault on user #2's child delete (e.g. a failing interceptor)
    await Assert.ThrowsAsync<Exception>(() => job.RunAsync());
    await using var db = fx.NewContext();
    // user #1 fully gone; user #2 fully intact (rolled back); NO user with 0 children but a surviving row
    Assert.False(await db.Users.AnyAsync(u => u.Id == user2 && !u.Bookings.Any()));
}
```

### #10 — Lottery race leaves bookings stuck `Pending`  *(P2)*
**Fix:** run the lottery read→apply→sweep for a slot inside one `Serializable` transaction and sweep any remaining `Pending` to `Lost`; treat SQLSTATE `40001` as retryable.
```csharp
[Fact] public async Task Pending_inserted_during_run_is_resolved_not_stuck()
{
    // start a run; concurrently insert a Pending booking for the same date/slot
    await lottery.RunAsync(date);
    await using var db = fx.NewContext();
    Assert.False(await db.Bookings.AnyAsync(b => b.Status == BookingStatus.Pending && b.Date == date)); // red today
}
```

---

## Phase 2 — Backend correctness (no DB needed)

### #12 — Retention must purge push subscriptions  *(InMemory ok)*
**Fix:** explicitly delete `PushSubscriptions` for each hard-deleted user in the retention loop.
```csharp
[Fact] public async Task Hard_deleting_user_removes_their_push_subscriptions()
{
    // seed stale user with a PushSubscription (InMemory)
    await job.RunAsync();
    Assert.Empty(db.PushSubscriptions.Where(p => p.UserId == staleUserId)); // red today
}
```

### #14 — Escape user fields in HTML email  *(pure unit)*
**Fix:** `WebUtility.HtmlEncode(displayName)` in every body builder in `EmailService.cs`.
```csharp
[Fact] public void DisplayName_is_html_encoded_in_body()
{
    var html = EmailService.BuildBookingWonBody(new User { DisplayName = "<script>x</script>" }, /*…*/);
    Assert.DoesNotContain("<script>", html);
    Assert.Contains("&lt;script&gt;", html);   // red today
}
```

### #17 — Consistent retention window  *(InMemory ok)*
**Fix:** prune bookings by `Date` (as history already is), not `CreatedAt`, in `DataRetentionJob`.
```csharp
[Fact] public async Task Old_booking_by_Date_is_pruned_even_if_created_recently()
{
    // booking Date = 2 years ago, CreatedAt = today (backfill scenario)
    await job.RunAsync();
    Assert.False(db.Bookings.Any(b => b.Id == oldByDateId)); // red: survives today
}
```

### #20 — Unbiased, auditable shuffle  *(pure unit)*
**Fix:** replace every `OrderBy(_ => rng.Next())` with `rng.Shuffle(array)` (Fisher–Yates) in `PureRandomStrategy` (15,17), `RoundRobinStrategy` (16 + `.ThenBy` tiebreak at 32), `WeightedHistoryStrategy` (16), and `PreferenceAwareSlotPlacer` (72). Inject the RNG so it's seedable in tests.
```csharp
[Fact] public void Shuffle_distribution_is_uniform_over_positions()
{
    // run the strategy N=100k times with a seeded Random over an oversubscribed slot;
    // assert each input position wins within ±3σ of expected — biased OrderBy fails this.
    var counts = RunManyAndCountWinsByInputPosition(seed: 12345, iterations: 100_000);
    AssertWithinChiSquareTolerance(counts);   // red with OrderBy(_ => rng.Next())
}
```

---

## Phase 3 — Frontend (#11)

### #11 — Push enable/disable must surface errors  *(Vitest + RTL)*
**Fix:** wrap `Notification.requestPermission()` / subscribe in try/catch and return a typed result instead of swallowing failures as `false` (`usePushNotifications.ts:24`, `pushService.ts:39-43`).
```ts
it('reports subscribe failure instead of silent false', async () => {
  vi.spyOn(pushService, 'subscribe').mockRejectedValueOnce(new Error('denied'))
  const { result } = renderHook(() => usePushNotifications())
  await act(() => result.current.enable())
  expect(result.current.status).toEqual({ ok: false, reason: 'error' }) // red: currently just false/no-op
})
```

---

## Phase 4 — Config (#16 remnant)

### #16 remnant — no `changeme` fallback in dev/appsettings
**Fix:** require the DB password via `:?`/blank default (fail-fast) instead of the literal `changeme` in `appsettings.json:13`, `docker-compose.yml:9`, and the override/e2e compose. Prod compose was already fixed.
```csharp
[Fact] public void App_refuses_to_start_with_changeme_password()
{
    // build the config with ConnectionString containing "changeme"
    var ex = Record.Exception(() => BuildAppHost(withPassword: "changeme"));
    Assert.NotNull(ex);   // red: boots happily today
}
```

---

## Recommended order
1. **P2** — Testcontainers fixture.
2. **#1** (trivial, no infra) → the **one migration** (#9 + #18 + #19 + #2-rowversion) → **#8** → **#10**.
3. **Phase 2** backend-correctness (#12, #14, #17, #20) — parallelizable, mostly pure unit.
4. **#11** (frontend) and **#16** (config) — independent tracks, can go anytime.

**Infra map:** PG/Testcontainers → #2, #8, #9, #10, #18, #19. In-process/InMemory → #1, #12, #17. Pure unit → #14, #20. Vitest → #11. Config → #16.
