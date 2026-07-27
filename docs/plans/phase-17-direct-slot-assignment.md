# Direct Slot Assignment After Lottery (+ optional Same-Day Booking)

## Context

Today, every new booking is created as `Pending` and only the nightly lottery (22:00 Berlin, for *tomorrow*) assigns slots. The lottery is idempotent per (location, date, timeSlot) — a `LotteryRun` row blocks any re-run. Consequence: **a booking created after its date's lottery has already run sits `Pending` forever** — no job, waitlist, or manual trigger ever picks it up, even when free slots remain (undersubscribed lottery leaves slots unassigned).

**Requirement:** after the lottery has run, if a user books and a free slot remains, assign it directly — no lottery re-run.

**Decisions (confirmed with owner):**
- Direct assignment → status **`Confirmed` immediately** (slot + `ConfirmedAt` set, no confirmation deadline).
- Lottery ran but **no free slot** → create booking as **`Lost`** so the existing waitlist promotion (`WaitlistService.TryPromoteWaitlistAsync`, which only considers `Lost`) picks it up when a slot frees.
- **No `LotteryHistory` row** written (fairness weights untouched).
- Same-day booking: **deferred** — Part B below is the reference design for a later follow-up; only Part A is in scope now.

---

# Part A — Core feature (approved scope)

## A1. Extract shared slot-availability helper (pure refactor first)

New `backend/SchulerPark.Infrastructure/Services/SlotAvailabilityHelper.cs`:
```csharp
public static class SlotAvailabilityHelper
{
    // Active slots at the location minus BlockedDay blocks
    // (whole-location block => empty list; per-slot blocks excluded).
    public static Task<List<ParkingSlot>> GetUnblockedActiveSlotsAsync(
        AppDbContext db, Guid locationId, DateOnly date)
}
```
Body = verbatim move of `LotteryService.RunLotteryForSlotAsync` lines ~89–107; replace that block in `LotteryService.cs` with a call. Run existing tests before continuing.

## A2. New `DirectAssignmentService`

- `backend/SchulerPark.Core/Interfaces/IDirectAssignmentService.cs`:
  - `enum DirectAssignmentOutcome { NotApplicable, AssignedConfirmed, WaitlistedLost }`
  - `Task<DirectAssignmentOutcome> ApplyAsync(Booking booking)` — mutates the not-yet-saved booking; does NOT SaveChanges; does NOT write LotteryHistory.
- `backend/SchulerPark.Infrastructure/Services/DirectAssignmentService.cs` (deps: `AppDbContext`, `ISlotPlacer`, logger). Logic:
  1. Lottery-ran check = same query as the idempotency guard (`LotteryService.cs:57-58`): any `LotteryRun` for (locationId, date, timeSlot). None → `NotApplicable` (stays Pending).
  2. Free slots = `GetUnblockedActiveSlotsAsync(...)` **set-minus** slots held by `Won`/`Confirmed` bookings for that (location, date, timeSlot) (only those statuses carry `ParkingSlotId`).
  3. Empty → `Status = Lost`, `ParkingSlotId = null` → `WaitlistedLost`.
  4. Else place via existing `ISlotPlacer.Place(new[]{ booking }, freeSlots, location, gridCells, preferredSlotsById)` (verified: takes `IReadOnlyList<Booking>`; load `booking.User` nav + location `GridCells` first, mirror `LotteryService.cs:74-76,141-152`) → preferred → nearest → random.
  5. Set `ParkingSlotId`, `Status = Confirmed`, `ConfirmedAt = DateTime.UtcNow` → `AssignedConfirmed`.
- DI: `builder.Services.AddScoped<IDirectAssignmentService, DirectAssignmentService>();` in `Program.cs` (~line 197).

## A3. Wire into `BookingService` + race guard

`backend/SchulerPark.Infrastructure/Services/BookingService.cs`:
- Ctor gains `IDirectAssignmentService`, `IEmailService`, `IPushNotificationService`.
- `CreateBookingAsync` (:54-65): build entity (Pending) → `ApplyAsync(booking)` → Add → save via new `SaveWithSlotConflictRetryAsync` → load `ParkingSlot` nav when assigned → send notifications per outcome (A5).
- `CreateWeekBookingAsync`: same per day inside the loop; **switch batch save (:160) to per-day saves** through the retry helper (needed to attribute slot-conflict retries to a day; days are distinct dates so semantics otherwise unchanged). Keep `created.Count == 0` guard.

