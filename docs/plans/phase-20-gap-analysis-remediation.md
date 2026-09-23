# Phase 20 — Gap Analysis Remediation

Date: 2026-09-23
Status: WP1–WP4 implemented (2026-09-23); WP5–WP8 planned. Deviations from the text below:
whole-location block/deactivation cancels Won/Confirmed bookings too (nobody can park there, so a
"withdrawn, you are waitlisted" mail would mislead); the
`TightenBookingUserDateSlotIndex` migration is not yet written (prod duplicate check pending).
WP4 as built: the lottery moved to **21:00** and the Morning default deadline to **07:00** (Afternoon
stays 13:00) — both configurable (`Booking:LotteryTime`, `Booking:ConfirmationDeadline:*`) and
exposed via `GET /api/bookings/window` so the UI texts, mails and the Hangfire cron share one source;
D1 resolved as auto-confirm; the Waitlisted badge is sky-blue (Pending already uses amber);
`GET /api/bookings/my` returns `waitlistPosition`; a `Waitlisted` booking can be cancelled by its
owner; `IX_Bookings_Status_ConfirmationDeadline` added for the 15-minute poll; the migration test
executes the migrations' SQL constants against the Postgres fixture rather than replaying history.
Source: `GapAnalysis-UserPerspective.md` (repo root, 2026-09-23). Item numbers below (1.1 … 3.5, §4, §5)
refer to that document. Every item was re-verified against the working tree while writing this plan;
file references are current.

## Context

The gap analysis lists 12 missing features, 5 bugs, 6 admin/ops gaps and 8 test holes. They cluster
around four themes, which is how this plan is cut:

1. **Bookings outlive the things they depend on.** Nothing that removes a user (disable, self-delete),
   a slot (deactivate, block) or a location touches existing bookings (3.3, 3.4). The lottery can fail or
   be skipped without anyone noticing (3.5).
2. **The confirmation model is rigid.** One computed deadline (06:00 / 13:00 Berlin) drives expiry,
   reminders and waitlist promotion. That produces the after-deadline dead zone (2.3), 10-minute
   confirmation windows (2.4), the overnight deadline (2.5), the silent expiry (2.6), the ambiguous
   `Lost` (2.7) and the stale Confirm button (2.8).
3. **Client and server disagree on rules.** Date window (3.1), what "booked" means (3.2), weekends
   (2.10), multi-location duplicates (2.9).
4. **Admins have read-only tooling.** No cancel/create/move (1.2, §4), no lottery page (1.3), no export
   (1.4), no half-day blocks (1.5), no bulk actions, no reporting, no announcements (§4).

Facts established during verification that shape the design:

- `User` has no `IsDisabled`/`IsDeleted`; both admin disable and self-deletion set `DeletedAt`
  (`UsersAdminController.cs:104`, `ProfileController.cs:153`). Admin hard delete cascades bookings.
- `ConfirmationExpiryJob` loads every `Won` booking, has no `ReminderSentAt` flag, and sends the
  reminder by email only. The expired branch sends nothing.
- `WaitlistService.TryPromoteWaitlistAsync` returns as its first guard when the deadline has passed,
  orders candidates by preferred-slot match, then `WeightedHistoryStrategy.CalculateWeight`, then
  `CreatedAt`, and only considers `Status == Lost`.
- `AuthService.RegisterFailedAttemptAsync` never resets `AccessFailedCount` when a lockout expires, so
  the first wrong password after a lockout re-locks immediately with a doubled duration. While locked,
  the password is not checked at all (`LoginAsync`, L169).
- The email-verification token already has the pattern a reset token needs: 32 random bytes,
  base64url, SHA-256 hash stored on `User`, 24 h expiry, link `{BaseUrl}/verify-email?token=`.
- `LocationService.GetAvailabilityAsync` counts every status except Cancelled/Expired as booked.
- Booking window: backend `today+1 … today.AddMonths(1)` in Berlin; frontend `d+1 … d+31` in browser
  local time (`utils/bookingWindow.ts:23-27`).
- Hangfire recurring jobs are registered without `MisfireHandlingMode` (`Program.cs:327-352`).
- Filtered unique index `IX_Bookings_ParkingSlotId_Date_TimeSlot` (Won/Confirmed) is the DB backstop
  for slot races; EF InMemory tests do not enforce it, the Testcontainers Postgres fixture does.
- Frontend: `User.hasAzureAd` exists but there is no `hasPassword`; admin pages have hardcoded English
  strings; `apiErrors.*` has 18 codes in both locales; `auth.forgot` link at `LoginPage.tsx:205` is
  `href="#"`.
- `docs/UserManual.md` and `docs/HLD.md` are not in the tree. The manual references in the gap analysis
  (§3, §7, §12) are therefore not actionable and this plan does not create a manual.

Constraints (unchanged from Phases 16–19):

- This checkout is the prod deploy checkout. Verify with the throwaway Docker recipes in §Verification,
  never with a local compose stack.
- All user-visible text goes through `de.json` and `en.json`. New `ValidationException` codes are added
  to `apiErrors` in both.
- Emails and push templates come in German and English (`User.PreferredLanguage`).
- Core stays dependency-free; time-dependent logic goes through `TimeProvider` so it can be tested with
  `FakeTimeProvider` (`Microsoft.Extensions.Time.Testing`).

---

## Work packages

| WP | Priority | Gap items | Migration | Deployable alone |
|----|----------|-----------|-----------|------------------|
| WP1 Booking integrity | P0 | 3.3, 3.4, 3.5 | `AddBookingCancellationAudit` | yes |
| WP2 Password self-service and lockout | P0 | 1.1 | `AddPasswordResetToken` | yes |
| WP3 Client/server rule alignment | P1 | 3.1, 3.2, 2.9, 2.1 | none | yes |
| WP4 Confirmation model | P1/P2 | 2.3, 2.4, 2.5, 2.6, 2.7, 2.8 | `AddBookingConfirmationDeadline`, `AddWaitlistedStatus` | yes, after WP1 |
| WP5 Admin booking control | P2 | 1.2, 1.3, 1.4, §4 override/move | none (uses WP1 audit columns) | yes |
| WP6 Calendar rules | P3 | 1.5, 2.10, 2.11, 2.12 | `AddBlockedDayTimeSlot`, `AddLocationCalendarRules`, `AddBookingSeries` | yes |
| WP7 Admin operations | P3 | §4 remaining | `AddCheckIn`, `AddAnnouncements` | yes |
| WP8 Test coverage | P3 (start in WP1) | §5 | none | n/a |

