# LouisE - Parking Slot Booking System

## Project Overview
Multi-location parking slot booking system with fair lottery assignment for Schuler office locations (Goeppingen, Erfurt, Hessdorf, Gemmingen, Weingarten, Netphen).

## Tech Stack
- **Backend:** .NET 10, ASP.NET Core Web API, Entity Framework Core, PostgreSQL
- **Frontend:** React 19 + Vite + TypeScript + Tailwind CSS v4
- **Auth:** Azure AD SSO (Microsoft.Identity.Web) + local auth fallback (JWT)
- **Scheduling:** Hangfire (lottery daily at `Booking:LotteryTime`, default 21:00; confirmation expiry every 15 min; retention weekly — Europe/Berlin)
- **Email:** MailKit via SMTP (MailHog for dev)
- **Deploy:** Docker Compose (dev: app + PostgreSQL + MailHog; prod: Caddy + app + PostgreSQL + db-backup)
- **Reverse Proxy:** Caddy 2 (stock `caddy:2-alpine`), currently `tls internal` (self-signed CA) on `louise.schuler.de` (canonical; `park.schuler.de` aliased) — Let's Encrypt DNS-01 migration is planned, see `docs/plans/phase-15-letsencrypt-dns01.md`. HTTP→HTTPS 308 redirect enforced (no plaintext site block). Security headers incl. CSP; HSTS deliberately off until a trusted cert is live.
- **PWA:** vite-plugin-pwa, injectManifest (`frontend/src/sw.ts`): precached app shell with offline navigation fallback (API routes never cached), Web Push handlers, installable. Icons are generated — run `python3 scripts/generate-icons.py`, don't hand-edit the PNGs.

## Repo Structure
```
/backend          .NET solution (Api, Core, Infrastructure, Tests)
/frontend         React + Vite + TypeScript
/docs/plans       Implementation plans (Phase1-Phase12)
/scripts          DB backup script, PWA icon generator
```

## Build & Run

### Local Development
```bash
# Backend (from /backend)
dotnet restore
dotnet build
dotnet run --project SchulerPark.Api

# Frontend (from /frontend)
npm install
npm run dev          # Dev server on http://localhost:5173
npm run build        # Production build to dist/

# Tests (from /backend)
dotnet test          # Runs xUnit strategy tests
```

### Docker (development)
```bash
cp .env.example .env   # Edit .env with real values
# Dev overrides are NOT auto-loaded (renamed from docker-compose.override.yml so a
# bare `docker compose up` on the prod box can't start a Development stack):
export COMPOSE_FILE=docker-compose.yml:docker-compose.dev.yml
docker compose up --build
# App: http://localhost:8080 (dev ports bind to 127.0.0.1 only)
# PostgreSQL: localhost:5432
# MailHog UI: http://localhost:8026
# Swagger: http://localhost:8080/swagger (dev only)
# Hangfire: http://localhost:8080/hangfire (dev only)
```

### Docker (production)
```bash
cp .env.production.example .env   # Edit with real secrets
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
# Caddy: https://<SITE_DOMAIN> (auto-TLS via Let's Encrypt)
# Scale: docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --scale app=3
# Backup: docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db-backup /usr/local/bin/db-backup.sh
```

### CI/CD
Push to master triggers: backend build+test, frontend lint+test+build, E2E Playwright tests.
If all pass, the `deploy` job runs on a self-hosted runner installed on the prod box
itself (`runs-on: [self-hosted, linux, prod]`, user-level systemd unit `github-runner`)
and does `git pull --ff-only` + `docker compose up -d --build` there. No deploy secrets
needed; inbound SSH to the box is firewalled. See `docs/deploy-this-server.md`.

### Default Credentials
- SuperAdmin (dev seed): `superadmin@schulerpark.local` / `Admin123!`
- Admin (dev seed): `admin@schulerpark.local` / `Admin123!`
- Production SuperAdmin is generated on first startup if no users exist; credentials are written to `admin.yml` (0600) in `$BOOTSTRAP_ADMIN_DIR` (`/bootstrap` in the container; next to the binary otherwise) — see `BootstrapAdmin`. Read it with `docker compose exec app cat /bootstrap/admin.yml`, then delete it.

## Code Conventions
- **C#:** PascalCase for public members, nullable reference types enabled, implicit usings
- **TypeScript:** camelCase for variables/functions, PascalCase for components/types, strict mode
- **Database:** UTC storage, Europe/Berlin display. All dates use DateOnly or DateTime UTC
- **API routes:** `/api/{resource}` (lowercase, plural nouns)
- **Project layers:** Core has zero dependencies. Infrastructure references Core. Api references both.
- **Frontend styling:** Tailwind CSS utility classes. No inline styles.

