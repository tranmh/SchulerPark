# Feedback: "Parkplatz Problem" + general feedback, mailed to all admins

## Context
Users have no way to report a problem with their assigned spot, such as someone else parked there, the spot is unusable, the spot can't be found, or the EV charger is broken. They also can't send general feedback. The reference screenshots show a "Parkplatz Problem" dialog with four options. "Spot occupied" asks for the blocking car's licence plate. Every option has an optional comment and a "Problem melden" button. The resulting mail tells the admins which spot, what's wrong and who reported it.

Decisions taken:
- Both a slot-problem dialog and a small general-feedback form.
- **Email only:** no table, no migration, nothing stored. Every Admin and SuperAdmin gets the mail.
- **Plate entry:** the full widget from the screenshot. I assumed this because the question wasn't answered.

## Backend

**Core**
- `Core/Enums/FeedbackCategory.cs` has the values `General`, `SlotOccupied`, `SlotUnusable`, `SlotNotFound` and `ChargerFault`.
- `Core/Interfaces/IFeedbackService.cs` declares `Task SubmitAsync(Guid userId, FeedbackSubmission s)`.
- `FeedbackSubmission` is a record in Core with these fields: `Category`, `BookingId?`, `PlateCountry?` (DE/AT/LU/OTHER), `PlateType?` (Standard/Historic/Electric), `PlateText?` and `Comment?`.
- `IAdminNotifier` (`Core/Interfaces/IAdminNotifier.cs`) gets one new method, `FeedbackReceivedAsync(FeedbackReport report)`. The report carries the reporter's name and email, the category, and the location name, slot number, date and time slot when they apply, plus the plate and the comment.

**Infrastructure**
- New `Services/FeedbackService.cs` handles validation. Each failure throws a `ValidationException` with a snake_case code:
  - `General` needs a comment that isn't blank → `feedback_comment_required`.
  - The four slot categories need a `BookingId`. The booking is loaded with `.Include(Location).Include(ParkingSlot)`, using the same pattern as `BookingService.ConfirmBookingAsync` (`BookingService.cs:326`):
    - A booking that doesn't exist → `NotFoundException`.
    - Someone else's booking → `ForbiddenException`.
    - Status not Won/Confirmed, or no slot → `feedback_booking_not_assigned`.
    - `Date` not today in Berlin (uses the injected `TimeProvider`) → `feedback_booking_not_today`.
  - Comment is capped at 1000 characters and the plate at 20 → `feedback_too_long`. Country and type must be on the allowed lists. Plate fields are ignored unless the category is `SlotOccupied`.
- When validation passes, the service builds a `FeedbackReport` and calls `IAdminNotifier.FeedbackReceivedAsync`.
- `AdminNotifier.FeedbackReceivedAsync` copies the existing `LotteryWatchdogInterventionAsync` pattern (`AdminNotifier.cs:79`):
  - It loops `ActiveAdminsAsync()` (Admin or SuperAdmin, `DeletedAt == null`).
  - It writes the subject and paragraphs in each admin's own language, then calls the existing `SendAdminAlertAsync`. That method already HTML-encodes every paragraph through `Enc`, so user text is safe.
  - The German mail follows the screenshot. Subject: "Parkplatz-Problem: P097 (Goeppingen) — LouisE". Paragraphs:
    - "Achtung: Parkplatz P097 in Goeppingen wurde als fehlerhaft gemeldet."
    - "Problem: Störung E-Ladesäule"
    - "Buchung: 21.10.2026, Vormittag"
    - "Blockierendes Fahrzeug: D · GP-AB 1234 E" (only for occupied)
    - "Kommentar: '…'"
    - "Melder: Name (email)"
  - General feedback gets the subject "Feedback von <Name> — LouisE" and carries the comment and the reporter.
- Register `IFeedbackService` as scoped in `Program.cs` next to the other services (around line 116).