Order: WP1 → WP2 → WP3 → WP4 → WP5 → WP6 → WP7. WP8 tests are written inside the WP that touches the
code; the pure backfill tests (§5 items with no feature change) ship with WP1.

---

## WP1 — Booking integrity (3.3, 3.4, 3.5)

### 1a. Shared release/reassign service

New `SchulerPark.Core/Interfaces/IBookingLifecycleService.cs` and
`SchulerPark.Infrastructure/Services/BookingLifecycleService.cs`. Both `BookingService` and the admin
controllers call it; it is the single place that turns "capacity or user went away" into booking state.

```csharp
public enum BookingReleaseReason { UserDisabled, UserDeleted, AdminCancelled, SlotBlocked,
                                   SlotDeactivated, LocationBlocked, LocationDeactivated }

public record CapacityChangeResult(int Affected, int Reassigned, int Waitlisted, int Cancelled);

public interface IBookingLifecycleService
{
    // 3.3 — cancels every Pending/Won/Confirmed/Lost booking of the user dated today or later,
    // frees slots, promotes waitlisters. No notification to the user (account is gone).
    Task<CapacityChangeResult> ReleaseUserBookingsAsync(Guid userId, BookingReleaseReason reason);

    // 3.4 — re-evaluates bookings on (location, date, optional slot) after capacity was removed.
    Task<CapacityChangeResult> HandleCapacityRemovedAsync(
        Guid locationId, DateOnly from, DateOnly? to, Guid? parkingSlotId,
        BookingReleaseReason reason, Guid actedByUserId, string? adminReason);
}
```

`HandleCapacityRemovedAsync` algorithm, per (date, timeSlot) in range, inside one transaction per date:

1. Load affected bookings: Won/Confirmed whose `ParkingSlotId` is the blocked/deactivated slot, or all
   Won/Confirmed at the location for a whole-location block/deactivation. Order Confirmed before Won,
   then `CreatedAt`.
2. Free slots = `SlotAvailabilityHelper.GetUnblockedActiveSlotsAsync` minus slots held by Won/Confirmed
   (same set-minus as `DirectAssignmentService.ApplyAsync`). For each affected booking try
   `ISlotPlacer.Place` (preferred → nearest → random). Success: keep status, set new `ParkingSlotId`,
   notify **slot reassigned**. Failure: `Status = Lost` (becomes `Waitlisted` after WP4),
   `ParkingSlotId = null`, notify **slot withdrawn, you are on the waitlist**.
3. Whole-location removal: Pending and Lost bookings for the range become `Cancelled` with
   `CancelledByUserId`/`CancelReason`, notify **cancelled by admin** with the reason.
4. Save through the same 23505 retry pattern as `SaveWithSlotConflictRetryAsync` (extract that helper
   into `BookingPersistence.cs` so both services share it).

`Booking` gains `CancelledByUserId (Guid?)`, `CancelReason (string?)`, `CancelledAt (DateTime?)`.
Migration `AddBookingCancellationAudit`. These columns are also what WP5 admin cancel writes.

New templates in `IEmailService` and `IPushNotificationService` (de first, en second, following
`SendWaitlistWonAsync`): `SendSlotReassignedAsync(Booking, oldSlotNumber)`,
`SendSlotWithdrawnAsync(Booking)`, `SendBookingCancelledByAdminAsync(Booking, reason)`.
`Fakes.cs` `CapturingEmailService` gets the new methods and records them by type.

### 1b. Wire the callers

- `UsersAdminController.Disable` (L104) and `ProfileController.RequestDeletion` (L153): after setting
  `DeletedAt`, call `ReleaseUserBookingsAsync`. `UsersAdminController.Delete` (L163): call it before
  removing the user so waitlisters are promoted before the cascade.
- `LotteryService.RunLotteryForSlotOnceAsync` Pending query and `WaitlistService` candidate query:
  add `b.User.DeletedAt == null` as belt and braces.
- `AdminController.CreateBlockedDay` (L211): validate `ParkingSlotId` belongs to `LocationId` (currently
  unchecked), then call `HandleCapacityRemovedAsync(locationId, date, date, slotId, SlotBlocked|LocationBlocked …)`.
  Return `CapacityChangeResult` alongside the created DTO.
- `AdminController.DeactivateSlot` (L178), `UpdateSlot` when `IsActive` flips to false (L164),
  `DeactivateLocation` (L94): call with `from = Berlin today`, `to = null` (all future dates). Return the
  result.
- Frontend (`Admin/SlotsPage.tsx`, `LocationsPage.tsx`, `BlockedDaysPage.tsx`): before the destructive
  call, fetch `GET /api/admin/bookings?locationId&from&to&status=Won,Confirmed` and show the count in the
  existing `ConfirmDialog` ("3 confirmed bookings will be moved or waitlisted"). After the call, toast the
  `CapacityChangeResult` counts. i18n keys `admin.capacityImpact.*`.

### 1c. Lottery observability (3.5)

- `LotteryService.RunAllLotteriesAsync` returns `LotteryRunSummary { Date, Succeeded, Failures:
  List<(LocationName, TimeSlot, string Error)> }`. It still continues past a failing slot.