## Key Endpoints
- `GET /api/health` — Health check
- `GET /swagger` — API documentation (development only)
- `POST /api/auth/login` — Local login (returns JWT; requires verified email)
- `POST /api/auth/register` — Local registration (sends verification email; no auto-login)
- `POST /api/auth/verify-email` — Confirm email via token from the verification mail
- `POST /api/auth/resend-verification` — Resend verification email (generic response)
- `POST /api/auth/forgot-password` — Request a password-reset mail (always 202, no enumeration)
- `POST /api/auth/reset-password` — Set a new password with the single-use token (1 h validity)
- `POST /api/profile/change-password` — Change password; revokes other sessions, returns a fresh token pair
- `GET /api/bookings/window` — Server-side bookable window in Berlin (`today`, `minDate`, `maxDate`, same-day slot flags) plus the schedule the UI shows (`lotteryTime`, `morningDeadline`, `afternoonDeadline`)
- `GET /api/locations` — List active locations
- `POST /api/bookings` — Create booking
- `GET /api/bookings/my` — User's bookings
- `GET /api/profile` — User profile
- `GET /api/profile/data-export` — DSGVO data export
- `DELETE /api/profile/data` — DSGVO account deletion
- `POST /api/lottery/run?date=` — Manual lottery trigger (admin)
- `GET /api/admin/lottery/status?date=` — Per location × slot: pending count, run time, `ran|not_run|no_demand` (admin)
- `POST /api/bookings/week` — Create week booking (Mon-Fri)
- `GET /api/push/vapid-public-key` — VAPID public key for push subscriptions
- `POST /api/push/subscribe` — Subscribe to push notifications
- `DELETE /api/push/subscribe` — Unsubscribe from push notifications
- `POST /api/push/test` — Push a test notification to the caller's devices (manual end-to-end check; 404 no subscription, 502 nothing delivered)
- `GET /api/admin/*` — Admin CRUD endpoints (admin)

## Error Handling
- Backend throws `ValidationException`, `NotFoundException`, `ForbiddenException` from Core/Exceptions
- `ExceptionHandlingMiddleware` maps these to RFC 9457 ProblemDetails with TraceId. A `ValidationException` a user can trigger through normal use carries a snake_case `code` (second ctor arg), emitted as the ProblemDetails `code` extension; the auth controller's `{ error, code }` responses use the same codes. The frontend maps them via `apiErrors.<code>` in both locale files (`utils/apiError.ts`) — never show backend `detail` text to users directly
- Frontend pages use try/catch with inline error display (red alert boxes)
- Email sending is fire-and-forget with internal error logging (never blocks the request)
- Emails and push notifications are German or English per `User.PreferredLanguage` (`de` default). It follows the UI language: the frontend calls `PUT /api/profile/language` whenever the signed-in user's UI language differs from the stored one, and registration passes the UI language along. Templates live side by side in `EmailService`/`PushNotificationService` (German first); `Core/Helpers/Localization.cs` normalizes codes and translates `TimeSlot`

## Hangfire Jobs
| Job | Schedule | Purpose |
|-----|----------|---------|
| `LotteryJob` | Daily at `Booking:LotteryTime` (default 21:00; Relaxed misfire, 3 retries) | Assign parking slots for next day (winners Won with a stored deadline, losers Waitlisted); a failing slot mails admins and fails the job |
| `LotteryWatchdogJob` | 23:30 (tomorrow) and 05:00 (today) | Runs any lottery that never ran, sweeps stale Pending to Lost, mails admins |
| `ConfirmationExpiryJob` | Every 15 min | Reminds once (mail + push) an hour before the stored deadline; at the deadline an unconfirmed Won expires (mail + push, slot to waitlist) only if somebody is Waitlisted for that slot and the slot has not ended, otherwise it is kept and becomes Confirmed (mail + push); closes Waitlisted past slot end as Lost |
| `DataRetentionJob` | Weekly Sunday 2 AM | Delete data older than 1 year, hard-delete soft-deleted users |

## Booking Rules (Phase 20)
- Window: Berlin today … today + `Booking:MaxDaysAhead` (default 31, env `Booking__MaxDaysAhead`); the frontend reads it from `GET /api/bookings/window`. Same-day bookings are allowed until the slot ends (Morning 12:00, Afternoon 18:00 Berlin) and are always assigned directly; a full day yields a `Waitlisted` booking.
- One live booking per user per date and time slot across all locations (`booking_duplicate_other_location` carries the other location in `params.location`).
- Availability: `bookingCount` = Won + Confirmed; `pendingCount`/`waitlistCount`/`lotteryRan` let the UI show demand before the lottery and free slots after it.
- Statuses (WP4): `Waitlisted` = no slot yet, day still ahead, promoted automatically; `Lost` is terminal (day over). Lottery losers, full same-day bookings and withdrawn slots become Waitlisted; the expiry job turns Waitlisted into Lost at slot end. Users can cancel a Waitlisted booking.
- Confirmation (WP4): `Booking.ConfirmationDeadline` is stored when a booking becomes Won — `DeadlineHelper.ComputeDeadline` = max(default deadline, now + `Booking:MinConfirmationWindowMinutes` (120)) capped at slot end. Defaults `Booking:ConfirmationDeadline:Morning` = 07:00, `Afternoon` = 13:00 Berlin (env `Booking__ConfirmationDeadline__Morning` etc.). `Booking:LotteryTime` (default 21:00, env `Booking__LotteryTime`) drives the Hangfire cron, the health check and every time shown to users. `WaitlistService` promotes to Won while the default deadline is more than the minimum window away, otherwise directly to Confirmed (auto-confirm mail/push); promotion works until slot end, not just until the deadline. `Booking.ReminderSentAt` makes the reminder single-shot. Push payloads may carry a `tag` so a newer notification replaces an older one.
- `IBookingLifecycleService` is the single place that releases/reassigns bookings when a user is disabled/deleted or a slot/location is blocked/deactivated; `Booking.CancelledByUserId/CancelReason/CancelledAt` hold the audit trail.
- Time-dependent code takes `TimeProvider` (tests pin it via `CustomWebApplicationFactory.Clock`). Migrations are hand-written (no `dotnet-ef` on the box); update `AppDbContextModelSnapshot.cs` by hand to match.

## Environment Variables
See `.env.example` for all required/optional configuration.
