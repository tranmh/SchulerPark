# Phase 21 – Wording: "lottery/won" and the licence plate field

Status: **approved and implemented** (2026-10-28) — helper text plus reporter plate in the problem mail, "Platzvergabe" / "zugeteilt"

## Trigger

User feedback (translated):

> The "raffle" is worded a bit strangely? If I have "won" it, is the electricity free of charge?
> If I enter my license plate number in the profile, will I then also be able to get to the
> parking lot by car?

Two real problems behind the joke:

1. **"Verlosung" / "gewonnen" reads like a prize draw.** The German UI, mails and push
   notifications call the assignment a "Verlosung" and tell users they "gewonnen" a slot.
   Users read that as winning a prize, not as getting a slot allocated.
2. **The licence plate field explains nothing and does nothing.** `User.CarLicensePlate` is
   only saved, shown back in the profile and included in the DSGVO export. It is not shown to
   admins, not put into problem-report mails, and no gate reads it. The field has no helper
   text, so users guess at its purpose.

## What I found (no code changed yet)

| Where | Current wording |
|---|---|
| `frontend/src/i18n/locales/de.json` | 21 strings with "Verlosung", "verlost", "gewinnen", "gewonnen" (nav, dashboard stat, booking page info box, waitlist hint, profile helpers, status badge `Won` = "Gewonnen — Bestätigen", slot-admin warnings, privacy page, two API error codes) |
| `frontend/src/i18n/locales/en.json` | same keys: "lottery", "Winners", "If you win", `Won` = "Won — Confirm" |
| `backend/.../EmailService.cs` | Booking-received mail ("Die Verlosung läuft…"), won mail (subject "Gewonnen!", heading "Sie haben einen Parkplatz gewonnen!"), lost mail ("Verlosungsergebnis", "Viel Glück beim nächsten Mal!" – and it does not say the user is now on the waitlist, which is what actually happens), kept/expired mails ("Ihr gewonnener Parkplatz"), direct-assign mail |
| `backend/.../PushNotificationService.cs` | "Parkplatz gewonnen!" / "You Won a Parking Spot!", "Verlosungsergebnis" / "Lottery Result" |
| `frontend/src/components/BookingStatusBadge.test.tsx` | asserts the label "Won — Confirm" |
| `docs/UserManual.md` (+ `UserManual.pdf`) | "Won the lottery?", status table "Won — Confirm", "fair lottery at 21:00" |
| `frontend/src/pages/Profile/ProfilePage.tsx` | plate field has a placeholder `GP-AB 1234` but no `helper` text (the `Field` component supports one) |

Not touched: the `BookingStatus.Won` enum, API values, route `/admin/lottery-history`,
`LotteryJob`, Hangfire names, E2E tests (they assert on English admin headings only, which stay).

## Proposed wording

Principle: describe an **allocation** ("Platzvergabe", "zugeteilt"), mention the lot-based
mechanism once where the process is explained ("per Losverfahren"), never "gewinnen".
English keeps "lottery" on admin pages (route and tests use it) but user-facing text says
"draw" / "assigned".

### German (de.json)

| Key | Now | Proposed |
|---|---|---|
| `nav.lotteryHistory`, `admin.lotteryHistory` | Verlosungshistorie | Vergabehistorie |
| `dashboard.statLotteryCutoff` / `statLotteryHint` | Verlosungsschluss / tägliche Verlosung | Platzvergabe / täglich, per Losverfahren |
| `booking.lotteryInfo` | Die Plätze werden am Vortag um … verlost. Gewinner bestätigen … Wer nicht gewinnt, … | Die Plätze werden am Vortag um … per Losverfahren vergeben. Wer einen Platz erhält, bestätigt ihn am Buchungstag bis … Wer keinen Platz erhält, steht auf der Warteliste und rückt automatisch nach. |
| `booking.nextLottery` | Die Verlosung läuft … | Die Platzvergabe läuft am Vorabend um … |
| `booking.nextWin` | Wenn Sie gewinnen, … | Wenn Sie einen Platz erhalten, bestätigen Sie ihn unter „Meine Buchungen“ bis … |
| `booking.nextLose` | Wenn Sie nicht gewinnen, … | Wenn Sie keinen Platz erhalten, … |
| `booking.directAssignedMessage` | Die Verlosung für diesen Tag … | Die Platzvergabe für diesen Tag … |
| `booking.waitlistPositionHint` | … bisherigen Verlosungsergebnissen … | … bisherigen Vergabeergebnissen … |
| `profile.preferredSlotHelper` | Wenn Sie die tägliche Verlosung gewinnen, … | Wird Ihnen bei der täglichen Platzvergabe ein Platz zugeteilt, erhalten Sie genau diesen, falls frei; … |
| `profile.pushSubtitle` | … sobald die Verlosungsergebnisse vorliegen. | … sobald die Platzvergabe abgeschlossen ist. |
| `landing.taglineSubtitle` | … tägliche Verlosung … | … tägliche Platzvergabe … |
| `components.status.Won` | Gewonnen — Bestätigen | Platz zugeteilt — Bestätigen |
| `components.timeSlot.lotteryPending` | Verlosung um {{time}} Uhr | Vergabe um {{time}} Uhr |
| `admin.slots.slotAffected_*` | gewonnene oder bestätigte Buchung(en) | zugeteilte oder bestätigte Buchung(en) |
| `privacy.section2_4`, `3_3`, `4Body` | Verlosungsdaten / Verlosungshistorie / Verlosungslauf | Vergabedaten / Vergabehistorie (… mittels gewichtetem Losverfahren) / Vergabelauf |
| `apiErrors.booking_not_cancellable` / `booking_not_confirmable` | gewonnene | zugeteilte |

