# Phase 12: Deployment & Testing Guide

## Prerequisites
- Docker & Docker Compose installed
- Ports 8080 (dev), 80/443 (prod) available
- For production: domain with DNS A record pointing to server IP

---

## 1. Dev Mode (local development)

```bash
# Start (auto-loads docker-compose.override.yml)
docker compose up --build

# Stop
docker compose down
```

**Services:**
| Service | URL |
|---------|-----|
| App | http://localhost:8080 |
| Health check | http://localhost:8080/api/health |
| Swagger | http://localhost:8080/swagger |
| MailHog UI | http://localhost:8025 |
| PostgreSQL | localhost:5432 |
| Hangfire | http://localhost:8080/hangfire |

**Default login:** `admin@schulerpark.local` / `Admin123!`

**PWA verification:**
- Open DevTools → Application → Manifest → should show SchulerPark manifest
- Application → Service Workers → should show `sw.js` registered

---

## 2. Prod Mode (local test with self-signed cert)

```bash
# Stop dev first
docker compose down

# Start prod profile (Caddy uses self-signed cert for localhost)
SITE_DOMAIN=localhost docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build

# Stop
docker compose -f docker-compose.yml -f docker-compose.prod.yml down
```

**Verify:**
```bash
# Health check (-k to accept self-signed cert)
curl -k https://localhost/api/health

# Security headers
curl -kI https://localhost
# Expected: Strict-Transport-Security, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy

# DB backup (manual trigger)
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db-backup /usr/local/bin/db-backup.sh

# Verify backup file
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db-backup ls -la /backups/
```

---

## 3. Load Balancing Test

```bash
# Start 3 app instances behind Caddy
SITE_DOMAIN=localhost docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build --scale app=3

# Verify all respond
for i in {1..6}; do curl -sk https://localhost/api/health; echo; done
```

---

## 4. Production Deployment (real server)

```bash
# 1. Clone repo
git clone <repo-url> && cd SchulerPark

# 2. Create .env from production template
cp .env.production.example .env

# 3. Edit .env with real values
#   DB_PASSWORD=<strong-random-password>
#   JWT_SECRET=<64-char-random-string>
#   SITE_DOMAIN=schulerpark.your-domain.com
#   SMTP_HOST=smtp.your-server.com
#   SMTP_PORT=587
#   SMTP_USERNAME=<smtp-user>
#   SMTP_PASSWORD=<smtp-password>
#   SMTP_FROM_ADDRESS=noreply@schulerpark.de
#   AZURE_AD_TENANT_ID=<tenant-id>     (optional)
#   AZURE_AD_CLIENT_ID=<client-id>     (optional)
#   AZURE_AD_CLIENT_SECRET=<secret>    (optional)

# 4. Ensure DNS resolves: schulerpark.your-domain.com → server IP

# 5. Launch production
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build

# 6. Verify
curl https://schulerpark.your-domain.com/api/health

# 7. Scale if needed
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --scale app=3
```

Caddy automatically obtains and renews Let's Encrypt certificates.

---

## 5. Maintenance Commands

```bash
# View logs
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f app
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f caddy
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f db-backup

# Manual DB backup
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db-backup /usr/local/bin/db-backup.sh

# List backups
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db-backup ls -lh /backups/

# Restore from backup
gunzip -c backup_file.sql.gz | docker compose -f docker-compose.yml -f docker-compose.prod.yml exec -T db psql -U schulerpark schulerpark

# Rebuild and redeploy (zero-downtime with scaling)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build --scale app=3

# Caddy reload (after Caddyfile changes)
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec caddy caddy reload --config /etc/caddy/Caddyfile
```

---

## 6. Cloudflare Setup (if using)

- Set SSL/TLS mode to **Full (strict)** — Caddy has a real Let's Encrypt cert
- Or set to **Full** — the HTTP block in Caddyfile prevents redirect loops
- **Never** use Flexible mode with a real cert (double redirect)

---

## Architecture

```
                    Internet
                       │
                  ┌────┴────┐
                  │  Caddy   │  :80 / :443 (auto-TLS)
                  │  reverse  │  security headers
                  │  proxy    │  load balancing
                  └────┬────┘
                       │ round-robin
            ┌──────────┼──────────┐
            │          │          │
        ┌───┴───┐ ┌───┴───┐ ┌───┴───┐
        │ app:1 │ │ app:2 │ │ app:3 │  :8080 (internal)
        └───┬───┘ └───┬───┘ └───┬───┘
            │          │          │
            └──────────┼──────────┘
                  ┌────┴────┐
                  │ postgres │  :5432 (internal)
                  └────┬────┘
                       │
                  ┌────┴─────┐
                  │ db-backup │  cron daily 03:00 UTC
                  └──────────┘
```
