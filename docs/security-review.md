# Security Review — LouisE (SchulerPark Parking Booking)

**Date:** 2026-07-07
**Branch reviewed:** `letsencrypt-dns01` (HEAD `49f3246`)
**Scope:** Backend auth & tokens, API authorization/access-control, infrastructure & deployment, React frontend.
**Method:** Source-level audit; every finding verified against actual code with `file:line` evidence. Live container/host state inspected read-only.

> **Context note:** Azure AD SSO was enabled in production on the review date. Finding **C1** (pre-account-takeover) directly affects that flow and should be treated as the top priority.

---

## Summary

| Severity | Count | Findings |
|----------|-------|----------|
| Critical | 1 | C1 |
| High     | 7 | H1–H7 |
| Medium   | 8 | M1–M8 |
| Low      | 8 | L1–L8 |

No secrets are leaked in git history, object-level authorization (IDOR) is correctly enforced everywhere, role escalation is well-guarded, and there is no SQL injection or CORS misconfiguration. The weaknesses cluster in three areas: **identity/account-linking**, **auth hardening (rate-limit, lockout, secret-guard, deleted-user)**, and **transport/deployment posture (plaintext HTTP, root container, backup perms)**.

---

## CRITICAL

### C1 — Pre-account-takeover: no email verification on local register + SSO auto-links by email
**Files:** `backend/SchulerPark.Infrastructure/Services/AuthService.cs:29-50` (register), `:82-94` (Azure link)

Local registration has **no email verification** — no `EmailVerified` flag on the `User` entity, no confirmation email, immediate login. Separately, `LoginWithAzureAdAsync` links an incoming Azure identity to any pre-existing local account that shares the same email:

```csharp
user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userInfo.Email);
if (user != null) { user.AzureAdObjectId = userInfo.ObjectId; ... }  // silent bind
```

**Attack:** An attacker registers a local account with a victim's corporate email (`victim@schuler.de`) and an attacker-chosen password — nothing blocks this. When the victim later signs in via Azure SSO, their Azure OID is bound to the attacker's row. The account is now shared; the attacker keeps password access to the victim's bookings, profile, and DSGVO data-export. Worse if the pre-registered email is later promoted to Admin.

**Fix:** Add `EmailVerified` to `User`; require confirmation before a local account is usable. In `LoginWithAzureAdAsync`, only auto-link when the local account is verified (or passwordless); otherwise treat the Azure identity as authoritative and refuse the merge. See also H5 (the email claim itself is unverified).

---

## HIGH

### H1 — Disabled / DSGVO-deleted accounts can log in again (soft-delete not enforced in auth)
**Files:** `AuthService.cs:55-63` (`LoginAsync`), `:82-113` (Azure), `TokenService.cs:72-94` (refresh)

Admin disable (`UsersAdminController.cs:99-133`) and DSGVO self-deletion (`ProfileController.cs:115-134`) set `DeletedAt` and revoke refresh tokens — but no auth path checks `DeletedAt`. The user simply logs in again (password or Azure) and gets fresh tokens, with role claim preserved. A disabled Admin regains Admin API access; a "deleted" user keeps booking during the 30-day grace window (`BookingService.cs:23-61` also never checks `DeletedAt`). `ProfileController` is the only caller that checks it.

**Fix:** Reject login/refresh/Azure-login when `user.DeletedAt != null`. Consider a claims/per-request check so already-issued 60-min JWTs die at next request.

### H2 — JWT signing secret never validated; insecure defaults + silent empty fallback
**Files:** `Program.cs:88-100`, `appsettings.json:18-19`, `docker-compose.prod.yml:24`, `docker-compose.override.yml:32`

The `Jwt.Secret` is fed straight into `SymmetricSecurityKey` for HS256 with no length/placeholder check. `appsettings.json` ships `"CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_KEY_AT_LEAST_32_CHARS"`; prod uses `${JWT_SECRET}` which becomes **empty** (not a startup error) if the env var is missing. A known/empty HMAC key lets anyone forge access tokens with `role=SuperAdmin` (the role claim is trusted from the token, `TokenService.cs:36`).

**Fix:** Fail startup in Production if `Jwt.Secret` is missing, equals the placeholder, or is < 32 bytes. Same guard for `DB_PASSWORD` (currently falls back to `changeme`, `docker-compose.yml:9`): use `${JWT_SECRET:?…}` / `${DB_PASSWORD:?…}` required syntax in the prod overlay.