- `LotteryJob`: after the run, if `Failures.Count > 0`, call the new `IAdminNotifier.LotteryFailedAsync`
  (Infrastructure; generalises `AuthService.NotifyAdminsOfPendingUserAsync` L129–143 into
  `AdminNotifier` that resolves active Admin/SuperAdmin users), then throw so Hangfire records the job as
  failed. Decorate `LotteryJob` with `[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[]{60,300,900})]`.
  The idempotency guard (`LotteryRun` exists) means retries only touch the slots that failed. The admin
  email goes out once per attempt; acceptable at 3 attempts.
- Missed schedule: set `MisfireHandling = MisfireHandlingMode.Relaxed` in the `RecurringJobOptions` for
  `daily-lottery` (`Program.cs:333`). Hangfire 1.8 then fires the job once when the server comes back.
- New `LotteryWatchdogJob` (recurring `30 23 * * *` and `0 5 * * *` Berlin, `[DisableConcurrentExecution]`
  like the others, add to `RecurringJobConcurrencyTests`): for the target date (tomorrow at 23:30, today
  at 05:00) and every active location × time slot, if Pending bookings exist and no `LotteryRun` row
  exists, run `RunLotteryForSlotAsync` for that slot (self-heal), then notify admins that the watchdog had
  to intervene. Also sweep Pending bookings with `Date < Berlin today` to `Lost` and notify admins with
  the count (these are the bookings a permanently failed lottery leaves behind).
- `GET /api/admin/lottery/status?date=` (new action on `LotteryController`, policy `AdminOnly`): per
  location × slot: `pendingCount`, `ranAt`, `totalBookings`, `availableSlots`, `lastError` (from a new
  `LotteryRun.Error` nullable column? No: keep failures in logs plus the admin email; the status endpoint
  reports `ranAt == null && pendingCount > 0` as "not run"). Consumed by the WP5 lottery page.
- Health: extend `GET /api/health` with `lottery: { lastRunAt, missingForTomorrow: bool }` computed after
  22:30 Berlin only, so the existing external health check can alert.

### 1d. Tests (WP1 + backfill of §5 items that need no feature)

Integration (InMemory unless noted):

- Disable user with Won booking → booking Cancelled, slot freed, single Lost waitlister promoted.
- Self-delete with Pending booking → Cancelled; lottery run for that date does not assign to the deleted
  user even if the release is skipped (query guard).
- Block slot after lottery, free slot exists → booking moved, `SendSlotReassignedAsync` recorded.
- Block slot after lottery, no free slot → Lost, `SendSlotWithdrawnAsync` recorded.
- Whole-location block → Pending cancelled with reason, `SendBookingCancelledByAdminAsync` recorded.
- Deactivate location with future Confirmed booking → waitlisted/cancelled per rules.
- Blocked day with slot from another location → 400.
- Lottery: strategy throws for one location → summary has one failure, other locations complete,
  admin email recorded, job throws.
- Watchdog: Pending bookings for tomorrow, no `LotteryRun` → run created; Pending with past date → Lost.
- §5 backfill: `POST /api/bookings/{id}/confirm` happy path and after-deadline 400
  `confirmation_deadline_passed` (use `FakeTimeProvider`; requires threading `TimeProvider` into
  `DeadlineHelper` callers, see WP4 which finishes that work); waitlist ordering with three candidates
  (preferred-slot boost beats weight beats `CreatedAt`); `ConfirmationExpiryJob` with the real
  `WaitlistService` promoting a waitlister after expiry (Postgres fixture, replace the no-op fake in
  `ConfirmationExpiryJobTests.cs`); `SaveWithSlotConflictRetryAsync` (Postgres fixture: pre-insert a
  Confirmed booking on the slot the placer will pick, assert the retry lands on another slot or Lost);
  `WaitlistService` 23505 path (Postgres fixture).

---

## WP2 — Password self-service and lockout (1.1)

### Data and backend

- `User`: `PasswordResetTokenHash (string?)`, `PasswordResetTokenExpiresAt (DateTime?)`. Migration
  `AddPasswordResetToken`. Reset tokens are valid 1 h (`PasswordResetTokenValidMinutes = 60`).
- `AuthService`:
  - `RequestPasswordResetAsync(email)`: always completes silently. If a non-deleted local user with a
    `PasswordHash` exists: issue token with the same generate/hash/expire code as
    `IssueVerificationTokenAsync` (refactor that into `IssueTokenAsync(user, purpose)`), send
    `SendPasswordResetAsync(user, link)` with link `{BaseUrl}/reset-password?token=`. If the address
    belongs to an SSO-only account (no `PasswordHash`), send `SendPasswordResetNotApplicableAsync`
    ("this account signs in with Microsoft"). Unknown address: nothing.
  - `ResetPasswordAsync(token, newPassword)`: look up by hash, check expiry, enforce the existing
    `PasswordComplexity`, set hash, clear token fields, set `EmailVerified = true` (the user proved
    mailbox control), reset `AccessFailedCount`/`LockoutEnd`, `ITokenService.RevokeAllUserTokensAsync`.
    Codes: `reset_token_invalid`, `password_too_weak`.
  - `ChangePasswordAsync(userId, current, new)`: verify current with `PasswordHasher`, set new, revoke all
    refresh tokens, return a fresh token pair (reuse the issuance at the end of `LoginAsync`) so the
    current session continues. Codes: `password_incorrect`, `password_not_set` (SSO-only), `password_too_weak`.
  - Lockout fixes: in `RegisterFailedAttemptAsync`, when `LockoutEnd` is in the past reset
    `AccessFailedCount` to 0 before incrementing. In `LoginAsync`, when locked, still verify the password;
    correct password → throw new `AccountLockedException(retryAfter)`; wrong password → generic 401 and no
    counter increment. This reveals the lockout only to someone holding the correct password, the same
    trust rule Phase 18 used for `pending_approval`. Optional: `SendAccountLockedAsync` email on the 5th
    failure with a reset link hint (decision below).
- `AuthController`: `POST forgot-password` and `POST reset-password` (rate-limit policy `auth`, both
  return 202/204 without body). Map `AccountLockedException` to 423 `{ code: "account_locked",
  retryAfterSeconds }`. `ProfileController`: `POST change-password` `[Authorize]`.