**Api**
- `Controllers/FeedbackController.cs`: `[ApiController][Route("api/feedback")][Authorize]` with `POST /api/feedback`. It returns 202. Like the other controllers, it has a private `GetUserId()`.
- `DTOs/Feedback/SubmitFeedbackRequest.cs`: `record SubmitFeedbackRequest(string Category, Guid? BookingId, string? PlateCountry, string? PlateType, string? PlateText, string? Comment)`. The category is parsed case-insensitively; an unknown value → `feedback_invalid_category`.
- Rate limit: **as built**, the cap of 5 per user per 10 minutes lives in `FeedbackService` as an `IMemoryCache` window and returns `feedback_rate_limited`. A `"feedback"` rate-limiter policy wouldn't work here: `UseRateLimiter` runs before `UseAuthentication`, so it never sees the user and would put everyone in one bucket.
- Add the endpoint to the CLAUDE.md "Key Endpoints" list.

## Frontend
- `services/feedbackService.ts`: `submit(req) => api.post('/feedback', req)`.
- `components/feedback/LicensePlateInput.tsx` is the widget from the screenshot:
  - Country chips: DE, AT, LU and "?". Small flags are drawn with Tailwind blocks; no images.
  - Type segmented control: Standard / Historic H / Electric E.
  - For DE: a blue EU strip plus three fields (district 1–3 letters, letters 1–2, digits 1–4) and the H/E suffix.
  - For any other country: one free-text field.
  - It emits `{ country, type, text }`, with the text composed client-side (e.g. `GP-AB 1234 E`).
- `components/feedback/SlotProblemModal.tsx` uses the existing `components/Modal.tsx`:
  - Step 1 shows four full-width outline buttons, as in Bild3.
  - Step 2 shows the chosen option highlighted, the plate widget (occupied only), a comment `<textarea>` with the input styling and the hint text, and a primary "Problem melden" button.
  - On success the modal shows a thank-you state in place of the form (as built; no page banner). Errors go through `describeApiError(err, 'feedback.failed')` and appear inline in the modal's red box.
- `components/feedback/FeedbackModal.tsx`: a comment textarea plus a submit button.
- Entry points:
  - `pages/MyBookings/MyBookingsPage.tsx`: in the action row (lines 251-274), add a secondary "Problem melden" button. It shows when the status is Won or Confirmed, there is a `parkingSlotNumber`, and `b.date === today`. Today comes from the existing window or today logic on that page.
  - `components/AppLayout.tsx` (around 255-275): add a "Feedback" button in the sidebar user block next to Profile and Sign out, making the grid three columns.
- i18n, in both `i18n/locales/de.json` and `en.json`:
  - A new `feedback.*` section. The German strings match the screenshots: "Parkplatz Problem", "Parkplatz ist belegt", "Parkplatz ist unbenutzbar", "Parkplatz ist nicht auffindbar", "Störung E-Ladesäulen", "Land des Kennzeichens auswählen", "Kennzeichentyp auswählen", "Kommentar", "Problem melden", and so on.
  - `nav.feedback`.
  - `apiErrors.feedback_*` for the new codes.

## Tests
- Backend `Tests/Integration/FeedbackTests.cs` uses `CustomWebApplicationFactory` and `AdminTestHelper`, and asserts on `factory.Emails.AdminAlerts` (no change to the fake needed). It covers:
  - A slot problem on today's Won booking mails every admin and superadmin but not plain users. The subject contains the slot number, and the plate and comment appear in the paragraphs.
  - Someone else's booking → 403. A booking for tomorrow → `feedback_booking_not_today`. A Pending booking → `feedback_booking_not_assigned`.
  - General feedback without a comment → `feedback_comment_required`. General feedback with a comment → mailed.
  - A German-speaking admin gets the German subject and an English-speaking admin the English one.
- Frontend:
  - `SlotProblemModal.test.tsx`: picking "occupied" shows the plate widget, and submitting calls the service with the composed plate.
  - `MyBookingsPage.test.tsx`: add `feedbackService` to the mocks, and check the button appears only for today's assigned booking.

## Verification
1. Run `dotnet build` and `dotnet test` from `/backend`. The Postgres integration tests run when Docker is available.
2. Run `npm run lint`, `npm test` and `npm run build` from `/frontend`.
3. Manual check against the dev stack (`COMPOSE_FILE=docker-compose.yml:docker-compose.dev.yml docker compose up --build`):
   - Log in as a user with a slot for today, open My Bookings, click "Problem melden", then pick "Parkplatz ist belegt", enter a plate and submit.
   - MailHog (http://localhost:8026) should hold one mail per admin and superadmin, in each one's language.
   - Repeat for general feedback from the sidebar.