**Race guard (two users, last free slot):**
- App-level: `ApplyAsync`'s occupied-slot re-query (same pattern as `WaitlistService.cs:38-45`).
- DB backstop: new filtered unique index in `backend/SchulerPark.Infrastructure/Data/Configurations/BookingConfiguration.cs`:
  ```csharp
  builder.HasIndex(b => new { b.ParkingSlotId, b.Date, b.TimeSlot })
      .IsUnique()
      .HasFilter("\"ParkingSlotId\" IS NOT NULL AND \"Status\" IN ('Won', 'Confirmed')");
  ```
  + EF migration `AddSlotUniquePerDateTimeSlotIndex`. (Integration tests run EF InMemory → index not enforced there; it's the production backstop.)
- Retry helper: catch `DbUpdateException` → `PostgresException` SqlState 23505 on that constraint (≤3 attempts): reset booking to Pending/no-slot, re-run `ApplyAsync` (loser now sees the committed competitor and picks another slot or goes `Lost`), retry save.
- Hardening: wrap `WaitlistService.TryPromoteWaitlistAsync` save (:78) in the same 23505 catch → log-skip (it can now race direct assignment).

## A4. API surface + controller email gating

- **No DTO changes.** Client branches on `status`: `Confirmed` + slot + `confirmedAt` (direct), `Lost` (waitlisted), `Pending` (pre-lottery, unchanged). `ConfirmationDeadline` already only set for `Won`.
- `BookingController.cs` (:39, :59-60): send the existing "booking created / lottery at 10 PM" email **only when `Status == Pending`** (it's misleading otherwise).

## A5. Notifications (two new templates)

- `IEmailService` + `IPushNotificationService`: add `SendBookingDirectlyConfirmedAsync(Booking)` and `SendBookingWaitlistedAsync(Booking)`.
- `EmailService.cs`: implement both reusing `BookingDetailsTable`/`BuildHtml`. Confirmed copy ~ "lottery already ran, slot {n} assigned, booking Confirmed, no action needed" (modeled on `SendWaitlistWonAsync` minus deadline). Waitlisted copy ~ "all spots taken; you're on the waitlist, auto-assigned if one frees".
- `PushNotificationService.cs`: matching payloads (style of :29-55).
- `Fakes.cs` `CapturingEmailService`: implement new methods; extend to record per-type sends so tests can assert "direct-confirmed sent, created-email suppressed".
- Sent fire-and-forget from `BookingService` after successful save (WaitlistService pattern).

## A6. Frontend

- `frontend/src/pages/Booking/BookingPage.tsx`:
  - Single path (:170-175): show result panel when `status !== 'Pending'` (or fallbackReason); branch panel (:189-213): `Confirmed` → success + `booking.directAssignedMessage` with slot number; `Lost` → amber + `booking.waitlistedMessage`.
  - Week path (:163-168): show summary when any created booking `status !== 'Pending'`; list "assigned directly" and "full, waitlisted" days alongside existing fallback/skipped lists.
  - Static copy: add `booking.nextDirectNote` line ("if the lottery already ran, a free spot is assigned immediately") after next1–next3 (:490-492).
- `MyBookingsPage.tsx` (:234): cancel button also for `Confirmed` (backend already allows it, `BookingService.cs:211-213`).
- i18n `en.json` + `de.json`: `directAssignedTitle/Message`, `waitlistedTitle/Message`, `weekDayAssignedHeading`, `weekDayWaitlistedHeading`, `nextDirectNote`.
- No changes: `types/booking.ts`, `BookingStatusBadge.tsx` (statuses already modeled/styled).

## A7. Tests

New `backend/SchulerPark.Tests/Integration/DirectAssignmentTests.cs` (pattern: `CustomWebApplicationFactory`, seed via scoped `AppDbContext`, simulate "lottery ran" by inserting a `LotteryRun` row, relative dates):
1. `CreateBooking_AfterLottery_WithFreeSlot_ReturnsConfirmedWithSlot` (201, Confirmed, slot, `confirmedAt` set, `confirmationDeadline` null)
2. `CreateBooking_AfterLottery_AssignsPreferredSlot`
3. `CreateBooking_AfterLottery_AllSlotsHeld_ReturnsLost`
4. `CreateBooking_AfterLottery_WritesNoLotteryHistory`
5. `CreateBooking_BeforeLottery_StaysPending` (regression pin — nothing pins this today)
6. `CreateBooking_AfterLottery_SkipsBlockedSlot` (+ only-slot-blocked → Lost)
7. `CreateBooking_AfterLottery_SendsDirectConfirmedEmail_NotCreatedEmail` (+ Lost variant)
8. `CancelDirectlyConfirmed_PromotesLostWaitlister` (A cancels Confirmed → B's Lost promoted to Won)
9. `WeekBooking_MixedLotteryDays_PerDayOutcomes` (LotteryRun for one day only)
10. `Availability_AfterDirectAssignment_CountsBooking`

New `backend/SchulerPark.Tests/Services/DirectAssignmentServiceTests.cs` (unit, InMemory ctx + real `PreferenceAwareSlotPlacer`): NotApplicable-when-no-run, free-slot→Confirmed, Won+Confirmed both exclude, whole-location-blocked→Lost, Lost-bookings-hold-no-slots.

(23505 retry path not exercisable on InMemory — covered by review + occupied-set unit tests; index is the prod backstop.)

## A8. Edge cases (resolved)

- Week booking mixed days → per-day independent outcomes.
- Duplicate check unchanged (runs before assignment; Lost booking blocks double-entering waitlist; cancelled Confirmed can rebook).
- Cancel directly-Confirmed → existing freed-slot → waitlist promotion works as-is.
- Availability endpoint counts Confirmed and Lost as booked (unchanged) — full days show 0 available, so the Lost path is mostly reached on partially-Lost days or direct API. Accepted.
- New booking direct-assigns ahead of existing Lost waiters when a slot frees without promotion (e.g. admin unblocks a slot) — matches the rule as stated; flag in PR.

## Implementation order (A)

1. A1 refactor → run existing tests. 2. A2 + A5 interfaces (+ fake update so tests compile). 3. A3 (wiring, retry, index + migration). 4. A4. 5. A7 tests. 6. A6 frontend.

---

# Part B — Same-day booking (DEFERRED — owner decided 2026-07-14; reference design for a follow-up, do NOT implement now)

Allow booking a free slot for **today** (today's lottery always ran yesterday → always the direct-assignment path).

- **Cutoff rule:** bookable until the slot's **end** in Berlin — Morning until 12:00, Afternoon until 18:00 (matches the ranges already shown in the UI i18n). Constants + pure `IsSameDayBookingOpen(date, slot, berlinNow)` added to `DeadlineHelper.cs` (existing deadline methods untouched).
- **Validation:** `BookingService.CreateBookingAsync` `date <= today` → `date < today`; add same-day cutoff check. Week bookings stay tomorrow+ (today skipped as now).
- **Full case for today: reject** ("No free parking slots left for today") instead of creating `Lost` — waitlist promotion refuses past the confirmation deadline (Morning 05:00 / Afternoon 12:00), so a same-day Lost booking would be dead on arrival.
- **Testability:** inject .NET `TimeProvider` into `BookingService` only (register `TimeProvider.System` in Program.cs; `FakeTimeProvider` from `Microsoft.Extensions.Time.Testing` in tests). Cutoff logic itself is a pure helper unit-tested with explicit times (incl. DST dates).
- **Frontend:** new `utils/berlinTime.ts` (Intl-based Berlin now — also fixes existing UTC/local minDate quirk at `BookingPage.tsx:114-117`); minDate = today (single mode); pass `from=today` to availability fetch (backend already permits it; default is tomorrow); grey out time slots past cutoff in `TimeSlotSelector.tsx`; ~3 i18n keys en+de.
- **Tests:** unit (cutoff boundaries 11:59/12:00, 17:59/18:00, DST); integration with `FakeTimeProvider` (today-with-free-slot → Confirmed; after-cutoff → 400; today-full → 400 + no Lost row; week-containing-today skips today; availability from=today).
- **Incremental cost:** ~11 files, ~300 lines, ~1 dev-day incl. tests. No migration, no new endpoints, no job changes. Cleanly severable from Part A.

---

## Verification (end-to-end)

1. `cd backend && dotnet build && dotnet test` — all existing + new tests green.
2. `cd frontend && npm run lint && npm test && npm run build`.
3. Manual via docker compose dev (`docker compose up --build`, app on :8080, MailHog on :8025):
   - Seed/admin: run lottery manually for a future date (`POST /api/lottery/run?date=...`) with fewer bookings than slots → book that date as a normal user → response `Confirmed` with slot number; MailHog shows "directly confirmed" email, NOT the "lottery at 10 PM" email.
   - Fill all slots for a ran date → book → `Lost`; cancel one Confirmed booking → the Lost booking is promoted to `Won` (waitlist email).
   - Book a date with no lottery run → still `Pending` + existing created email.
   - Frontend: create booking for a post-lottery date → success panel shows direct assignment + slot; MyBookings shows Confirmed with cancel button.
   - (If Part B included) book today before/after cutoff; check greyed-out slot in UI.
4. EF migration: `dotnet ef database update` on dev DB; confirm index exists and inserting a duplicate Won/Confirmed (slot, date, timeSlot) row fails.
