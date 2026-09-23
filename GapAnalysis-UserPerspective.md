# LouisE – Uncovered Cases (User-Perspective Gap Analysis)

Date: 2026-09-23
Scope reviewed: `docs/UserManual.md`, `docs/TestPlan.md`, `docs/HLD.md`, backend services/controllers/jobs (`backend/SchulerPark.*`), frontend pages/components (`frontend/src`), backend xUnit tests and Playwright E2E tests (`e2e/tests`).

Every item below was verified against the code, not only the documentation. File references point to where the gap lives.

---

## 1. Features promised in the User Manual that do not exist

| # | Gap | Evidence |
|---|-----|----------|
| 1.1 | **Change password / Forgot password.** Manual §3 says local users can change their password from Profile; the login page shows a "Forgot password?" link. No `change-password`, `forgot-password` or `reset-password` endpoint exists. The link is a dead `href="#"`. Combined with the account lockout (5 failures → up to 1 h, surfaced as generic "Invalid email or password"), a local user who forgets their password has **no self-service recovery** and no hint that they are locked out. | `frontend/src/pages/Login/LoginPage.tsx:205`, `AuthController.cs` (routes list), `ProfileController.cs`, `AuthService.cs:20-21, 326-335` |
| 1.2 | **Admin cancelling a booking.** Manual §7: "Cancelled by you (or an admin)". `AdminController` has no cancel endpoint; the admin Bookings page is read-only. Admin also cannot create a booking on behalf of a user (visitor, new joiner, executive). | `AdminController.cs`, `frontend/src/pages/Admin/BookingsPage.tsx` |
| 1.3 | **Admin → Lottery UI.** Manual §12 describes a manual-trigger page. Only the API exists (`POST /api/lottery/run`); no route in `App.tsx`. Admin must use Swagger/curl. | `frontend/src/App.tsx`, `LotteryController.cs` |
| 1.4 | **Export bookings list for reports.** No CSV/Excel export in the admin Bookings page. | `frontend/src/pages/Admin/BookingsPage.tsx` |
| 1.5 | **Blocking a time slot.** Manual §12: "block specific dates and time slots". `BlockedDay` has no `TimeSlot`; blocks are whole-day only — a half-day event cannot be modelled. | `SchulerPark.Core/Entities/BlockedDay.cs` |

---

## 2. Booking lifecycle gaps (end-user perspective)

| # | Gap | Evidence |
|---|-----|----------|
| 2.1 | **Same-day / walk-in booking is impossible.** `date <= today` is rejected. Someone who decides at 08:00 to come in cannot book even if 20 slots are free. Direct assignment (Phase 17) only helps for *future* dates whose lottery already ran. | `BookingService.CreateBookingAsync` |
| 2.2 | **Full-day parking requires two lotteries.** Only Morning/Afternoon exist. A full-day employee books both, may win Morning at P004 and Afternoon at P017 (or lose one) and must move the car at 13:00. No "Full day" option; no logic to keep the same slot across both halves. | `TimeSlot.cs`, `LotteryService.cs` |
| 2.3 | **Freed slots after the deadline go to nobody.** Waitlist promotion returns early once the deadline has passed. A Morning winner who cancels at 06:30 frees a slot that stays empty all day while Lost users remain waitlisted. | `WaitlistService.TryPromoteWaitlistAsync` (first guard) |
| 2.4 | **Waitlist promotion minutes before the deadline.** Promotion at 05:50 gives the promoted user 10 minutes to confirm — while asleep. No minimum confirmation window. Reminder is **email only**, no push. | `WaitlistService.cs`, `ConfirmationExpiryJob.cs` (`IsReminderDue`) |
| 2.5 | **06:00 deadline for a 22:00 lottery.** Winners must confirm overnight; the hourly job sends the reminder between 05:00–06:00. Consider deadline = slot start, or auto-confirm with a "Release" button. | `DeadlineHelper.cs`, `ConfirmationExpiryJob.cs` |
| 2.6 | **No "Expired" notification.** When a Won booking expires the user gets no email/push; they discover it in My Bookings or on arrival. | `ConfirmationExpiryJob.ExecuteAsync` (expired branch sends nothing) |
| 2.7 | **"Lost" is ambiguous.** Same status for "on the waitlist, still hoping" and "day is over, definitely no slot". No waitlist position, no automatic transition after the day passes. | `BookingStatus.cs`, `MyBookingsPage.tsx` |
| 2.8 | **Confirm button remains clickable after the deadline** until the hourly expiry job flips the status; clicking yields an error. | `MyBookingsPage.tsx` (`DeadlineCountdown` shows "passed", button still rendered) |
| 2.9 | **A user can hold slots at several locations for the same day/slot.** Duplicate check includes `LocationId`, so Goeppingen + Gemmingen for Monday morning are both allowed and both can be won — a hoarding path that undermines fairness. | `BookingService.CreateBookingAsync` duplicate query |
| 2.10 | **Weekends bookable in single-day mode; no public-holiday calendar.** Weekends are only disabled in week mode. Admins must block each holiday at each of 6 locations by hand. | `CalendarPicker.tsx:132-139` |
| 2.11 | **No recurring booking** (e.g. "every Tue/Thu until December"). Week booking is Mon–Fri only. | `BookingService.CreateWeekBookingAsync` |
| 2.12 | **No calendar integration** (ICS attachment in the "Won" email, add-to-calendar). | `EmailService.cs` |