### English (en.json)

| Key | Now | Proposed |
|---|---|---|
| `components.status.Won` | Won — Confirm | Slot assigned — Confirm |
| `booking.lotteryInfo` | Slots are drawn at … Winners confirm … If you don't win … | Slots are allocated by a fair draw at … the day before. If you get a slot, confirm it on the booking day by … If you don't, you are on the waitlist and move up automatically. |
| `booking.nextWin` / `nextLose` | If you win / If you don't win | If you get a slot / If you don't get a slot |
| `profile.preferredSlotHelper` | When you win the daily lottery … | When the daily draw assigns you a slot … |
| `dashboard.statLotteryCutoff` / hint | Lottery cutoff / daily lottery time | Slot allocation / daily draw |
| `admin.slots.slotAffected_*`, API errors | won | assigned |
| `nav.lotteryHistory`, `admin.lotteryHistory`, `timeSlot.lotteryPending` | Lottery … | unchanged ("Lottery history" is asserted by E2E tests; "Lottery at 21:00" is fine in English) |

### Mails and push (backend, DE / EN)

| Template | Proposed |
|---|---|
| Booking received | "Die Platzvergabe läuft am Vortag um … Uhr, danach …" / "Slots are allocated at … the day before …" |
| Lottery won – subject | "Parkplatz zugeteilt — {Ort} am {Datum}" / "Parking spot assigned — …" |
| Lottery won – heading | "Ihnen wurde ein Parkplatz zugeteilt" / "You have been assigned a parking spot" |
| Lottery lost – subject/heading | "Ergebnis der Platzvergabe" / "Allocation result" |
| Lottery lost – body | "Leider konnte Ihnen bei der Platzvergabe dieses Mal kein Parkplatz zugeteilt werden: … Die Nachfrage war größer als die Anzahl freier Plätze. **Sie stehen auf der Warteliste und rücken automatisch nach, sobald ein Platz frei wird.**" (replaces "Viel Glück beim nächsten Mal!", which is both prize-like and wrong: losers are Waitlisted, not out) |
| Direct assignment | "Die Platzvergabe für diesen Tag ist bereits gelaufen …" |
| Kept / expired | "Ihr zugeteilter Parkplatz …" / "Your assigned parking spot …" |
| Push won | "Parkplatz zugeteilt" / "Parking spot assigned" |
| Push lost | "Ergebnis der Platzvergabe" / "Allocation result"; body adds "Sie stehen auf der Warteliste." / "You are on the waitlist." |

### Licence plate field

Add a helper text under the field (new keys `profile.carPlateHelper` in both locales):

> DE: Optional. Hilft Admins, Ihr Fahrzeug zuzuordnen, wenn Sie ein Parkplatz-Problem melden.
> Das Kennzeichen öffnet keine Schranke und ist für die Einfahrt nicht nötig.
>
> EN: Optional. Helps admins identify your car when you report a parking spot problem.
> It does not open any barrier and is not needed to enter the car park.

To make that sentence true, include the reporter's own plate in the slot-problem mail:

- `FeedbackReport` (`backend/SchulerPark.Core/Models/Feedback.cs`): add `string? ReporterPlate`.
- `FeedbackService`: pass `user.CarLicensePlate` for slot problems (null for general feedback).
- `AdminNotifier.FeedbackReceivedAsync`: line "Kennzeichen des Melders: …" / "Reporter's plate: …" when set.

Alternative if you prefer not to touch the backend for the plate: helper text only, worded as
"Optional. Wird nur in Ihrem Profil gespeichert; öffnet keine Schranke und ist für die
Einfahrt nicht nötig." – tell me which.

### Docs and tests

- `BookingStatusBadge.test.tsx`: expected label becomes "Slot assigned — Confirm".
- `docs/UserManual.md`: section 3 and status table reworded ("Got a slot? Confirm …",
  "Slot assigned — Confirm"), section 5 Profile bullet mentions what the plate is for, "Last
  updated" line. Regenerate `UserManual.pdf` (no pandoc on this box; I would render the
  Markdown with Playwright's bundled Chromium – or you regenerate it the way you did before).
- Run `npm run lint && npm test` (frontend) and `dotnet build && dotnet test` (backend).

## Reply suggestion for the person who sent the feedback

> Danke für den Hinweis, das Wording war unglücklich. Es ist keine Verlosung mit Gewinn,
> sondern eine faire Platzvergabe per Losverfahren, wenn mehr Leute buchen als Plätze da sind.
> „Gewonnen“ heißt nur: Sie haben einen Platz bekommen und müssen ihn noch bestätigen. Strom
> ist nicht inbegriffen ;) Das Kennzeichen im Profil ist optional und öffnet keine Schranke;
> es hilft den Admins nur, Ihr Auto zuzuordnen, wenn Sie ein Parkplatz-Problem melden. Wir
> passen die Texte in der App entsprechend an.

## Out of scope

Renaming `BookingStatus.Won`, the `/admin/lottery-history` route, Hangfire job names, the
`LotteryRun` entity or any API field. Those are internal and the E2E tests depend on them.
