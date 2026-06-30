# SchulerPark — Deployment on park.schuler.de

Server-specific deployment guide for this host.

## Server facts
- Hostname: `park.schuler.de`
- Public IP: `193.28.217.49`
- OS: Rocky Linux 10.1
- Docker: 29.4.0
- Docker Compose: v5.1.3
- Project path: `/home/Prache.Maurya/SchulerPark`

## Steps

### 1. Edit `/home/Prache.Maurya/SchulerPark/.env`
`DB_PASSWORD` and `JWT_SECRET` are already filled in with strong values. Fill in the remaining placeholders:

```
SITE_DOMAIN=park.schuler.de
SMTP_HOST=<real-smtp-host>
SMTP_PORT=587
SMTP_USERNAME=<smtp-user>
SMTP_PASSWORD=<smtp-password>
SMTP_FROM_ADDRESS=noreply@schuler.de
AZURE_AD_TENANT_ID=<tenant-id>      # optional if only local auth
AZURE_AD_CLIENT_ID=<client-id>      # optional
AZURE_AD_CLIENT_SECRET=<secret>     # optional
```

### 2. Confirm DNS
This is an **intranet** deployment: `park.schuler.de` must resolve on the corporate
resolver, not the public internet. `dig` is not installed on this server — use `getent`:

```bash
getent ahosts park.schuler.de
# Expected: 193.28.217.49 STREAM park.schuler.de
```

Optional — install `dig`:

```bash
sudo dnf install -y bind-utils
```

Note: `/etc/hosts` maps `park.schuler.de` to `::1` (IPv6 localhost). This is a local-only
override. If testing from this box, use `curl --ipv4 ...` to avoid hitting IPv6 localhost.
Getting the internal A record published on the corporate resolver is an IT task — see
`docs/go-live-it-requirements.md` §1.

### 3. Open ports 80 and 443 (firewalld)

```bash
sudo firewall-cmd --permanent --add-service=http
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --reload
```

### 4. Launch

```bash
cd /home/Prache.Maurya/SchulerPark
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

### 5. Verify

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps
# -k: Caddy serves a self-signed cert (`tls internal`), so the cert won't validate yet.
# --noproxy '*': a plain curl from this host is intercepted by the McAfee corporate proxy
#                and returns a misleading 502 (see docs/restart-services.md §7).
curl -k --noproxy '*' --ipv4 https://park.schuler.de/api/health
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f app
```

> **TLS is self-signed today.** `Caddyfile` uses `tls internal` (Caddy's built-in CA),
> so every browser shows a cert warning. Replacing it with a real internal-CA cert is an
> IT deliverable — see `docs/go-live-it-requirements.md` §2. HSTS is set to `max-age=0`
> until then, because HSTS and a self-signed cert are incompatible.

### 6. First-time admin login
- Email: `admin@schulerpark.local`
- Password: `Admin123!`

Change the password immediately after first login.

## Local prod smoke test (optional, before real DNS/TLS)

```bash
SITE_DOMAIN=localhost docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build
curl -k https://localhost/api/health
```

## Maintenance

```bash
# Logs
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f app
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f caddy

# Manual DB backup
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db-backup /usr/local/bin/db-backup.sh

# Restore from backup (DB + role are both `louise` since the 2026-05-26 rename)
gunzip -c backup_file.sql.gz | docker compose -f docker-compose.yml -f docker-compose.prod.yml exec -T db psql -U louise louise

# Caddy reload after Caddyfile changes.
# NOTE: the Caddyfile is a single-file bind mount. Editing it in place breaks the mount
# (the editor writes a new inode), so `caddy reload` re-reads STALE content. Force-recreate
# the caddy container instead so the new file is re-mounted:
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --force-recreate caddy

# Scale app to 3 instances
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --scale app=3
```