- `UserDto` gains `hasPassword` (`PasswordHash != null`). Data export includes nothing new (token hashes
  are excluded, as `EmailVerificationTokenHash` already is).

### Frontend

- `authService`: `forgotPassword(email)`, `resetPassword(token, newPassword)`;
  `profileService.changePassword(current, next)` returning `AuthResponse` and calling `setAccessToken`.
- Extract the password rules from `RegisterPage.tsx:42-60` into `utils/passwordRules.ts` and reuse them.
- `pages/Login/ForgotPasswordPage.tsx` (`/forgot-password`): email field, generic success card modelled
  on the register "verification sent" card. Wire `auth.forgot` link (`LoginPage.tsx:205`) to it.
- `pages/Login/ResetPasswordPage.tsx` (`/reset-password?token=`): mirrors `VerifyEmailPage` (token from
  `useSearchParams`, `startedRef` guard not needed since it is form-driven), two `PasswordInput`s,
  success → link to login.
- `ProfilePage.tsx`: new `<Section>` "Change password" between Personal Information and Push, rendered
  only when `user.hasPassword`. Three fields, save button, inline error via `describeApiError`.
- `LoginPage.tsx`: handle 423 `account_locked` like `pending_approval` (L70-73): show
  `auth.accountLocked` with minutes and a link to forgot-password.
- i18n: `auth.forgotTitle/Body/Sent`, `auth.resetTitle/Success/Invalid`, `profile.changePassword.*`,
  `auth.accountLocked`, `apiErrors.reset_token_invalid|password_too_weak|password_incorrect|password_not_set|account_locked`.
- `User` type: `hasPassword: boolean`.

### Tests

- `AuthTests`: forgot for unknown email → 202 and no mail; for local user → reset mail recorded; reset
  with valid token → login with new password works, old refresh token rejected, token single-use;
  expired token → 400; reset marks unverified user verified; change-password wrong current → 400;
  SSO-only → `password_not_set`; lockout: 5 failures → locked, correct password → 423 `account_locked`,
  wrong password → 401; lockout expiry then one wrong attempt does not re-lock (the survey's double-lock
  finding); successful login resets the counter (§5).
- Vitest: `ForgotPasswordPage.test.tsx`, `ResetPasswordPage.test.tsx`, `passwordRules.test.ts`.
- E2E `auth.spec.ts`: forgot → MailHog link → reset → login (MailHog API is already used by the
  register test).

Decisions to confirm: (a) send an email when an account is locked (recommended: yes, it is the only
in-band hint and costs one template); (b) reset-token validity 1 h (recommended).

---

## WP3 — Client/server rule alignment (3.1, 3.2, 2.9, 2.1)

### 3.1 One booking window

- Backend: `Booking:MaxDaysAhead` (default 31) in `appsettings.json`, env `Booking__MaxDaysAhead`.
  `BookingService` uses `today.AddDays(MaxDaysAhead)` instead of `AddMonths(1)`. New
  `GET /api/bookings/window` → `{ today, minDate, maxDate }` in Berlin, computed with `TimeProvider`.
- Frontend: `utils/berlinTime.ts` (Intl-based `todayInBerlin()`; also the helper Phase 17 Part B calls
  for). `bookingWindow.ts` keeps its DST-safe arithmetic but starts from `todayInBerlin()` and takes
  `maxDaysAhead` from the window endpoint (fetched once in `BookingPage`, cached; fall back to 31).
  `CalendarPicker` `minDate`/`maxDate` come from that.
- Tests: backend boundary (today+31 accepted, +32 `booking_too_far_ahead`, with `FakeTimeProvider` on
  2026-01-31 to cover the month-length case); `bookingWindow.test.ts` updated; `berlinTime.test.ts`
  (UTC evening vs Berlin next day).

### 3.2 Availability semantics

- `LocationService.GetAvailabilityAsync`: `booked` = Won + Confirmed only. DTO gains `pendingCount`
  (Pending), `waitlistCount` (Lost/Waitlisted) and `lotteryRan: bool` (a `LotteryRun` row exists for
  date × slot). `AvailabilityDto` and `frontend/src/types/booking.ts Availability` updated.
- `CalendarPicker` dot logic and `TimeSlotSelector`: before the lottery show demand
  ("12 requests for 10 slots" → amber, "4 for 10" → emerald), after the lottery show free slots as today.
  Legend keys `components.calendar.demandLow/demandHigh` added; the existing three keep working for
  post-lottery days.
- Test: update the availability assertion in `DirectAssignmentTests` and add a pre-lottery case
  (10 slots, 3 Pending → `availableSlots = 10`, `pendingCount = 3`).

### 2.9 One booking per user per date and time slot

- `BookingService.CreateBookingAsync` duplicate query and the week loop: drop `LocationId` from the
  duplicate check. New code `booking_duplicate_other_location` whose ProblemDetails includes the other
  location's name; frontend `apiErrors` entry uses `{{location}}`.
- DB index: tighten `IX_Bookings_UserId_Date_TimeSlot_LocationId` to `(UserId, Date, TimeSlot)` filtered
  `"Status" NOT IN ('Cancelled','Expired')` **in a later migration**, after checking prod for existing
  future cross-location duplicates with
  `SELECT "UserId","Date","TimeSlot",count(*) FROM "Bookings" WHERE "Status" NOT IN ('Cancelled','Expired') AND "Date" >= current_date GROUP BY 1,2,3 HAVING count(*) > 1`.
  Never cancel user bookings inside a migration; if rows exist, the admin cancels them via WP5.
- Tests: second booking at another location → 400 with the code; cancelled first booking → allowed.

### 2.1 Same-day booking

Implement Phase 17 Part B exactly as written in `phase-17-direct-slot-assignment.md` (cutoff = slot end,
`IsSameDayBookingOpen` in `DeadlineHelper`, `date < today`, full → 400 `no_slots_today`, `TimeProvider`
into `BookingService`, `from=today` for availability, greyed-out past time slots). WP4 removes the
"promotion refuses past the confirmation deadline" caveat mentioned there, so after WP4 a full same-day
request may create a `Waitlisted` booking instead of a 400. Ship Part B first with the 400, flip to
waitlisting in WP4.

