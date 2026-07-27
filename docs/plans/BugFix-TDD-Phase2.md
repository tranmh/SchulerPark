# TDD Bug-Fix Plan — remaining findings (#21–#56)

**Source:** `Bugs-SchulerPark-master.md`
**Approach:** TDD — write the failing ("red") test first, make it pass with the minimal fix ("green"), then refactor. Mark each bug ✅ **Done** in the status table the moment its test passes.
**Baseline:** Phase 1 (#1–#20) complete and merged to `master`. This plan covers everything still open.
**Prod note (standing):** Local login/registration is **disabled in production (SSO-only)**. Findings whose only vector is a local-auth endpoint are therefore **dev-only in prod** — flagged below; they are still worth fixing for defence-in-depth and dev safety, but rank lower.

---

## Worth-check triage (do this first)

Every open finding assessed for whether it earns a fix. Verdicts: **Fix** (clear value) · **Fix (low)** (cheap, latent/minor) · **Decide** (needs a product call, not a mechanical fix) · **Drop** (not worth code).

**Impact if fixed / ignorable?** ranks real-world value independent of severity (added 2026-07-21): **Fix now** / **High impact** = prod-real, fires in normal operation or catastrophic-if-it-happens · Worth it = prod-real but recoverable/conditional · Low value = cheap but minor · Decide = policy call · Deferred = already decided · **Ignore** = safe to skip, with the condition that keeps it safe — _Ignore (prod)_ holds only while production stays SSO-only (local login/registration off); _Ignore (latent)_ holds only under current config (e.g. #35 becomes real if an early-morning deadline hour is added); _Ignore (hygiene)_ is a non-runtime repo-cleanliness item.

| # | Sev | Title (short) | Verdict | Why | Impact if fixed / ignorable? |
|---|-----|---------------|:-------:|-----|-----|
| #21 | Med | Booking date window `toISOString()` drift/DST | ✅ **Fixed** | User-visible: can offer/hide the wrong day near midnight/DST | **Fix now** — deterministic wrong day near midnight/DST on the core booking action |
| #22 | Med | Availability request race on location switch | **Fix** | Calendar can show the wrong location's availability | Worth it — removes wrong-location availability; cheap cancelled-flag |
| #23 | Med | Axios refresh bypasses single-flight guard | ✅ **Fixed** | Double token rotation → spurious reuse-revoke logout | **Fix now** — prevents spurious forced-logout of active users |
| #24 | Med | E2E vacuous assertions (accept failure as pass) | ✅ **Fixed** | Regressions in booking/confirm/week-booking pass CI green | Worth it — restores the CI net guarding every other correctness fix |
| #25 | Med | Service worker serves stale shell after deploy | **Fix** | Installed-PWA users hit a broken bundle after a breaking deploy | Worth it — no broken-bundle wave after a breaking deploy |
| #26 | Med | Deploy doc: prod creds that don't exist | **Fix (low)** | Doc-only, but pushes operators toward `ASPNETCORE_ENVIRONMENT=Development` | Ignore (prod) — doc-only; safe while prod stays Development-gated off |
| #27 | Med | Internal host/IP/OS/user leaked in committed docs | **Fix (low)** | Redact real infra + username from the repo (recon material) | Ignore (hygiene) — not runtime; redact only if repo is shared/public |
| #28 | Low | Hangfire dashboard auth disabled (dev only) | **Fix (low)** | Dev-hardening; real filter instead of `AllowAnonymous()` | Ignore (prod) — dev-gated dashboard; not a prod surface |
| #29 | Low | Dev/E2E weak seed creds on reachable ports | **Fix (low)** | Dev-hardening; env-sourced seed pw + loopback binds | Ignore (prod) — dev-gated seed creds; not a prod surface |
| #30 | Low | Postgres/MailHog ports published in dev | **Fix (low)** | Bind to `127.0.0.1` (pairs with #29) | Ignore (prod) — dev-only port exposure |
| #31 | Low | `AllowedHosts: "*"` | **Fix (low)** | One-line prod-config defence-in-depth | Low value — one-line prod defence-in-depth |
| #32 | Low | Verbose validation messages aid enumeration | **Fix (low)** | Small; genericise object-existence leaks | Ignore — low-value shared objects; minimal disclosure |
| #35 | Low | Confirmation deadline DST-fragile | **Fix (low)** | Latent today (06:00/13:00) but a real trap for any early hour | Ignore (latent) — safe unless an early-morning deadline hour is added |
| #36 | Low | Navigate during render (Login/Register) | **Fix (low)** | Declarative `<Navigate>` — clean, cheap | Ignore — blank screen fixed by refresh; already-authed edge only |
| #37 | Low | Async `onClick` → unhandled rejection | **Fix (low)** | Reuses the #11 `pushError` surface | Ignore — recoverable push-toggle failure; frontend only |
| #38 | Low | `DeadlineHelper` throws w/o ICU on Windows | **Fix** | **Improves our own Windows `dotnet test` reliability** | Ignore (latent) — modern .NET ships ICU; dev-portability only |
| #39 | Low | `location!` null-deref in lottery | **Fix (low)** | Rare but leaves bookings stuck `Pending` (ties #10) — Track A | Ignore — rare mid-run location delete; already caught by per-slot catch |
| #40 | Low | CalendarPicker past-date comparison | **Fix (low)** | Latent (Booking always passes `minDate`); cheap guard | Ignore (latent) — cannot fire; Booking always passes minDate |
| #41 | Low | `User.UpdatedAt` not auto-bumped | **Fix (low)** | Data quality; SaveChanges interceptor | Low value — data-quality only (stale UpdatedAt on some paths) |
| #42 | Low | `Location` cascade delete destroys history | ✅ **Fixed** | One misclick erases all bookings + audit/GDPR history — Track A | **High impact / low odds** — prevents irreversible loss of all bookings + GDPR history on a location delete |
| #43 | Low | Dashboard "waiting" count over truncated page | **Drop→optional** | Cosmetic greeting stat; nearest thing to a styling bug. Ship only if trivial | Ignore — cosmetic greeting stat; needs 6+ upcoming Won |
| #44 | Low | Expiry job emails "please confirm" while expiring | ✅ **Fixed** | Real user confusion; needs an "expired" template | **Fix now** — stops the misleading please-confirm email on every expiry |
| #45 | Low | Fire-and-forget on a disposed `DbContext` | **Fix (low)** | Push notifications silently lost after jobs return — Track A | Ignore — worst case a lost push notification; no data impact |
| #46 | Med | Registration timing/SMTP side-channel + bombing | **Fix** *(dev-only in prod)* | Email-bombing of third parties is the real part | Ignore (prod) — registration off under SSO; real if local register re-enabled |
| #47 | Med | Account-lockout DoS; counter never decays | **Fix** *(dev-only in prod)* | Attacker can lock a known email out | Ignore (prod) — local login off under SSO; real if re-enabled |
| #48 | Med | Rate-limit policy mis-scoped to read/refresh | ✅ **Fixed** | **Prod-real:** office behind one NAT IP → spurious 429s | **Fix now** — stops office-wide 429s behind one NAT IP |
| #49 | Low–Med | `OnTokenValidated` DB query every request | ✅ **Fixed** | **Prod-real:** doubles round-trips on every authed call | **Fix now** — removes a DB round-trip on 100% of authed requests |
| #50 | Med | Registration check-then-insert race | **Defer** | Corporate email is unique per person, so this is never two *distinct* users — only the same identity double-submitting. #9's unique index already prevents duplicate rows; only an ugly 500 on the losing click remains. Local register is **off in prod (SSO)**, so the sole prod flavour is a rare SSO/local timing overlap. Deferred 2026-07-21. | Deferred — corporate email unique; #9 index covers dup rows |
| #51 | Low | Lockout branch skips dummy hash (timing) | **Fix (low)** *(dev-only in prod)* | One-line; do it with #46/#47 | Ignore (prod) — dev-only timing oracle under SSO |
| #52 | Low | `refreshToken.User` null-deref | **Fix (low)** | **Prod-real:** `/refresh` 500 if a token outlives its user | Worth it — one-line guard; clean 401 instead of /refresh 500 |
| #53 | Med | Week booking no longer atomic | ✅ **Fixed** | Partial commits + skipped confirmations on mid-week failure — Track A | **High impact** — prevents partial-week commits + skipped confirmations |
| #54 | Low | Retry-exhausted `DbUpdateException` → raw 500 | **Fix (low)** | Map to a friendly conflict ProblemDetails — Track A | Ignore — cosmetic 500-to-4xx on a rare retry-exhaustion |
| #55 | Low | Direct assignment writes no `LotteryHistory` | **Decide** | Fairness policy call — not a mechanical bug | Decide — fairness policy call, not a mechanical bug |
| #56 | Low | Booking during lottery run stuck `Pending` | **Fix** | Narrow re-introduction of #10 on the direct-assignment path — Track A | Ignore — very narrow sub-second race during the 22:00 run |
| #57 | Low | No notification when a `Won` booking expires | **Fix (low)** | User never told their spot expired; with #44 the only message misleads. New `SendBookingExpiredAsync` template | Worth it — pairs with #44; user gets silence today when their spot is released |
| — | — | *Informational: XFF/`KnownProxies` note* | **Removed** | Safe as configured; removed from report 2026-07-16 | — |
| — | — | *Test-gap note (Testcontainers)* | **Removed/Done** | Satisfied by Phase 1 `DbIntegrityTests` | — |

**Net: 31 to fix (5 of them "low", incl. #57 added 2026-07-21), 1 to decide (#55), 1 deferred (#50), 1 optional (#43), 2 informational removed.**

---

## Execution order & tracks

Grouped so related fixes share a test harness and a commit. Tracks are independent; do them in roughly this order (value × prod-relevance first).

> **#46–#56 have full "existing code → change → why → test" specs** in the *Detailed fix specs* section below (the report lists them tersely with no diffs). The track bullets for those bugs are just the one-line summary.

### Track A — Booking/lottery correctness & lifetime (backend, real-Postgres) — **highest value**
Reuses the Phase 1 `PostgresFixture` (Testcontainers, graceful Docker-skip) + `[SkippableFact]`.

- **#45 — fire-and-forget on a disposed `DbContext`.**
  *Fix:* in `WaitlistService`/`LotteryService`/`ConfirmationExpiryJob`, collect the notification `Task`s and `await Task.WhenAll(...)` before the method returns (or give `PushNotificationService` its own `IServiceScopeFactory` scope).
  *Test (`NotificationLifetimeTests`):* run a lottery/waitlist path, then assert the push service completed its `_db` read (recording fake) — **red** today because the discarded task races scope disposal / throws `ObjectDisposedException`; **green** once awaited. Pairs cleanly with #44.

- **#39 — `location!` null-deref.**
  *Fix:* null-check the `FirstOrDefaultAsync` result in `RunLotteryForSlotAsync`; log-and-skip; drop the two later `location!` uses.
  *Test:* delete/deactivate the location between the active-locations read and the per-slot call (interceptor or a pre-seeded gap) → **red** NRE, **green** clean skip with no stuck `Pending`.

- **#42 — `Location` cascade delete destroys history.**
  *Fix:* `Location → Bookings` and `Location → LotteryHistories` FKs to `DeleteBehavior.Restrict`; decommission via `Location.IsActive`. Ship a migration.
  *Test (`DbIntegrityTests`, extend):* seeding a location with a booking then `Remove`+`SaveChanges` on a **fresh context** must throw (FK restrict) — **red** with `Cascade`, **green** with `Restrict`. (Fresh context so EF client-fixup doesn't mask it — same lesson as #18.)

- **#53 — week booking not atomic.**
  *Fix:* wrap the `CreateWeekBookingAsync` day loop in one `IDbContextTransaction` (keep per-day slot-conflict retry inside); commit once; notify after commit.
  *Test:* force day-4 to fail unrecoverably (interceptor) → **red** days 1–3 committed + no notifications; **green** whole week rolled back, single clean error.

- **#54 — retry-exhausted `DbUpdateException` → raw 500.**
  *Fix:* on the 3rd slot-conflict attempt, translate the `PostgresException`/`DbUpdateException` to a `ValidationException`/409 in `BookingService`.
  *Test:* interceptor that keeps raising the unique-violation → **red** opaque 500 / raw exception; **green** mapped conflict ProblemDetails.

- **#56 — booking created mid-lottery stuck `Pending`.**
  *Fix:* extend the Phase-1 lottery post-commit sweep to also re-scan for `Pending` on the slot after `RecordRun`, or route leftovers to direct-assignment/waitlist. (Same sweep mechanism as #10 — this is its direct-assignment sibling.)
  *Test (extend `LotteryRaceTests`):* POST after the lottery's read but before `RecordRun` → **red** left `Pending`; **green** resolved (`Lost`/assigned).

### Track B — Auth robustness (prod-relevant) (backend, in-process `WebApplicationFactory` / unit)
- **#49 — `OnTokenValidated` DB query per request.** Fold the deleted/active check into the existing per-request user load, or cache it briefly (`IMemoryCache`, short TTL). *Test:* count DB round-trips per authed request (recording interceptor) — **red** extra `Users.AnyAsync` every call; **green** cached/folded. Keep fail-closed behaviour.
- **#52 — `refreshToken.User` null-deref.** Null-check in `TokenService`; treat null as invalid → 401. *Test:* seed a refresh token whose user row is gone → **red** 500/NRE, **green** 401.
- **#48 — rate-limit policy mis-scoped.** Move `[EnableRateLimiting("auth")]` off the controller class and onto `login`/`register`/`resend-verification` only; give `/me`,`/config`,`/refresh`,`/logout` a separate, higher limit (or none). *Test:* hammer `/refresh` under the limit-count and assert no 429; assert `login` still throttles. (Relaxed in `Testing` env as today.)

### Track C — Auth hardening (dev-only in prod, SSO) (backend)
Lower priority — vectors live on local-auth endpoints that are off in production. Batch together.
- **#46 — registration timing/SMTP side-channel + email bombing.** Equalise work across both branches; move the verification email to a background queue (fire-and-forget off the request). *Test:* the existing-vs-new branch no longer differs in DB-write/awaited-send shape; assert no synchronous SMTP await on the request path.
- **#47 — lockout DoS.** Decay/reset `AccessFailedCount` after the lockout window; prefer IP/attempt throttling over persistent per-account lockout. *Test:* after the window elapses a correct password succeeds without an admin reset.
- **#51 — lockout branch skips dummy hash.** Run the same dummy `VerifyHashedPassword` on the lockout branch. *Test:* response-shape/latency parity between locked-existing and unknown user. (Near-informational; include because it's one line.)

### Track D — Timezone & data quality (backend, pure unit)
- **#38 — `DeadlineHelper` ICU fallback.** `TryFindSystemTimeZoneById("Europe/Berlin")` → fall back to `"W. Europe Standard Time"`; centralise the resolver (also used in `LotteryJob`, `Program.cs`, `EmailService`). **Do early — it de-flakes our own Windows test runs.** *Test:* resolver returns a Berlin zone; add a guard test.
- **#35 — DST-fragile deadline.** Skip the spring-forward gap (`BerlinTz.IsInvalidTime` → `+1h`); use explicit `DateTimeKind.Unspecified`. *Test:* a 02:30 deadline hour on the March DST date converts without throwing (**red** `ArgumentException`, **green** safe).
- **#41 — `UpdatedAt` not auto-bumped.** Add a `SaveChanges`/`SaveChangesAsync` override (or interceptor) on `AppDbContext` stamping modified `User`s. *Test:* mutate a user field that no code path stamps → **red** stale `UpdatedAt`, **green** bumped.

### Track E — Frontend correctness (Vitest + Testing Library)
- **#21 — date-window `toISOString()` drift.** Format from local Y/M/D and add days on the `Date`. *Test:* fake a near-midnight/DST clock; assert `minDate`/`maxDate` are the intended local days.
- **#22 — availability request race.** `cancelled` flag / `AbortController` in the effect (Booking + Profile slot-load). *Test:* resolve A after B; assert B's data wins and no `setState`-after-unmount.
- **#23 — refresh single-flight.** Route the interceptor through `authService.refresh()` (lazy import to dodge the cycle) or hoist one guard. *Test:* two simultaneous 401s → exactly one `/auth/refresh` call.
- **#25 — stale service worker.** `self.skipWaiting()` + `clientsClaim()` (or a "new version — reload" prompt); confirm nav-fallback only serves `index.html`. *Test:* SW unit/registration test asserts skipWaiting/claim wired.
- **#36 — navigate during render.** `return <Navigate to="/" replace />` in Login/Register (add to the `react-router-dom` import). *Test:* authenticated render redirects without a `null` frame / act-warning.
- **#37 — async `onClick`.** Wrap `disablePush`/`requestPush`: `onClick={() => void fn().catch(() => setError(t('profile.pushError')))}`. *Test:* forced rejection surfaces the error, no unhandled rejection.
- **#40 — CalendarPicker comparison.** Compare `yyyy-mm-dd` strings on both branches; derive today's string. *Test:* without `minDate`, past days disabled correctly across a mocked midnight.
- **#43 — dashboard truncated count** *(optional/cosmetic).* Only if trivial: raise the stat's page size or add a count field. Skip if it needs an API change this cycle.

### Track F — Test quality (E2E)
- **#24 — vacuous assertions.** Seed deterministic state, then assert strictly: `[200,201]` (not `400`), require `/my-bookings` navigation, require `createdBookings.length > 0`. *Verification:* break booking creation locally → the tightened specs go red (they currently stay green).

### Track G — Config & docs hardening (mostly review-verified, minimal tests)
- **#31** `AllowedHosts` → real host in `appsettings.Production.json`/env. *Test:* spoofed `Host` rejected (in-process).
- **#32** genericise object-existence validation messages (flag or per-throw-site). *Test:* "Location not found"-class message no longer distinguishes existence.
- **#28** real `AdminDashboardAuthorizationFilter` on the Hangfire dashboard (drop `AllowAnonymous()`). *Test:* unauthenticated `/hangfire` challenged (dev harness).
- **#29 / #30** env-sourced seed password (random default) + bind dev/e2e Postgres/MailHog/app ports to `127.0.0.1`. **No unit test — verify by config review** (compose/`SeedData` change).
- **#26 / #27** docs: real `admin.yml` first-login flow; redact host/IP/OS/username to placeholders (+ drop hard-coded IP from `Caddyfile`; fix `CLAUDE.md` Default Credentials). **Doc-only — verify by review.**

---

## Detailed fix specs — Phase 16/17 findings (#46–#56)

These 11 were found reviewing the newly-committed Phase 16 (auth hardening) and Phase 17 (direct slot assignment) code, so the report lists them tersely with no diff. Below is the same *existing code → change → why → test* treatment the report gives #1–#45. All line numbers are against the current `SchulerPark-latest`.

### #46 — Registration timing / SMTP side-channel + email bombing  *(dev-only in prod: SSO)*

**Existing** — `AuthService.RegisterAsync` (`AuthService.cs:55-85`) and its helper `IssueVerificationTokenAsync` (`:247-265`):
```csharp
var existing = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
if (existing != null)
{
    if (!existing.EmailVerified && existing.DeletedAt == null)
        await IssueVerificationTokenAsync(existing);   // SaveChanges + awaits SMTP
    return;                                            // verified/deleted: returns immediately, no work
}
// new address: INSERT + IssueVerificationTokenAsync (SaveChanges + awaits SMTP)
...
private async Task IssueVerificationTokenAsync(User user)
{
    ...
    await _context.SaveChangesAsync();
    await _emailService.SendEmailVerificationAsync(user.Email, user.DisplayName, link); // ← synchronous, in-request
}
```
**Why it's still a leak after #5.** #5 equalised the *status code* (always `Ok`), but not the *work*: a verified/soft-deleted address returns at `:67` with no DB write and no email, while a new address does an INSERT and then **awaits an SMTP round-trip** on the request thread. Response latency therefore still separates "new" from "already verified", and because the send is in-request, registering arbitrary third-party addresses both emails them a verification link (bombing) and makes the caller wait for the SMTP hop.

**Change** — take the email off the request path (matches the codebase's existing fire-and-forget idiom in `BookingService.SendDirectAssignmentNotifications`), so latency no longer depends on SMTP and the caller can't drive synchronous sends:
```diff
-        await _context.SaveChangesAsync();
-        ...
-        await _emailService.SendEmailVerificationAsync(user.Email, user.DisplayName, link);
+        await _context.SaveChangesAsync();
+        ...
+        // Fire-and-forget: the SMTP hop must not sit on the request thread (timing
+        // oracle + lets a caller drive third-party sends synchronously). EmailService
+        // already logs its own failures.
+        _ = _emailService.SendEmailVerificationAsync(user.Email, user.DisplayName, link);
```
**Honest scope.** This kills the big signal (the SMTP hop) and the in-request bombing lever; the residual micro-difference between "one INSERT" and "no-op" is not worth contorting the code for, and register is **dev-only in production** (SSO). Bombing volume is bounded by the register rate-limit once #48 scopes the tight policy onto `register`. If you want it airtight, enqueue via Hangfire (`BackgroundJob.Enqueue`) instead of `_ =`.

**Test (`AuthEnumerationTests`, in-process):** stub `IEmailService` with a slow send; assert `POST /api/auth/register` returns before the send completes (no synchronous await) for both a new and an already-verified address. Red today (new-address path blocks on the send); green once detached.

---

### #47 — Account-lockout DoS; counter never decays  *(dev-only in prod: SSO)*

**Existing** — `LoginAsync` (`AuthService.cs:128-146`) + `RegisterFailedAttemptAsync` (`:267-284`):
```csharp
if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
    throw new UnauthorizedAccessException("Invalid email or password.");
...
// AccessFailedCount is only ever reset on a SUCCESSFUL login (:142-146).
```
`RegisterFailedAttemptAsync` locks after 5 failures with exponential backoff (`2^(n-5)` min, capped 1 h). The count only resets on success.

**Why it's a bug.** Anyone who knows a victim's email can burn 5 wrong passwords (within the 10/min IP limit) to lock them out; because the counter never decays, each subsequent failure escalates the window, so a victim who never *successfully* logs in stays locked essentially forever. Lockout has become an attacker-controlled DoS.

**Change** — decay the counter once a lockout window has elapsed, so failures don't accumulate indefinitely; lean on the per-IP limiter (#48) as the real brute-force control:
```diff
-        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
-            throw new UnauthorizedAccessException("Invalid email or password.");
+        if (user.LockoutEnd.HasValue)
+        {
+            if (user.LockoutEnd.Value > DateTime.UtcNow)
+                throw new UnauthorizedAccessException("Invalid email or password.");
+
+            // Window elapsed: decay, don't escalate. Start the next window fresh so a
+            // never-succeeding victim isn't locked out permanently.
+            user.AccessFailedCount = 0;
+            user.LockoutEnd = null;
+        }
```
**Test:** lock an account (5 failures), advance `LockoutEnd` into the past, then a correct password logs in **without** an admin reset. Red today (escalating lock never clears); green after decay.

---

### #48 — Auth rate-limit policy mis-scoped to read/refresh endpoints  *(prod-real)*

**Existing** — `AuthController.cs:16` applies the tight policy to the **whole controller**; the policy is 10 req/min **per IP** (`Program.cs:168-183`):
```csharp
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]          // ← class-level: covers /me, /config, /refresh, /logout too
public class AuthController : ControllerBase
...
var authPermitLimit = builder.Configuration.GetValue("RateLimit:AuthPermitLimit", 10);
options.AddPolicy("auth", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", ...));  // partition = IP
```
**Why it's a bug (and prod-real even under SSO).** `/me`, `/config`, `/refresh`, `/logout` are hit on every app boot, per tab, and on PWA revalidation — but they share the 10/min brute-force bucket. On an intranet where the whole office egresses one NAT IP, everyone lands in the *same* partition, so normal use trips `429` for all of them. These endpoints run in production regardless of SSO.

**Change** — scope the tight policy to the sensitive POSTs only; let reads/refresh fall back to the 300/min global limiter:
```diff
 [ApiController]
 [Route("api/auth")]
-[EnableRateLimiting("auth")]
 public class AuthController : ControllerBase
```
```diff
 [HttpPost("register")]
+[EnableRateLimiting("auth")]
 public async Task<IActionResult> Register([FromBody] RegisterRequest request)
```
Add the same `[EnableRateLimiting("auth")]` to the `login` and `resend-verification` actions only. `/me`, `/config`, `/refresh`, `/logout` keep the global limiter (already 300/min per IP).

**Test (`RateLimitScopeTests`, in-process):** fire >10 `GET /api/auth/me` in a minute from one client → **no** `429` (red today: 429 after 10); fire >N `login` → still throttled (guards against over-correcting).

---

### #49 — `OnTokenValidated` runs a DB query on every authenticated request  *(prod-real)*

**Existing** — `Program.cs:142-156`:
```csharp
OnTokenValidated = async context =>
{
    var sub = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(sub, out var userId)) { context.Fail("Invalid subject claim."); return; }

    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
    var active = await db.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null); // ← every request
    if (!active) context.Fail("Account is disabled or deleted.");
}
```
**Why it's a bug.** This is the #4 revocation check, but it issues a `Users` query on **every** authenticated call — doubling round-trips where the handler already loads the user, and amplifying any valid-token flood into DB load. (It fails closed, which is correct.)

**Change** — cache the active/deleted result briefly with `IMemoryCache`; a disabled account then keeps access for at most the TTL — the same bounded window the 60-min token already tolerates:
```diff
 var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
-var active = await db.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null);
+var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
+var active = await cache.GetOrCreateAsync($"user-active:{userId}", entry =>
+{
+    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
+    return db.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null);
+});
 if (!active) context.Fail("Account is disabled or deleted.");
```
Register `builder.Services.AddMemoryCache();` if it isn't already. On disable, optionally evict `user-active:{id}` for instant effect.

**Test:** issue N authed requests with one token behind a recording `DbCommandInterceptor` → exactly **1** `Users` query, not N. Red today (N); green after caching.

---

### #50 — Registration is check-then-insert  *(largely pre-solved by #9 — ⏸️ DEFERRED 2026-07-21)*

> ⏸️ **Deferred.** Corporate email is unique per person, so this is never two distinct users racing — only the same identity double-submitting (double-click, retry, two tabs, or an SSO/local timing overlap). #9's unique index already guarantees no duplicate row; the only remnant is an ugly 500 on the losing click, and local registration is off in production (SSO). Not worth a cycle now. The spec below is kept for when/if a friendly-response polish is wanted.

**Existing** — `AuthService.cs:59-84`: `FirstOrDefaultAsync(Email==email)` then `Add`. **The DB unique index this needed already shipped in Phase 1 (#9):** `UserConfiguration.cs:26` — `HasIndex(u => u.Email).IsUnique().HasFilter("\"DeletedAt\" is null")`.

**Why there's still a remnant.** With the index in place, two concurrent registrations can no longer create duplicate rows — but the second INSERT now throws a unique-violation `DbUpdateException`, which `ExceptionHandlingMiddleware` doesn't map → an opaque 500 (and a confusing one, since the response is supposed to be uniform).

**Change** — catch the unique violation on the new-user path and fold it into the same uniform response (treat "lost the race" as "already registered"):
```diff
     user.PasswordHash = _passwordHasher.HashPassword(user, password);
     _context.Users.Add(user);
-    await IssueVerificationTokenAsync(user);
+    try
+    {
+        await IssueVerificationTokenAsync(user);   // does SaveChanges
+    }
+    catch (DbUpdateException ex) when (
+        ex.InnerException is Npgsql.PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
+    {
+        // Concurrent registration of the same email won the race; the unique Email
+        // index (#9) rejected our insert. Respond as if already registered — uniform.
+    }
```
**Test (`DbIntegrityTests`, real Postgres):** two `RegisterAsync` calls for the same normalised email on separate contexts → exactly **one** row and **no** exception escapes. Red today (second throws `DbUpdateException`); green after the catch.

---

### #51 — Lockout branch skips the dummy hash → timing oracle  *(dev-only in prod: SSO)*

**Existing** — `LoginAsync` (`AuthService.cs:121-131`):
```csharp
if (user == null || string.IsNullOrEmpty(user.PasswordHash) || user.DeletedAt != null)
{
    _passwordHasher.VerifyHashedPassword(new User(), DummyPasswordHash.Value, password); // equalise
    throw new UnauthorizedAccessException("Invalid email or password.");
}
if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
    throw new UnauthorizedAccessException("Invalid email or password.");   // ← throws with NO hash op
var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
```
**Why it's a bug.** Unknown users pay a dummy-hash cost; a *locked existing* account throws immediately with no hash — so it responds measurably faster, revealing "this email exists and is locked." (One line; do it alongside #47, which touches the same branch.)

**Change** — run the dummy hash before the lockout throw:
```diff
-        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
-            throw new UnauthorizedAccessException("Invalid email or password.");
+        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
+        {
+            _passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, password); // equalise latency
+            throw new UnauthorizedAccessException("Invalid email or password.");
+        }
```
**Test:** measure/assert the code path — locked account runs one `VerifyHashedPassword` before throwing (assert via a spy hasher call-count = 1 on the lockout branch). (Micro-timing is hard to assert directly; call-count parity is the deterministic proxy.)

---

### #52 — `refreshToken.User` dereferenced without a null guard  *(prod-real)*

**Existing** — `TokenService.ValidateRefreshTokenAsync` (`TokenService.cs:76-98`):
```csharp
var refreshToken = await _context.RefreshTokens.Include(rt => rt.User)
    .FirstOrDefaultAsync(rt => rt.Token == tokenHash);
...
if (refreshToken.User.DeletedAt != null || !refreshToken.User.EmailVerified)  // ← .User assumed non-null
    return null;
return refreshToken.User;
```
**Why it's a bug.** Safe *today* (retention deletes tokens before the user row), but the code silently assumes a token never outlives its user. Any future hard-delete path that skips token cleanup turns `/refresh` into an uncaught `NullReferenceException` → 500. This runs in production (refresh is not a local-auth-only path).

**Change** — treat a userless token as invalid:
```diff
     if (refreshToken.IsExpired)
         return null;
 
+    // A token should never outlive its user, but if it does, fail closed rather
+    // than NRE on the next line.
+    if (refreshToken.User is null)
+        return null;
+
     if (refreshToken.User.DeletedAt != null || !refreshToken.User.EmailVerified)
         return null;
```
**Test (`DbIntegrityTests`, real Postgres):** insert a refresh-token row whose `User` is gone, call `ValidateRefreshTokenAsync` → returns `null` (→ 401). Red today (NRE); green after the guard.

---

### #53 — Week booking is no longer atomic  *(prod-real)*

**Existing** — `BookingService.CreateWeekBookingAsync` (`BookingService.cs:144-234`): the loop does a **per-day** `_db.Bookings.Add` + `SaveWithSlotConflictRetryAsync` (a per-day `SaveChanges`, `:213-215`), then notifies after the loop. There is no enclosing transaction. Phase 17 replaced the old single terminal `SaveChanges` with these per-day saves.

**Why it's a bug.** If day 4 fails unrecoverably (retry exhausted — see #54 — or any non-unique-violation DB error), days 1-3 are **already committed** and the exception escapes before the notification loop (`:231-232`): the user gets a 500 with partial bookings and no confirmations. Previously all-or-nothing.

**Change** — wrap the loop in one transaction; keep the per-day save (for slot-conflict retry attribution) but commit once at the end so any escape rolls the whole week back:
```diff
     var outcomes = new List<(Booking Booking, DirectAssignmentOutcome Outcome)>();
+    await using var tx = await _db.Database.BeginTransactionAsync();
 
     for (int i = 0; i < 5; i++) { ... per-day Add + SaveWithSlotConflictRetryAsync ... }
 
     if (created.Count == 0)
         throw new ValidationException("No bookings could be created ...");
+
+    await tx.CommitAsync();
 
     foreach (var (booking, _) in created) { ... reload nav props ... }
     foreach (var (booking, outcome) in outcomes)
         SendDirectAssignmentNotifications(booking, outcome);   // after commit (see #45)
```
(`Program.cs` uses plain `UseNpgsql` — no retrying execution strategy — so an explicit transaction is safe, same as the #8/#10 fixes.)

**Test (`WeekBookingAtomicityTests`, real Postgres):** interceptor forces day-4's save to throw a non-retryable error → assert **zero** bookings persisted for the week and no notifications sent. Red today (3 committed); green after the transaction.

---

### #54 — Retry-exhausted `DbUpdateException` surfaces as a raw 500  *(prod-real)*

**Existing** — `SaveWithSlotConflictRetryAsync` (`BookingService.cs:91-113`):
```csharp
for (var attempt = 0; ; attempt++)
{
    try { await _db.SaveChangesAsync(); return outcome; }
    catch (DbUpdateException ex) when (
        attempt < 3
        && outcome == DirectAssignmentOutcome.AssignedConfirmed
        && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == "IX_Bookings_ParkingSlotId_Date_TimeSlot")
    { ... re-run assignment ... }
}
```
**Why it's a bug.** On the 4th collision the `when` guard is false, so the `DbUpdateException` propagates. `ExceptionHandlingMiddleware` maps `ValidationException`/`NotFoundException`/`ForbiddenException` but **not** `DbUpdateException` → opaque 500 instead of a friendly "please retry."

**Change** — translate exhausted retries into a domain exception the middleware already maps to a clean 409/400:
```diff
     catch (DbUpdateException ex) when (
         attempt < 3
         && outcome == DirectAssignmentOutcome.AssignedConfirmed
         && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
         && pg.ConstraintName == "IX_Bookings_ParkingSlotId_Date_TimeSlot")
     { ... re-run assignment ... }
+    catch (DbUpdateException ex) when (
+        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
+    {
+        throw new ValidationException(
+            "That parking slot was just taken. Please try booking again.");
+    }
```
**Test:** interceptor raises the slot unique-violation on every attempt → the call throws `ValidationException` (→ 400 ProblemDetails), not a raw `DbUpdateException`. Red today (raw 500); green after mapping. (Verifies with #53's harness.)

---

### #55 — Direct assignment records no `LotteryHistory` → fairness bypass  *(DECISION, not a mechanical fix)*

**Existing** — `DirectAssignmentService.ApplyAsync` (`DirectAssignmentService.cs:81-87`) sets `Status = Confirmed` with **no** history row, unlike `LotteryService`, which writes `LotteryHistory` for winners:
```csharp
booking.ParkingSlotId = slotId;
booking.Status = BookingStatus.Confirmed;
booking.ConfirmedAt = DateTime.UtcNow;
// (no LotteryHistory{Won=true} written)
return DirectAssignmentOutcome.AssignedConfirmed;
```
**Why it matters.** `WeightedHistoryStrategy` penalises frequent lottery winners using `LotteryHistory`. A user who books *after* the 10 PM lottery and grabs a leftover slot gets `Confirmed` parking with **zero** fairness weight — so habitual late-booking quietly games the lottery. Bounded (only otherwise-idle slots) but a real incentive.

**The decision (yours):**
- **Option A — close the loophole:** write `LotteryHistory { UserId, LocationId, Date, TimeSlot, Won = true }` right after `:83`, so direct assignments count toward fairness weighting like lottery wins.
- **Option B — document the exemption:** deliberately treat leftover-slot pickups as un-penalised (they only use slots the lottery didn't allocate) and record that intent in code + docs.

I recommend **Option A** (keeps fairness honest) but it's a product call. **If A:** test asserts a `LotteryHistory{Won=true}` row exists after a direct assignment (red today, green after the write). No test until the choice is made.

---

### #56 — Booking created during a slot's lottery run can stay `Pending`  *(prod-real; sibling of #10)*

**Existing** — `DirectAssignmentService.ApplyAsync` gate (`DirectAssignmentService.cs:26-30`):
```csharp
var lotteryRan = await _db.LotteryRuns.AnyAsync(lr =>
    lr.LocationId == booking.LocationId && lr.Date == booking.Date && lr.TimeSlot == booking.TimeSlot);
if (!lotteryRan)
    return DirectAssignmentOutcome.NotApplicable;   // booking stays Pending; lottery "will handle it"
```
**Why it's a bug.** The gate assumes the lottery either hasn't run (it'll process this Pending booking) or has (direct-assign). But there's a window: a booking POSTed **after** the lottery has read its Pending set but **before** it writes the `LotteryRuns` row sees `lotteryRan == false` → `NotApplicable` → left `Pending`. The lottery is already past its read and is idempotent thereafter, and direct assignment only runs at creation → stuck `Pending` forever. This is #10's race re-introduced on the direct-assignment path.

**How #10's fix partially covers it, and the gap.** Phase 1's #10 post-commit sweep (`LotteryService`) does `UPDATE Bookings SET Status=Lost WHERE loc/date/slot AND Status=Pending` **after** the run commits — so a booking that commits *before* the sweep is caught (→ `Lost`). The residual gap is a booking whose own transaction commits *after* the sweep has already run.

**Change** — close the residual window with a cheap reconciliation rather than more locking (matches the "sweep, don't lock" conclusion from #10):
- Add to the hourly `ConfirmationExpiryJob` (or a small dedicated sweep) a pass that marks any `Pending` booking as `Lost` when a completed `LotteryRuns` row exists for its `(LocationId, Date, TimeSlot)` and the date is imminent/past:
```csharp
// Pending bookings whose slot's lottery has already completed missed the draw.
await _db.Bookings
    .Where(b => b.Status == BookingStatus.Pending
        && _db.LotteryRuns.Any(lr => lr.LocationId == b.LocationId
            && lr.Date == b.Date && lr.TimeSlot == b.TimeSlot))
    .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Lost));
```
This is deterministic regardless of commit ordering (it runs later and sees all committed rows). Optionally route to direct-assignment/waitlist instead of `Lost` if a free slot exists — but `Lost` is the safe baseline and matches #10.

**Test (`LotteryRaceTests`, real Postgres — extend):** insert a `Pending` booking + a completed `LotteryRuns` row for the same slot (simulating the late commit), run the reconciliation → booking becomes `Lost`, never left `Pending`. Red without the pass; green with it.

---

## Suite / infra notes
- Backend integration tests reuse Phase-1 `PostgresFixture` + `[SkippableFact]` (`Skip.IfNot(fx.DockerAvailable, …)`) so they **skip, never fail**, when Docker is absent. See `docs/testing-integration-tests.md`.
- New migrations expected: **#42** (Location FK → Restrict). #50 needs **no** new index (Phase-1 `Email` unique index suffices).
- Run with `DOTNET_ROOT`/PATH pointed at the user-profile dotnet and `HTTP(S)_PROXY` cleared, per the Windows recipe in `docs/testing-integration-tests.md`.
- Convention: mark each row ✅ **Done** here as its test goes green; keep this plan mirrored in `SchulerPark-master/docs/plans/`.

## Progress log
_(fill in as fixes land — Bug | ✅ | Test(s) | Notes)_

### Track A — Booking/lottery correctness & lifetime — ⏮️ **reverted 2026-07-21** (implementation backed out per request)

Track A was implemented (TDD red→green, real Postgres) and then **fully reverted** at the user's request on 2026-07-21: all code, migration, and test changes were undone in `SchulerPark-latest` and the "resolved" banners removed from the report. Bugs #39, #45, #54 remain **open** (see status table). (#42, #53 were re-fixed in the selective batch below; #56 remains open.)

### Selective batch — ✅ **complete 2026-07-22** (user-selected: #21, #48, #49, #23, #24, #44, #53, #42)

TDD red→green. Backend suite **111 passed, 0 failed, 0 skipped** (real Postgres up); frontend **28 passed**; frontend `tsc --noEmit` clean.

| Bug | ✅ | Test(s) | Notes |
|-----|:--:|---------|-------|
| #48 | ✅ | `AuthRateLimitAndCacheTests.Login_is_rate_limited_but_config_is_not` | Removed class-level `[EnableRateLimiting("auth")]`; applied to `register`/`login`/`resend-verification` only. `me`/`config`/`refresh`/`logout` now use the global limiter. |
| #49 | ✅ | `AuthRateLimitAndCacheTests.Active_user_check_is_cached_across_requests` | `OnTokenValidated` result cached in `IMemoryCache` (30s TTL) via `UserActiveCache`. Evicted on disable/enable/delete (`ProfileController`, `UsersAdminController`) so #4's immediate revocation still holds. |
| #44 | ✅ | `ConfirmationExpiryJobTests` (2) | Reminder moved out of the expiry branch to a 1-hour pre-deadline window (`IsReminderDue`); no misleading "please confirm" on expiry. |
| #53 | ✅ | `BookingServiceDbTests.Week_booking_rolls_back_entirely_when_a_day_fails` | `CreateWeekBookingAsync` wrapped in one `BeginTransactionAsync`/`CommitAsync`; notifications only after commit. |
| #42 | ✅ | `DbIntegrityTests.Deleting_location_with_booking_is_blocked` | `Location→Bookings`/`Location→LotteryHistories` FKs `Cascade`→`Restrict`; migration `20260722065743_RestrictLocationDeleteBehavior`. |
| #21 | ✅ | `bookingWindow.test.ts` (3) | Extracted `getBookingWindow` computing min/max from local Y/M/D (no `toISOString`/ms math). |
| #23 | ✅ | `api.test.ts` | Interceptor now delegates to the single-flight `authService.refresh()` (lazy import); one rotation when a direct refresh races it. |
| #24 | ✅ | `booking-full.spec.ts`, `week-booking.spec.ts` (Playwright, CI) | Removed vacuous assertions: assert navigation to `/my-bookings`, assert create success after pre-clean, assert `createdBookings > 0` and exactly 5 days accounted for. |

Not committed/pushed (per standing instruction).
