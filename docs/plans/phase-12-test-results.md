# Phase 12: Deployment Test Results

**Date:** 2026-04-16

---

## Dev Mode - ALL PASSED

| Check | Result |
|-------|--------|
| `docker compose up --build` | 3 containers: db, mailhog, app |
| Health check (`/api/health`) | `{"status":"healthy"}` |
| Swagger (`/swagger`) | 200 OK |
| MailHog (`:8025`) | 200 OK |
| PWA manifest | Valid JSON with icons |
| Service worker (`/sw.js`) | 200 OK |
| Login (`admin@schulerpark.local`) | JWT returned |

---

## Prod Mode - ALL PASSED

| Check | Result |
|-------|--------|
| `docker compose -f ... -f docker-compose.prod.yml up --build` | 4 containers: db, app, caddy, db-backup |
| HTTPS health check (`curl -sk https://localhost/api/health`) | `{"status":"healthy"}` |
| Security headers | HSTS, X-Frame-Options: DENY, X-Content-Type-Options: nosniff, Referrer-Policy, Permissions-Policy |
| Swagger NOT accessible in prod | 404 (correct) |
| PWA manifest via Caddy | Valid |
| DB backup | `schulerpark_20260416_065019.sql.gz` (13.1K) |

---

## Bug Found & Fixed

**Hangfire `RecurringJob.AddOrUpdate` static API** crashed in Production because `JobStorage.Current` wasn't initialized. In dev mode, `MapHangfireDashboard` was triggering Hangfire storage initialization as a side effect — but in Production the dashboard is disabled, so the static API had no storage to use.

**Fix:** Switched from static `RecurringJob.AddOrUpdate` to DI-based `IRecurringJobManager` in `Program.cs`.

---

## Docker Compose Architecture (final)

```
docker-compose.yml          <- base (db only)
docker-compose.override.yml <- dev (auto-loaded): app + mailhog + db ports
docker-compose.prod.yml     <- prod (explicit):   app + caddy + db-backup
```

### Commands

```bash
# Dev (unchanged from before)
docker compose up --build

# Production
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build

# Production scaled (3 app instances behind Caddy)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build --scale app=3
```

---

## Security Headers Verified

```
strict-transport-security: max-age=31536000; includeSubDomains; preload
x-content-type-options: nosniff
x-frame-options: DENY
referrer-policy: strict-origin-when-cross-origin
permissions-policy: camera=(), microphone=(), geolocation=()
```