### H3 — No rate limiting or account lockout on auth endpoints
**Files:** `Program.cs` (no `AddRateLimiter`), `AuthService.cs:52-74`

No ASP.NET rate limiter anywhere; no `AccessFailedCount`/`LockoutEnd` on `User`; `/api/auth/login` and `/register` are anonymous and unthrottled. Enables password brute-force, credential stuffing, and unlimited account creation (which compounds C1).

**Fix:** `AddRateLimiter` with strict per-IP (and per-account) windows on `/api/auth/*`; add a failed-attempt lockout with backoff.

### H4 — Entire app reachable over plaintext HTTP; HSTS disabled
**Files:** `Caddyfile:35-37` (explicit `http://` reverse-proxy block), `Caddyfile:23` (`Strict-Transport-Security "max-age=0"`), `docker-compose.prod.yml:57` (port 80 published to intranet)

The `http://louise.schuler.de` site block disables Caddy's automatic HTTP→HTTPS redirect, and `max-age=0` actively clears any HSTS pin. A user on `http://` performs the full login (password POST) in cleartext on the corporate LAN. The block's "Cloudflare redirect loop" comment is stale — this intranet deployment has no Cloudflare.

**Fix:** Delete the `http://` block so Caddy issues 308 redirects; restore a real HSTS `max-age` once a trusted cert is in place (see M8).

### H5 — SSO trusts unverified `preferred_username` as email
**File:** `AzureAdTokenValidator.cs:57-65`

```csharp
var email = claims.FindFirst("preferred_username")?.Value ?? claims.FindFirst("email")?.Value;
```

In Azure AD v2 tokens `preferred_username` is a **mutable, non-verified** identifier (UPN, email, or phone). It's used as the email that drives account matching/linking (C1/H1). A tenant insider whose UPN can be set to match a local account's email could take it over. The stable `oid` is captured but not what drives the link. Single-tenant issuer validation limits this to tenant members.

**Fix:** Link on `oid` only, or require a verified email claim; do not treat `preferred_username` as verified email.

### H6 — World-readable, unencrypted DB backups containing PII
**Path/Files:** host `/dbbackup` (`drwxr-xr-x`, files `-rw-r--r-- root`), `scripts/db-backup.sh:28-31`, `docker-compose.prod.yml:105`

~30 daily `pg_dump | gzip` files written with no `umask`/`chmod` — world-readable to any local account. Dumps contain employee emails, names, license plates, password hashes, and refresh-token rows. Backups also live only on the same box as the DB (no offsite, no encryption): one disk loss takes prod **and** all backups.

**Fix:** `chmod 700 /dbbackup`; `umask 077` in `db-backup.sh`; encrypt (`gpg`/`age`) and copy off-box.

### H7 — App container runs as root
**Files:** `Dockerfile:26-38` (no `USER`), verified live `docker exec schulerpark-app-1 id` → `uid=0`

Any RCE in the .NET app runs as root inside the container — writable `/app`, access to the DataProtection `/keys` volume, larger escape surface.

**Fix:** Add a non-root user in the runtime stage (`adduser app`, `chown -R app /keys`, `USER app`); port 8080 is unprivileged so nothing else blocks this.

---

## MEDIUM

### M1 — Unbounded date range on availability endpoint → DoS
**Files:** `LocationController.cs:61-75`, `LocationService.cs:82-107`

An explicit `to` value is not capped (only the default is). `GET /api/locations/{id}/availability?from=0001-01-01&to=9999-12-31` loops day-by-day building an in-memory list (~7M tuples). The prod box has ~1.7 GB RAM — a single authenticated request can OOM/stall the app.
**Fix:** Clamp range (e.g. reject `to-from > 62 days`, reject `from < today`).

### M2 — No input validation on register / profile DTOs
**Files:** `DTOs/Auth/RegisterRequest.cs` (no annotations), `AuthService.cs:29-50`, `ProfileController.cs:60-61`

Empty password accepted; no email-format check; `DisplayName > 200` chars throws `DbUpdateException` → 500. Same for `UpdateProfileRequest` (DisplayName/CarLicensePlate unvalidated).
**Fix:** DataAnnotations (`[EmailAddress]`, `[MinLength(8)]`, `[MaxLength]`) or explicit checks.

### M3 — Bootstrap SuperAdmin credentials in plaintext, weak perms, logged
**Files:** `backend/SchulerPark.Infrastructure/Data/Seed/BootstrapAdmin.cs:24-59`, `Program.cs:155`

