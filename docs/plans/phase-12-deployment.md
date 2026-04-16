# Phase 12: Production Deployment with Caddy, DB Backup & PWA

## Context

SchulerPark currently runs as a dev-only Docker Compose stack (app on port 8080, PostgreSQL, MailHog) with no TLS, no reverse proxy, no backups, and no PWA support. This phase adds production-readiness: Caddy for automatic HTTPS via Let's Encrypt, PostgreSQL scheduled backups, load-balancing support, and Progressive Web App capability. The swiss-manager project serves as the template for Caddyfile, db-backup, and Docker Compose profile patterns.

---

## Step 1: Create Caddyfile

**New file:** `Caddyfile`

Adapted from swiss-manager's Caddyfile — uses `{$SITE_DOMAIN}` env var (set in `.env`) so the domain is configurable without editing the file. Reverse proxies to `app:8080`. Security headers match swiss-manager exactly. HTTP block prevents redirect loop with Cloudflare Flexible SSL.

**Load balancing:** When running `--scale app=N`, Docker DNS returns multiple A records for `app`. Caddy's `reverse_proxy` round-robins across them automatically — no extra config needed.

---

## Step 2: Create DB Backup Script

**New file:** `scripts/db-backup.sh`

Adapted from swiss-manager's `scripts/db-backup.sh` — changes `openpairing2` prefix to `schulerpark`. Uses `PGHOST/PGUSER/PGPASSWORD/PGDATABASE` env vars that `pg_dump` reads automatically. Retention cleanup via `find -mtime`. Default 30-day retention.

---

## Step 3: Restructure Docker Compose (profile-based dev/prod split)

Follow swiss-manager's pattern exactly: prod services get `profiles: [prod]`, dev uses `docker-compose.override.yml` (auto-loaded by Docker Compose).

### `docker-compose.yml` — base + prod services

| Service | Profile | Purpose |
|---------|---------|---------|
| `db` | _(none)_ | PostgreSQL 16 — shared by dev and prod |
| `app` | `prod` | App container, Production env, no host port (Caddy fronts it) |
| `caddy` | `prod` | Caddy 2 alpine, ports 80/443, mounts Caddyfile |
| `db-backup` | `prod` | pg_dump cron daily 03:00 UTC, 30-day retention |

Volumes: `postgres_data`, `db_backups`, `caddy_data`, `caddy_config`

Key: prod `app` has **no `container_name`** to allow `--scale app=N`.

### `docker-compose.override.yml` — dev (auto-loaded)

Defines `app` (no profile — overrides the prod definition), `mailhog`, and `db` port mapping. This preserves the exact current dev workflow: `docker compose up` starts db + mailhog + app on port 8080 with Development env.

### Usage

```bash
# Dev (unchanged from today)
docker compose up --build

# Production
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build

# Production scaled (3 app instances behind Caddy)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build --scale app=3
```

---

## Step 4: Backend Production Hardening

### 4a. `backend/SchulerPark.Api/appsettings.Production.json` (new)

Reduce log verbosity to Warning level for production.

### 4b. `backend/SchulerPark.Api/Program.cs` (modify)

Two changes:

1. **Add `UseForwardedHeaders`** before `UseStaticFiles()` — Caddy sets `X-Forwarded-For` and `X-Forwarded-Proto`; ASP.NET needs to trust these for correct URL generation and HTTPS detection.

2. **Move `MigrateAsync()` outside the `IsDevelopment()` guard** — run migrations in all environments on startup. Keep `SeedData` dev-only. EF Core migrations are idempotent and use DB-level locking, safe for concurrent startup with `--scale app=N`.

---

## Step 5: PWA Support

### 5a. Install `vite-plugin-pwa`

### 5b. `frontend/vite.config.ts` (modify)

Add `VitePWA` plugin with:
- `registerType: 'autoUpdate'` — auto-update service worker (no prompt, appropriate for internal tool)
- Manifest: name "SchulerPark", `theme_color: '#111827'` (matches sidebar), `display: 'standalone'`
- Workbox: precache static assets; runtime cache `/api/locations` with NetworkFirst (1h); all other `/api/` routes NetworkOnly (booking data must never be stale)

### 5c. `frontend/index.html` (modify)

- Replace `vite.svg` favicon with `favicon.ico`
- Add `<link rel="apple-touch-icon">`
- Add `<meta name="theme-color" content="#111827">`
- Add `<meta name="description">`

### 5d. PWA Icons

Create placeholder icons in `frontend/public/`:
- `pwa-192x192.png`, `pwa-512x512.png`, `apple-touch-icon-180x180.png`, `favicon.ico`

---

## Step 6: Environment & Config Updates

### `.env.example` (modify)
Add `SITE_DOMAIN` and `BACKUP_RETENTION_DAYS` variables.

### `.env.production.example` (new)
Production-specific template with placeholder values for all required secrets.

### `.dockerignore` (modify)
Add `Caddyfile`, `scripts/`, `docs/`, `*.md` — not needed in Docker build context.

---

## Files Summary

| Action | File |
|--------|------|
| NEW | `Caddyfile` |
| NEW | `scripts/db-backup.sh` |
| NEW | `docker-compose.override.yml` |
| NEW | `backend/SchulerPark.Api/appsettings.Production.json` |
| NEW | `.env.production.example` |
| NEW | `frontend/public/pwa-*.png`, `favicon.ico` |
| MODIFY | `docker-compose.yml` |
| MODIFY | `backend/SchulerPark.Api/Program.cs` |
| MODIFY | `frontend/vite.config.ts` |
| MODIFY | `frontend/index.html` |
| MODIFY | `frontend/package.json` (via npm install) |
| MODIFY | `.env.example` |
| MODIFY | `.dockerignore` |
| MODIFY | `CLAUDE.md` (update deploy section) |

---

## Verification

1. **Dev workflow preserved:** `docker compose up --build` → app on http://localhost:8080, MailHog on :8025, Swagger on /swagger
2. **Prod services start:** `docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build` → caddy, app, db, db-backup all healthy
3. **Backend builds:** `cd backend && dotnet build` — no errors after Program.cs changes
4. **PWA manifest:** After `cd frontend && npm run build`, verify `dist/manifest.webmanifest` and `dist/sw.js` exist
5. **Health check:** `curl http://localhost:8080/api/health` returns 200
6. **DB backup:** `docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db-backup /usr/local/bin/db-backup.sh` creates a `.sql.gz` in `/backups/`
