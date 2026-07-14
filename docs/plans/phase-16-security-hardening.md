# Phase 16: Security Hardening

> **Status (2026-07-07):** Implemented on branch `letsencrypt-dns01` — all code-side items
> done and verified (65 backend tests incl. new C1/H1/H3/H5/M1/M4 regressions; frontend
> lint/tests/build green; prod image builds and runs non-root; H2 fail-fast verified live;
> `/dbbackup` locked down on the host). Still open (ops/user decisions):
> secret rotation at deploy (WP2), off-box backup copy + `BACKUP_PASSPHRASE` (H6),
> Let's Encrypt DNS-01 rollout & HSTS re-enable (M8/H4 follow-up), forced-command
> deploy key (M7 second half).

## Context

The 2026-07-07 security review (`docs/security-review.md`, branch `letsencrypt-dns01` @ `49f3246`) found 1 critical, 7 high, 8 medium, and 8 low findings. Azure AD SSO is live in production, which makes C1 (pre-account-takeover via unverified email + silent SSO auto-link) the top priority. This plan sequences remediation into work packages ordered by the review's priority list. Finding IDs (C1, H1–H7, M1–M8, L1–L8) reference the review document.

Strengths confirmed by the review (no work needed): object-level authz, role model, password hashing, refresh-token rotation/reuse detection, JWT validation, no SQLi/CORS issues, secrets hygiene in git.

## Work Packages

### WP1 — Identity & account linking (C1, H5) — CRITICAL, ship first

Backend: `AuthService.cs`, `AzureAdTokenValidator.cs`, `User` entity + migration.

- [x] Add `EmailVerified` (bool) + `EmailVerificationToken`/`ExpiresAt` to `User`; EF migration.
- [x] Local register: create account unverified, send confirmation email (MailKit, fire-and-forget pattern per convention), add `GET /api/auth/verify-email?token=` endpoint.
- [x] Local login: reject unverified accounts with a clear error (resend option).
- [x] Existing users: backfill migration sets `EmailVerified = true` for all pre-existing accounts (they predate the attack window) — decide explicitly and record in the migration comment.
- [x] `LoginWithAzureAdAsync`: match/link on `oid` (`AzureAdObjectId`) first. Email-based auto-link ONLY if the local account is verified. If an unverified local account holds that email, refuse the merge: create/claim the account for the Azure identity and invalidate the unverified local credentials (H5 + C1 together).
- [x] Stop treating `preferred_username` as verified email — keep it for display only; use `oid` as the identity key (H5).
- [x] Tests: pre-registered-unverified-email + SSO login scenario; oid-first matching; verified-account link still works.

### WP2 — Auth hardening (H1, H2, H3) — small diffs, high value

- [x] H1: reject `DeletedAt != null` in `LoginAsync`, `LoginWithAzureAdAsync`, and refresh (`TokenService.cs`). Add a per-request check (custom claims transformation or middleware querying `DeletedAt`) so already-issued 60-min JWTs die on next request. Also guard `BookingService` if the per-request check doesn't cover it.
- [x] H2: startup guard in `Program.cs` — fail fast in Production if `Jwt.Secret` is missing, equals the shipped placeholder, or < 32 bytes.
- [x] H2: change prod compose to required-var syntax: `${JWT_SECRET:?JWT_SECRET must be set}`, same for `${DB_PASSWORD:?...}` (remove the `changeme` fallback in `docker-compose.yml`).
- [x] H3: `AddRateLimiter` — strict per-IP fixed window on `/api/auth/login`, `/register`, `/refresh` (e.g. 10/min/IP); global sane default for the rest of the API.
- [x] H3: account lockout — add `AccessFailedCount` + `LockoutEnd` to `User` (same migration as WP1 if convenient); exponential backoff after ~5 failures; reset on success.
- [ ] Rotate `JWT_SECRET` and `DB_PASSWORD` on deploy of this WP (reviewer note: both were echoed into a terminal session). Note: DB password rotation requires the SCRAM reset gotcha from the 2026-05-26 rename.

### WP3 — Transport & infrastructure (H4, H6, H7) — ops-side, quick