`admin.yml` (plaintext email + generated password) is written next to the binary with default perms (no `SetUnixFileMode`), never auto-deleted (relies on a comment), and the password is printed to stdout on the write-failure branch → `docker logs`. Runs in all non-Testing environments including Production. Password entropy (~96 bits) is fine.
**Fix:** Write `0600`, never log the password, prefer secret-manager/env delivery, force reset on first login.

### M4 — User enumeration (register 409 + login timing + case-sensitive email)
**Files:** `AuthController.cs:48-51` (`Conflict("Email is already registered.")`), `AuthService.cs:55-63` (early return skips PBKDF2 for unknown email), `:32/:55` (`u.Email == email`, case-sensitive)

Register confirms account existence; login returns measurably faster for nonexistent emails (no hash computed). Case-sensitivity also allows duplicate logical identities and breaks the email-link path.
**Fix:** Generic register response; dummy-hash verify on unknown-user login to equalize timing; normalize email to lowercase.

### M5 — No Content-Security-Policy header
**Files:** `Caddyfile:19-30` (sets nosniff/X-Frame-Options/Referrer-Policy/Permissions-Policy but no CSP); `frontend/index.html` (no CSP meta)

No `script-src` restriction — any future XSS has free rein to exfiltrate the in-memory access token or abuse the refresh cookie.
**Fix:** Add CSP to the Caddy header block, e.g. `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'` (tune for Tailwind inline styles; drop the Google Fonts allowance if L7 is fixed).

### M6 — `deploy.sh --rollback` sources a world-writable-dir file (local code exec)
**Files:** `scripts/deploy.sh:57` (`STATE_FILE=/tmp/schulerpark-deploy.last`), `:621` (`source "$STATE_FILE"`)

Any local user can pre-create `/tmp/schulerpark-deploy.last` with arbitrary shell; the next rollback executes it as the deploy user (who has docker-daemon = root-equivalent access).
**Fix:** Move state to `$REPO/.deploy-state` (`0600`), or parse strict `KEY=VALUE` and verify ownership (`[[ -O $STATE_FILE ]]`) before use.

### M7 — Third-party deploy action pinned by mutable tag while holding prod SSH key
**File:** `.github/workflows/ci.yml:124` (`appleboy/ssh-action@v1` + `secrets.DEPLOY_KEY`)

`v1` is mutable on a third-party repo; a compromised release could exfiltrate the production SSH private key.
**Fix:** Pin `appleboy/ssh-action` to a full commit SHA; consider a dedicated deploy user with a forced command instead of a general shell key.

### M8 — TLS is a self-signed internal CA on the canonical hostnames
**File:** `Caddyfile:15` (`tls internal`)

Every user clicks through cert warnings (training them to accept MITM) and HSTS can't be enabled (H4). Known stopgap.
**Fix:** Complete the Let's Encrypt DNS-01 plan (`docs/plans/phase-15-letsencrypt-dns01.md`) or deploy an internal-CA cert trusted by clients.

---

## LOW

- **L1 — `Enum.TryParse` without `Enum.IsDefined`.** `BookingController.cs:28,48` (TimeSlot), `UsersAdminController.cs:70` (UserRole), `AdminController.cs:107`, `LotteryController.cs:33`. `"7"` parses to an undefined enum value and gets persisted, breaking lottery matching / role state. **Fix:** add `&& Enum.IsDefined(...)`.
- **L2 — Production build ships source maps.** `frontend/vite.config.ts:65` (`sourcemap: true`). Full TS source is downloadable via `*.js.map` (recon only; no secrets in source). **Fix:** `sourcemap: mode !== 'production'` or block `*.map` in Caddy.
- **L3 — Google Fonts from external CDN.** `frontend/index.html:12-17`. Sends every user's IP to Google (GDPR concern for a German employee app; cf. LG München I 3 O 17493/20); also breaks CSP purity and may hang on the proxied intranet. **Fix:** self-host Inter via `@fontsource/inter`.
- **L4 — Push endpoint sent in DELETE query string.** `frontend/src/services/pushService.ts:14-15`. The endpoint (a bearer capability) lands in access logs, unlike the POST-body subscribe. **Fix:** send in the request body.
- **L5 — Service worker navigates to unvalidated push URL.** `frontend/src/sw.ts:35,43-52`. VAPID-authenticated, but a server bug could redirect users to an arbitrary site from a trusted OS notification. **Fix:** origin-check `data.url` against `self.location.origin`.
- **L6 — Cancel/rebook loop grows rows unbounded.** `BookingService.cs:38-43,207`. Cancelled rows persist ~1 year; storage nuisance/DoS only. **Fix:** per-user daily creation cap.
- **L7 — Azure validator swallows all exceptions.** `AzureAdTokenValidator.cs:48-70` (bare `catch { return null; }`). Fail-closed is correct, but network/key-rollover failures look identical to bad tokens and there's no telemetry. **Fix:** catch `SecurityTokenException` specifically; log unexpected errors.
- **L8 — Logout cookie delete doesn't mirror all attributes.** `AuthController.cs:153` deletes with `Path` only (set uses HttpOnly/Secure/SameSite=Strict). Generally works; mirror attributes for robustness.