---

## 3. Bugs found during the review

| # | Bug | Evidence |
|---|-----|----------|
| 3.1 | **Date-window mismatch frontend vs backend.** Frontend allows today + 31 days; backend uses `today.AddMonths(1)`. On 23 Sep the calendar offers 24 Oct, which the API rejects ("at most one month in advance"). Around February the gap grows to 3 days. Frontend also computes "tomorrow" in browser-local time, backend in Europe/Berlin. | `frontend/src/utils/bookingWindow.ts`, `BookingService.cs` |
| 3.2 | **Availability counts Pending and Lost bookings as "booked".** Before the lottery a popular day shows "0 available" although nothing is assigned and the user can still enter the lottery. Lost rows keep inflating "booked" afterwards. | `LocationService.GetAvailabilityAsync` |
| 3.3 | **Deleted / disabled users keep their slots.** Soft-delete (`DELETE /api/profile/data`) and admin disable never cancel the user's Pending/Won/Confirmed bookings. A Confirmed slot stays blocked; Pending ones can still *win* the lottery for a ghost account; nothing goes to the waitlist. | `ProfileController.RequestDeletion`, `UsersAdminController.Disable` |
| 3.4 | **Blocking a day / deactivating a slot or location after bookings exist does nothing to them.** Block P001 for tomorrow after the 22:00 lottery → the Confirmed user arrives at a blocked bay; no reassignment, no notification. Deactivating a location strands its Pending bookings forever (lottery iterates active locations only) and users are never told. | `AdminController.CreateBlockedDay / DeactivateSlot / DeactivateLocation`, `LotteryService.RunAllLotteriesAsync` |
| 3.5 | **Lottery failure is silent.** Per-slot exceptions are only logged; bookings stay Pending; no admin alert, no user notification. Same if the app is down at 22:00 (Hangfire default does not fire missed schedules) — nothing verifies next morning that today's lottery ran. | `LotteryService.RunAllLotteriesAsync`, `Program.cs` Hangfire registration |

---

## 4. Admin / operations gaps

- No occupancy or no-show reporting (there is no check-in, so "Confirmed but never showed up" is invisible and fairness cannot account for it).
- No per-user fairness view (win rate, consecutive losses) although `LotteryHistory` holds the data.
- No bulk slot creation and no bulk blocked-day creation (e.g. block all 6 locations for 3 Oct in one step).
- No way to override a lottery result or move a user to a specific slot.
- No lightweight announcement/broadcast to users of one location ("lot closed Friday for resurfacing").
- Only de/en locales.

---

## 5. Test-coverage holes (features that exist but have no automated test)

- `POST /api/bookings/{id}/confirm` happy path and confirm-after-deadline rejection.
- Cancel of Won/Confirmed → waitlist promotion **ordering** (preferred-slot boost, WeightedHistory weight, `CreatedAt` tiebreak).
- `ConfirmationExpiryJob` actually promoting a waitlister after expiry (current tests only cover reminder behaviour).
- `SaveWithSlotConflictRetryAsync` slot-conflict retry path.
- Lockout reset after a correct login.
- Disabled/deleted user with active bookings (behaviour is currently undefined — see 3.3).
- PWA offline shell (manual test plan only).
- Any E2E for the Approvals page or the mobile grid view.

---

## Suggested priority

1. **P0 (correctness / trust):** 3.3, 3.4, 3.5, 1.1
2. **P1 (daily user pain):** 3.1, 3.2, 2.1, 2.3, 2.6, 2.9
3. **P2 (experience):** 2.2, 2.4, 2.5, 2.7, 2.8, 1.2, 1.3, 1.4
4. **P3 (nice to have):** 1.5, 2.10–2.12, section 4, section 5