---

## WP4 — Confirmation model (2.3 – 2.8)

### Design

Store the deadline instead of computing it, and give the waitlist an explicit status.

- `Booking.ConfirmationDeadline (DateTime? UTC)` — set when a booking becomes Won. Migration
  `AddBookingConfirmationDeadline` backfills existing Won rows with the current formula.
- `Booking.ReminderSentAt (DateTime?)` — replaces the "job runs exactly hourly" assumption.
- `BookingStatus.Waitlisted = 6` (stored as string like the others). `Lost` becomes terminal:
  "the day is over, no slot". Migration `AddWaitlistedStatus` updates `Lost` rows with `Date >= today`
  to `Waitlisted`. `LotteryHistory.Won = false` is unchanged (fairness weights unaffected).
- `DeadlineHelper` gains configurable defaults `Booking:ConfirmationDeadline:Morning = "06:00"`,
  `Afternoon = "13:00"` (2.5; defaults unchanged, changing them becomes an ops decision), slot start/end
  constants shared with Part B (`SlotStart`, `SlotEnd` per `TimeSlot`), and
  `ComputeDeadline(date, slot, nowUtc)`:
  `max(defaultDeadline, nowUtc + MinConfirmationWindow) capped at SlotEnd`, with
  `Booking:MinConfirmationWindowMinutes = 120` (2.4).
- `DeadlineHelper.IsDeadlinePassed(Booking, TimeProvider)` reads the stored value. Every caller
  (`BookingService.ConfirmBookingAsync`, `ConfirmationExpiryJob`, `WaitlistService`) switches.

### Flows