---

## Checked and confirmed sound (strengths)

- **Object-level authorization (no IDOR):** bookings, profile, DSGVO export/delete, and push subscriptions all derive the target from `ClaimTypes.NameIdentifier`, never from request-supplied IDs. Ownership checks: `BookingService.cs:199-200,228-229`; `PushController.cs:70-72`.
- **Role model:** `UsersAdminController` is `SuperAdminOnly`; self-role-change/self-disable/self-delete and last-SuperAdmin protections all enforced; role claim minted only from DB (`TokenService.cs:36`), register hardcodes `UserRole.User`. No mass-assignment path to `Role`.
- **Password hashing:** ASP.NET `PasswordHasher<User>` (PBKDF2-HMAC-SHA256, per-user salt, versioned), transparent rehash on `SuccessRehashNeeded`.
- **Refresh tokens:** 512-bit CSPRNG, stored SHA-256-hashed (never plaintext), rotated on use, **reuse detected → whole family revoked** (`TokenService.cs:84-88,122-126`).
- **Cookie flags:** refresh cookie is `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`.
- **JWT validation:** issuer/audience/lifetime/signing-key validated, tight 1-min clock skew.
- **Frontend token handling:** access token in a module-scoped variable only (never `localStorage`/`sessionStorage`); no `dangerouslySetInnerHTML`/`eval`/`innerHTML` anywhere; service worker caches only `/api/locations` (no PII); no `VITE_*`/secrets in the bundle (API base is relative `/api`).
- **No SQL injection** (zero raw SQL; all parameterized EF LINQ), **no CORS misconfig** (no CORS policy → same-origin only), **no file uploads**, paging clamped everywhere.
- **Exception middleware** returns a fixed generic message + traceId; stack traces logged server-side only.
- **Secrets hygiene:** `.env` is `600`, never committed (verified via git history); `admin.yml` gitignored + `.dockerignore`'d + absent from the running container; only Caddy 80/443 published (Postgres 5432 and app 8080 are compose-network-only); Caddy admin API 2019 not exposed; Postgres host auth is `scram-sha-256`; dev seed accounts (`Admin123!`) gated to Development only.
- **Hangfire & Swagger** are Development-only (fall through to SPA in Production).
- **Lottery trigger** is `AdminOnly` and idempotent.

---

## Remediation priority

1. **C1** — email verification + fix SSO auto-link (do this alongside the SSO rollout).
2. **H1, H2, H3** — one-line `DeletedAt` checks; startup secret guard; add rate limiter. Small, high value.
3. **H4, H6, H7** — delete the `http://` Caddy block; `chmod`/encrypt backups; non-root container. Ops-side, quick.
4. **H5** — link SSO on `oid`, not `preferred_username`.
5. **M1, M2, M4** — range clamp, DTO validation, enumeration hardening.
6. **M3, M5–M8** — bootstrap creds perms, CSP, deploy.sh state file, action pinning, real TLS.
7. **L1–L8** — cleanups as capacity allows.

---

### Reviewer notes / operational
- During this review session, `.env` contents (DB password, JWT secret) were echoed into a terminal session via a `vi` attempt. Not remotely exploitable, but if that box's session/transcript is ever shared, rotating `JWT_SECRET` and `DB_PASSWORD` is cheap insurance — and rotating `JWT_SECRET` is the clean remedy for H2 regardless.
- **Doc drift:** `CLAUDE.md` states Caddy is a custom build with the DNS-01 plugin (`Dockerfile.caddy`), but HEAD runs stock `caddy:2-alpine` with `tls internal` and `Dockerfile.caddy` is absent. Reconcile the docs to avoid misleading future operators.
- `npm audit` could not run on the box (no Node). No known advisories against pinned deps as of review, but add `npm audit --omit=dev` to the CI frontend job.