- [x] H4: delete the `http://louise.schuler.de` block from `Caddyfile` so Caddy 308-redirects to HTTPS. (The "Cloudflare redirect loop" comment is stale.)
- [x] H4/M8: HSTS — `Strict-Transport-Security "max-age=86400"` added to the public Let's-Encrypt site block in `Caddyfile` (2026-07-08); absent from the internal-CA localhost/IP block. Raise toward 31536000 (+ includeSubDomains; preload) after burn-in with a trusted cert live.
- [~] M8: Let's Encrypt rollout — **DNS-01 abandoned** (zone is Cloudflare *secondary*, can't write challenge records; see `project-dns-topology` memory). Switched to stock-Caddy **inbound HTTP-01/TLS-ALPN-01**: `Caddyfile` + `docker-compose.prod.yml` done (2026-07-08, ACME_CA defaults to LE staging). **BLOCKED on Schuler IT opening inbound :80/:443** to `193.28.217.49`; then flip ACME_CA to production. See `docs/plans/phase-15-letsencrypt-dns01.md` (superseded banner).
- [x] H6: `chmod 700 /dbbackup` applied on the host (existing dumps 600); `umask 077` in `scripts/db-backup.sh`; optional AES-256 encryption via `BACKUP_PASSPHRASE` (openssl, present in alpine). Open: set the passphrase in `.env` and arrange an off-box copy. NB: db-backup.sh is a single-file bind mount — recreate the db-backup container to pick up the new script.
- [x] H7: non-root app container — runtime stage in `Dockerfile`: create `app` user, `chown` `/app` (read-only where possible) and the `/keys` DataProtection volume, `USER app`. Port 8080 is unprivileged, no blocker.
- [x] Deploy note: Caddyfile is a single-file bind mount — changes need `up -d --force-recreate caddy`, not `caddy reload`.

### WP4 — API input & enumeration hardening (M1, M2, M4)

- [x] M1: clamp availability range in `LocationController`/`LocationService` — reject `to - from > 62 days` and `from < today` (400 ProblemDetails).
- [x] M2: DataAnnotations on `RegisterRequest` (`[EmailAddress]`, `[MinLength(8)]` password, `[MaxLength(200)]` DisplayName) and `UpdateProfileRequest` (DisplayName, CarLicensePlate max lengths).
- [x] M4: normalize emails to lowercase on write and compare (one-off migration to lowercase existing rows; detect/resolve any resulting duplicates first). Generic register response ("if this email is new, a confirmation was sent" — dovetails with WP1's verification email). Dummy PBKDF2 verify on unknown-email login to equalize timing.

### WP5 — Remaining mediums (M3, M5, M6, M7)

- [x] M3: `BootstrapAdmin` — `admin.yml` written 0600 (file first, user only on success), password never logged, moved to writable `/bootstrap` for the non-root container. (Forced first-login password reset not implemented — no reset flow exists yet; the generated 96-bit password + 0600 file is the accepted posture.)
- [x] M5: add CSP to the Caddy header block: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'`. Verify PWA/service-worker and Tailwind still work; tighten `style-src` if possible after L3.
- [x] M6: move deploy state from `/tmp/schulerpark-deploy.last` to `$REPO/.deploy-state` with `0600`; parse strict `KEY=VALUE` instead of `source`; verify ownership (`[[ -O ]]`).
- [x] M7: pin `appleboy/ssh-action` to a full commit SHA in `.github/workflows/ci.yml`; consider a forced-command deploy key.

### WP6 — Low-severity cleanups (L1–L8) + CI

- [x] L1: add `Enum.IsDefined` after every `Enum.TryParse` (`BookingController.cs:28,48`, `UsersAdminController.cs:70`, `AdminController.cs:107`, `LotteryController.cs:33`).
- [x] L2: `sourcemap: mode !== 'production'` in `vite.config.ts` (or block `*.map` in Caddy).
- [x] L3: self-host Inter via `@fontsource/inter`; drop Google Fonts links from `index.html` (GDPR + CSP + proxy hang).
- [x] L4: move push unsubscribe endpoint from DELETE query string to request body (`pushService.ts` + `PushController`).
- [x] L5: service worker — only navigate to `data.url` if same-origin (`sw.ts`).
- [x] L6: per-user daily booking-creation cap in `BookingService`.
- [x] L7: `AzureAdTokenValidator` — catch `SecurityTokenException` specifically; log unexpected exceptions.
- [x] L8: logout cookie delete mirrors set attributes (HttpOnly/Secure/SameSite/Path).
- [x] CI: add `npm audit --omit=dev` to the frontend job (non-blocking at first, review output).

## Sequencing & deployment

1. **WP1 + WP2** ship together as one backend release (shared `User` migration; both touch `AuthService`). Rotate secrets at this deploy.
2. **WP3** is ops/config, can go immediately and independently — but land the H4 Caddyfile fix together with (or after) the DNS-01 cert so users aren't stuck on cert warnings with no HTTP fallback.
3. **WP4–WP6** in order as capacity allows; each is independently shippable.

Deployment constraints: ~1.7 GB RAM box (deploy.sh MemAvailable floor), corporate proxy poisons container egress and host curl tests — health checks must bypass proxy (already handled in deploy.sh).

## Verification

- Unit/integration tests for WP1/WP2 auth flows (xUnit, `dotnet test`).
- Manual E2E per package: attempt C1 attack scenario post-fix; login as disabled user (must fail); start app with missing `JWT_SECRET` (must not start); `curl -x '' http://louise.schuler.de/` (must 308); check `id` in app container (non-root); `ls -la /dbbackup` (700/600).
- Re-run the security review checklist against the fixed branch and update `docs/security-review.md` with a remediation-status column or follow-up section.