- **Lottery win**: `ConfirmationDeadline = ComputeDeadline(...)` at run time (22:00 → default 06:00).
- **Waitlist promotion** (`WaitlistService.TryPromoteWaitlistAsync`):
  - Replace guard 1 with "slot end has passed" (2.3). Candidates: `Status == Waitlisted`.
  - If `now < defaultDeadline - MinConfirmationWindow`: promote to Won with `ComputeDeadline`.
  - Otherwise promote **directly to Confirmed** (`ConfirmedAt = now`) and send
    `SendWaitlistAutoConfirmedAsync` ("a slot freed up at short notice, P004 is yours, no confirmation
    needed"). Rationale: a 06:30 cancellation should fill the slot; asking someone asleep to confirm
    within 10 minutes cannot. (Decision to confirm; alternative is Won with a 2 h deadline capped at slot
    end, which still leaves the 06:30 slot empty if they do not react.)
- **Expiry job** (`ConfirmationExpiryJob`): schedule `*/15 * * * *` (deadlines are now arbitrary
  timestamps). Query Won with `ConfirmationDeadline <= now` for expiry; Won with
  `ReminderSentAt == null && ConfirmationDeadline - now <= 1h` for reminders. Reminder goes out by email
  **and push** (`IPushNotificationService.SendConfirmationReminderAsync`, 2.4). Expired branch sends
  `SendBookingExpiredAsync` email + push (2.6). Also transition `Waitlisted` with `SlotEnd <= now` →
  `Lost` (2.7).
- **Same-day full** (WP3/Part B): create `Waitlisted` instead of 400 once this WP is in.
- **Waitlist position** (2.7): `GET /api/bookings/my` adds `waitlistPosition` for Waitlisted rows,
  computed with the same ordering as the promotion minus the preferred-slot term (weight, then
  `CreatedAt`); documented as "approximate" in the tooltip.

### Frontend

- `BookingStatus` type and `BookingStatusBadge`: add `Waitlisted` (de "Warteliste", en "Waitlisted",
  amber); `Lost` label becomes "Kein Platz" / "No slot". `STATUS_OPTIONS` in `MyBookingsPage` and admin
  `BookingsPage` gain Waitlisted. Row shows "Position {{n}}" when present.
- Confirm button (`MyBookingsPage.tsx:225-236`): render only while `confirmationDeadline > now`
  (reuse `DeadlineCountdown`'s tick); after that show the `deadlinePassed` pill alone (2.8).
- Dashboard deadline pill uses the stored deadline unchanged (DTO field already exists).
- sw.ts payload gains optional `tag` so a reminder replaces an earlier reminder notification.

### Tests

- `DeadlineHelperTests` (new, unit): `ComputeDeadline` at 22:00 → 06:00; at 05:30 → 07:30 (window);
  at 11:30 Morning → 12:00 (cap); DST dates 2026-03-29 and 2026-10-25.
- `WaitlistServiceTests` (InMemory + `FakeTimeProvider`): before window → Won with deadline; inside
  window → Confirmed + auto-confirm mail; after slot end → no promotion; ordering (moved from WP1 if not
  yet written).
- `ConfirmationExpiryJobTests`: reminder sent once (second run does not resend), reminder push recorded,
  expiry sends expired mail + push, Waitlisted past slot end → Lost.
- Migration test on the Postgres fixture: `Lost` future rows → `Waitlisted`, past rows unchanged.
- Vitest: `BookingStatusBadge.test.tsx` new status; `MyBookingsPage` confirm button hidden after deadline.
- E2E `user/booking-full.spec.ts`: Won → Confirm flow via the API trigger helper (also closes the §5 hole
  "no E2E for confirm").

---

## WP5 — Admin booking control (1.2, 1.3, 1.4, §4 override/move)

### Backend (`AdminController`, policy `AdminOnly`)

- `DELETE /api/admin/bookings/{id}` body `{ reason }`: Pending/Won/Confirmed/Waitlisted → Cancelled,
  writes `CancelledByUserId/CancelReason/CancelledAt` (WP1 columns), frees slot →
  `TryPromoteWaitlistAsync`, sends `SendBookingCancelledByAdminAsync`.
- `POST /api/admin/bookings` body `{ userId, locationId, date, timeSlot, parkingSlotId?, reason? }`:
  on behalf of a user. Without `parkingSlotId`: same path as `CreateBookingAsync` (Pending or direct
  assignment). With `parkingSlotId`: slot must be active, unblocked and free → `Confirmed` immediately,
  no `LotteryHistory`, sends `SendBookingDirectlyConfirmedAsync`. Same-day allowed for admins regardless
  of cutoff. Skips the 50/day limit. Bypasses the one-per-day rule only with `force: true`.
- `PUT /api/admin/bookings/{id}/slot` body `{ parkingSlotId }`: move a Won/Confirmed booking to a free
  slot at the same location (§4 "override a lottery result"); notify with `SendSlotReassignedAsync`.
  Swapping two users is two moves via a temporary free slot; no swap endpoint.
- `GET /api/admin/bookings/export` with the same filters as `GET /api/admin/bookings`, returns
  `text/csv; charset=utf-8` with BOM and `;` separator (German Excel), columns Date, TimeSlot, Location,
  Slot, User email, User name, Licence plate, Status, Created, Confirmed, Cancelled by, Reason. Capped at
  10 000 rows; 400 `export_too_large` above.
- `GET /api/admin/lottery/status?date=` from WP1c; `LotteryController` actions stay at `/api/lottery/run`
  for the E2E helper, and are additionally routed under `/api/admin/lottery/run` (two `[Route]`
  attributes).

Visitors and new joiners without an account: admins book on a shared local account per location
("Besucher Goeppingen"), created once by a SuperAdmin. No visitor entity in this phase.

### Frontend

- `Admin/BookingsPage.tsx`: row actions **Cancel** (dialog with reason, required), **Move** (slot
  dropdown filtered to free slots via `GET /locations/{id}/grid-availability?date&timeSlot`), header
  buttons **New booking** (modal: user search over `GET /admin/users?search=`, location, date, slot,
  optional fixed slot) and **Export CSV** (downloads with current filters). Add a `userId` filter (the
  service already supports it). Replace the hardcoded English strings with `admin.bookings.*` keys while
  touching the file.
- `Admin/LotteryPage.tsx` (`/admin/lottery`, nav `nav.lottery`, `LotteryIcon`): date picker default
  tomorrow, table from the status endpoint (location, slot, pending, ran at, result), buttons **Run all**
  and per-row **Run**, confirmation dialog, result toast, link to the history page. Also surfaces "not
  run" rows in red.
- `adminService`: `cancelBooking`, `createBooking`, `moveBooking`, `exportBookings`, `getLotteryStatus`,
  `runLottery`, `runLotteryForLocation`.

### Tests

- Integration: admin cancel → Cancelled with audit fields, waitlister promoted, mail recorded; admin
  create with slot → Confirmed; with occupied slot → 400; move to occupied → 400; export returns CSV with
  BOM and filtered rows; non-admin → 403 (`admin/permissions` pattern).
- E2E: `admin/bookings-actions.spec.ts` (cancel via UI, export download header), `admin/lottery.spec.ts`
  (page loads, run all, row appears in history; replaces the API-only `lottery-trigger.spec.ts` checks
  where they overlap).

---

## WP6 — Calendar rules (1.5, 2.10, 2.11, 2.12)

### 1.5 Half-day blocks

- `BlockedDay.TimeSlot (TimeSlot?)`, null = whole day. Migration `AddBlockedDayTimeSlot`; both filtered
  unique indexes in `BlockedDayConfiguration.cs:37-41` gain `TimeSlot`.
- `SlotAvailabilityHelper.GetUnblockedActiveSlotsAsync(db, locationId, date, timeSlot)` filters
  `TimeSlot == null || TimeSlot == slot`; `BookingService.ValidateLocationForDateAsync`,
  `LocationService.GetAvailabilityAsync`, grid availability and WP1 `HandleCapacityRemovedAsync` take
  the time slot. `LocationController` blocked-days DTO and `frontend types BlockedDay/AdminBlockedDay`
  gain `timeSlot`.
- `Admin/BlockedDaysPage.tsx` modal: radio Whole day / Morning / Afternoon and an optional slot picker
  (the `parkingSlotId` field the type already has). `CalendarPicker`: half-blocked days render a diagonal
  half fill and stay selectable; `TimeSlotSelector` disables the blocked half with reason tooltip.
- Tests: Morning block → Afternoon booking allowed, Morning `location_blocked`; lottery for the blocked
  half gets zero slots; availability per half.

### 2.10 Weekends and public holidays

- `Location.AllowWeekendBooking (bool, default false)` and `Location.State (string, ISO 3166-2, e.g.
  "DE-BW")`. Migration `AddLocationCalendarRules` seeds states: Goeppingen, Gemmingen, Weingarten → DE-BW;
  Erfurt → DE-TH; Hessdorf → DE-BY; Netphen → DE-NW. Admin `LocationsPage` form gets both fields.
- `SchulerPark.Core/Helpers/GermanHolidays.cs`: pure function `GetHolidays(year, state)` returning
  `(DateOnly, Name)` for nationwide holidays (Gauss Easter algorithm for the movable ones) plus per-state
  additions (BW: Heilige Drei Könige, Fronleichnam, Allerheiligen; BY: same plus Mariä Himmelfahrt as an
  opt-in via `Location.ExtraHolidays` config since it is municipality-dependent; TH: Weltkindertag,
  Reformationstag; NW: Fronleichnam, Allerheiligen). Unit tests against 2026 and 2027 dates.
- Enforcement in one place: `ICalendarRuleService.GetClosure(location, date)` → `null | Weekend | Holiday(name)`.
  `ValidateLocationForDateAsync` throws `booking_weekend_not_allowed` / `booking_public_holiday`;
  `GetAvailabilityAsync` returns zero and a `closureReason`; `GET /locations/{id}/blocked-days` returns
  virtual read-only entries (`isVirtual: true`) so the calendar greys them out with the holiday name; the
  lottery skips closed dates. Admins can still add manual blocks on top.
- `CalendarPicker`: weekends disabled in single-day mode unless the location allows them; closure reason
  in the tooltip. Week booking skips holidays with a `SkippedDay` reason (already supported).

### 2.11 Recurring bookings

Two steps; step 1 is small, step 2 is the real feature.

1. Generalise the week endpoint: `POST /api/bookings/series` body
   `{ locationId?, timeSlot, weekdays: ["Tuesday","Thursday"], from, until }` limited to the booking
   window. `CreateWeekBookingAsync` becomes a thin call to it (`weekdays = Mon–Fri`, `until = from+4`).
   Response reuses `WeekBookingResponse` (created + skipped). `BookingPage` week toggle becomes a
   "Repeat" panel with weekday chips and an until-date.
2. `BookingSeries` entity `{ Id, UserId, LocationId?, TimeSlot, Weekdays (flags), From, Until,
   IsActive, CreatedAt }`, migration `AddBookingSeries`. `BookingSeriesJob` at `0 21 * * *` Berlin
   (before the lottery) materialises the next day inside the window for every active series, skipping
   closures and duplicates, sending one summary mail per series per week rather than per booking.
   `GET/DELETE /api/bookings/series[/{id}]`; My Bookings gets a "Series" tab; cancelling a series does not
   cancel already-created bookings (explicit checkbox to also cancel future ones). Data export and
   `DataRetentionJob` include the entity. Deleting a user cascades.

### 2.12 Calendar integration

- `SchulerPark.Core/Helpers/IcsBuilder.cs`: `Build(booking, location, method)` → VCALENDAR with one
  VEVENT: `UID = {bookingId}@louise.schuler.de`, `DTSTART/DTEND` from `SlotStart/SlotEnd` in
  `TZID=Europe/Berlin` (embed a static VTIMEZONE block), `SUMMARY "Parkplatz P004 – Goeppingen"`,
  `LOCATION` = address, `DESCRIPTION` with the My Bookings link, `SEQUENCE` bumped on reassignment,
  `METHOD:PUBLISH` (won/confirmed/reassigned) or `METHOD:CANCEL` with `STATUS:CANCELLED` (cancelled,
  expired, withdrawn). Unit-tested for folding at 75 octets and escaping.
- `EmailService`: attach `parkplatz.ics` (`text/calendar; method=PUBLISH`) via `BodyBuilder.Attachments`
  to `SendLotteryWonAsync`, `SendWaitlistWonAsync`, `SendBookingDirectlyConfirmedAsync`,
  `SendSlotReassignedAsync`, and the CANCEL variant to cancel/expired/withdrawn mails.
- `GET /api/bookings/{id}/ics` `[Authorize]` (own bookings only) for an **Add to calendar** button on
  My Bookings rows with a slot.

---

## WP7 — Admin operations (§4 remaining)

- **Check-in and no-show reporting.** `Booking.CheckedInAt (DateTime?)`, migration `AddCheckIn`.
  `POST /api/bookings/{id}/check-in` allowed for Confirmed bookings between `SlotStart - 30 min` and
  `SlotEnd`; My Bookings and Dashboard show a **I'm here** button in that window; push at `SlotStart`
  ("Checked in? Tap to confirm you parked") via a `CheckInReminderJob` (`*/15` Berlin, only in slot
  windows). `GET /api/admin/reports/occupancy?from&to&locationId` → per date × slot: slots, confirmed,
  checked-in, no-shows, utilisation %. `Admin/ReportsPage.tsx` (`/admin/reports`) with a table and CSV
  export. Fairness coupling is a **decision**: optional `Lottery:NoShowPenalty` (adds N virtual
  consecutive losses to `WeightedHistory` for a no-show), default off, because check-in adoption must be
  measured before it can penalise anyone.
- **Per-user fairness view.** `GET /api/admin/reports/fairness?locationId&from&to` → per user:
  participations, wins, win rate, current consecutive losses (from `WeightedHistoryStrategy`), last win,
  no-shows. Shown as a second tab on the Reports page and as an expandable row on `Admin/UsersPage`.
- **Bulk creation.** `POST /api/admin/slots/bulk { locationId, prefix, from, to, padWidth }` (P001…P040,
  skips existing numbers, returns created/skipped). `POST /api/admin/blocked-days/bulk
  { locationIds | all, dates[] | range, timeSlot?, reason }` runs `HandleCapacityRemovedAsync` per
  location. UI: **Add range** on SlotsPage; **Block for several locations** on BlockedDaysPage with a
  location multi-select and "all".
- **Override / move**: delivered by WP5.
- **Announcements.** `Announcement { Id, LocationId?, TitleDe, TitleEn, BodyDe, BodyEn, ValidFrom,
  ValidUntil, CreatedByUserId, CreatedAt, NotifyByEmail }`, migration `AddAnnouncements`.
  `GET /api/announcements` (active, filtered to the user's preferred location plus global) shown as a
  dismissible banner on Dashboard and Booking step 1 (dismissal per announcement id in `localStorage`).
  Admin CRUD at `/api/admin/announcements` and `Admin/AnnouncementsPage.tsx`. With `NotifyByEmail`, one
  email + push to every user holding a Pending/Won/Confirmed/Waitlisted booking at that location in the
  validity window (or all non-deleted users for a global one).
- **Locales.** Do not add a third language now. Prerequisite work: move the hardcoded admin strings
  (`BookingsPage`, `BlockedDaysPage`, `LotteryHistoryPage`, `LotteryRuns` `de-DE` date formatting) into
  the locale files as those pages are touched in WP5/WP6, and add `frontend/src/i18n/README.md`
  describing how a locale is added (copy `en.json`, register in `i18n/index.ts`, add to
  `Localization.cs` normalisation, add email/push template variants). Adding a language is then a content
  task, not a code change.

---

## WP8 — Test coverage (§5), summary of where each hole closes

| §5 hole | Closed in | Test |
|---------|-----------|------|
| Confirm happy path / after deadline | WP1d (backfill), WP4 | `BookingConfirmTests.cs` |
| Waitlist promotion ordering | WP1d, WP4 | `WaitlistServiceTests.cs` |
| `ConfirmationExpiryJob` actually promoting | WP1d | `ConfirmationExpiryJobTests.cs` with real `WaitlistService` (Postgres) |
| `SaveWithSlotConflictRetryAsync` | WP1d | `SlotConflictRetryTests.cs` (Postgres) |
| Lockout reset after correct login | WP2 | `AuthTests.cs` |
| Disabled/deleted user with active bookings | WP1d | `BookingLifecycleTests.cs` |
| PWA offline shell | this WP | Playwright project `pwa-chrome` with `serviceWorkers: 'allow'`: load `/`, go `context.setOffline(true)`, navigate to `/my-bookings`, assert the shell renders and the offline banner shows; API request returns the NetworkOnly failure |
| Approvals page E2E | this WP | `admin/approvals.spec.ts`: register external domain via API, verify, admin approves in UI, user can log in |
| Mobile grid view E2E | this WP | `mobile/booking-grid.spec.ts` on the Pixel 7 project: step 3 grid scrolls horizontally, page does not, a slot is selectable |

E2E runs only in CI (Playwright image cannot be pulled on the prod box, see memory `prod-box-limits`).

---

## Migrations in order

1. `AddBookingCancellationAudit` (WP1)
2. `AddPasswordResetToken` (WP2)
3. `AddBookingConfirmationDeadline` (WP4, with Won backfill)
4. `AddWaitlistedStatus` (WP4, data update Lost → Waitlisted for future rows)
5. `TightenBookingUserDateSlotIndex` (WP3 2.9, only after the prod duplicate check is empty)
6. `AddBlockedDayTimeSlot`, `AddLocationCalendarRules`, `AddBookingSeries` (WP6)
7. `AddCheckIn`, `AddAnnouncements` (WP7)

All apply via `MigrateAsync` at startup (`Program.cs:255`). Each WP is one deploy. Take a backup with
`db-backup.sh` before 3, 4 and 5 (they update data, not just schema).

---

## Decisions to confirm before implementation

| # | Question | Recommendation |
|---|----------|----------------|
| D1 | Promotion inside the confirmation window: auto-confirm (plan) or Won with a short deadline? | Auto-confirm. A freed 06:30 slot is otherwise empty all day. |
| D2 | Default deadlines stay 06:00/13:00 (now configurable) or move to slot start? | Keep, make configurable, revisit after the expired-notification (2.6) shows how many people miss it. |
| D3 | Email on account lockout? | Yes. |
| D4 | Visitor bookings via shared "Besucher" accounts? | Yes for this phase; a visitor entity is a separate plan. |
| D5 | No-show penalty in the lottery weight? | Off by default; enable only after a month of check-in data. |
| D6 | Weekend booking per location flag default | Off for all six locations. |
| D7 | Third locale | Not now; do the string extraction so it is a content task later. |

---

## Verification

Backend (from the repo root; Postgres fixture tests need the docker socket flags):

```bash
docker run --rm -v "$PWD/backend:/src" -w /src -v "$HOME/.nuget-docker:/root/.nuget/packages" \
  -v /var/run/docker.sock:/var/run/docker.sock --network host -e TESTCONTAINERS_RYUK_DISABLED=true \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test SchulerPark.Tests
```

Frontend:

```bash
docker run --rm -v "$PWD/frontend:/app" -w /app node:22-alpine \
  sh -c "npm run lint && npx vitest run && npm run build"
```

Per WP, manual checks on the deployed prod stack after rollout (MailHog is not available in prod; use a
real mailbox and the Hangfire dashboard is dev-only, so check job outcomes in the app logs):

- WP1: disable a test user holding a Won booking → booking Cancelled, waitlister promoted, mail
  received. Block a slot for tomorrow after 22:00 → user gets reassigned/withdrawn mail. Stop the app
  over 22:00 on a test date, restart → lottery fires (Relaxed misfire), watchdog quiet.
- WP2: forgot → reset → login; wrong password ×5 → 423 shown on login page; change password from Profile.
- WP3: calendar last selectable day equals the API `maxDate`; a day with 12 Pending on 10 slots shows
  demand, not "0 free".
- WP4: cancel a Confirmed booking at 06:30 → waitlister gets "auto-confirmed"; leave a Won unconfirmed →
  expired mail + push at the deadline within 15 minutes.
- WP5: cancel/move/create from the admin table; run tomorrow's lottery from the new page; CSV opens in
  Excel with umlauts intact.
- WP6: block Morning only → afternoon bookable; 3 Oct greyed out with "Tag der Deutschen Einheit" at every
  location; Won mail has a working `.ics`.
- WP7: check-in button appears at slot start; occupancy report shows the no-show.

CI (`ci.yml`) runs backend tests, frontend lint/test/build and all Playwright projects on push to master;
the `deploy` job then rolls the prod stack. Do not push a WP whose migration is in the "backup first" list
without taking the backup on the box.

## Suggested commit split

1. WP1a+1b lifecycle service, audit columns, migration, tests.
2. WP1c lottery summary, admin notifier, misfire mode, watchdog, health, tests.
3. WP1d §5 backfill tests.
4. WP2 backend (token, endpoints, lockout fixes, tests).
5. WP2 frontend (pages, profile section, i18n, vitest, E2E).
6. WP3 window + availability + duplicate rule; 7. WP3 same-day (Part B).
8. WP4 migration + deadline storage; 9. WP4 waitlist/expiry flows; 10. WP4 frontend + tests.
11. WP5 backend; 12. WP5 frontend + E2E.
13–16. WP6 one commit per item (1.5, 2.10, 2.11 step 1/2, 2.12).
17–20. WP7 one commit per feature.
21. WP8 remaining E2E (PWA, approvals, mobile grid).
