# SchulerPark — Production Deployment Guide

Stepwise guide for deploying SchulerPark to a Rocky Linux 10 server behind a corporate proxy. Steps are split into **[USER]** (can run yourself) and **[ADMIN]** (needs root; request from server admin).

---

## Environment assumptions

| Item | Value |
|---|---|
| Server OS | Rocky Linux 10 (Red Quartz) |
| Server hostname | `park` |
| Deploy user | `Prache.Maurya` (member of `docker` group, limited sudo for `dnf` only) |
| Corporate proxy | `http://web.schuler.de:3128` |
| Code location | `~/SchulerPark` on server |
| DNS | `SITE_DOMAIN` must resolve to the server's public IP |

Pre-installed on the server: **git**, **docker**, **docker compose**, **python3**.

---

## Role summary

| # | Step | Role | Blocks next step? |
|---|------|------|---|
| 1 | Install supporting packages | ADMIN | No — optional |
| 2 | Configure Docker daemon proxy | ADMIN | **Yes** — needed before build |
| 3 | Open firewall (80/443) | ADMIN | **Yes** — needed before Caddy starts |
| 4 | SELinux relabel bind-mounts | ADMIN | **Yes** — needed before Caddy starts |
| 5 | Clone the repo | USER | Yes |
| 6 | Create `.env` with secrets | USER | Yes |
| 7 | Verify Docker can pull | USER | Yes |
| 8 | Build and start the stack | USER | Yes |
| 9 | Verify and first-login | USER | — |
| 10 | Update later | USER | — |

Steps 1–4 can run in parallel with 5–6. Steps 7–9 require 1–4 done.

---

## [ADMIN] Step 1 — Install supporting packages

```bash
sudo dnf install -y openssl policycoreutils-python-utils
```

`openssl` is optional (user can generate secrets with `python3`). `policycoreutils-python-utils` provides `chcon` used in step 4.

---

## [ADMIN] Step 2 — Configure Docker daemon proxy

Docker ignores shell `http_proxy` env vars. It needs a systemd drop-in. Without this, **all image pulls and builds will fail**.

```bash
sudo mkdir -p /etc/systemd/system/docker.service.d

sudo tee /etc/systemd/system/docker.service.d/http-proxy.conf >/dev/null <<'EOF'
[Service]
Environment="HTTP_PROXY=http://web.schuler.de:3128"
Environment="HTTPS_PROXY=http://web.schuler.de:3128"
Environment="NO_PROXY=localhost,127.0.0.1,park,.local,.schuler.de,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16"
EOF

sudo systemctl daemon-reload
sudo systemctl restart docker
```

Verify:
```bash
docker info | grep -i proxy
# Should list the 3 Environment values
```

---

## [ADMIN] Step 3 — Open firewall ports 80 and 443

Caddy needs both ports for Let's Encrypt ACME challenges and production HTTPS.

```bash
sudo firewall-cmd --permanent --add-service=http
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --reload

sudo firewall-cmd --list-services   # should include http and https
```

---

## [ADMIN] Step 4 — SELinux relabel bind-mounted files

Rocky runs SELinux in enforcing mode. The Compose stack bind-mounts `Caddyfile` and `scripts/db-backup.sh` into containers; without relabeling, the containers get "Permission denied".

Replace `<repo-path>` with the absolute path to the user's cloned repo (user provides via `readlink -f ~/SchulerPark`):

```bash
sudo chcon -Rt container_file_t <repo-path>/Caddyfile <repo-path>/scripts/
```

This must be re-run after any fresh clone or if the files are replaced.

---

## [USER] Step 5 — Clone the repo

Via HTTPS through the proxy (already set in the shell env):

```bash
git config --global http.proxy "http://web.schuler.de:3128"
git config --global https.proxy "http://web.schuler.de:3128"

cd ~
git clone https://github.com/tranmh/SchulerPark.git
cd SchulerPark

# Give admin the absolute path for step 4:
readlink -f ~/SchulerPark
```

---

## [USER] Step 6 — Create `.env` with secrets

```bash
cd ~/SchulerPark
cp .env.production.example .env
chmod 600 .env
```

Generate the two strong secrets (openssl not required):

```bash
python3 -c "import secrets; print('DB_PASSWORD=' + secrets.token_urlsafe(32))"
python3 -c "import secrets; print('JWT_SECRET=' + secrets.token_urlsafe(64))"
```

Generate VAPID keys on your **laptop** (faster, avoids server proxy friction):

```bash
docker run --rm node:20-alpine sh -c "npx -y web-push generate-vapid-keys"
```

Edit `.env` (`nano .env` or `vim .env`) and fill in all required values:

| Variable | Source |
|---|---|
| `DB_PASSWORD` | Python output above |
| `JWT_SECRET` | Python output above |
| `SITE_DOMAIN` | DNS hostname pointing at this server |
| `SMTP_HOST` / `SMTP_PORT` | Your production mail server |
| `SMTP_USERNAME` / `SMTP_PASSWORD` | Mail credentials |
| `SMTP_FROM_ADDRESS` | e.g. `noreply@schulerpark.de` |
| `AZURE_AD_TENANT_ID` | From Azure AD app registration |
| `AZURE_AD_CLIENT_ID` | From Azure AD app registration |
| `AZURE_AD_CLIENT_SECRET` | From Azure AD app registration |
| `BACKUP_RETENTION_DAYS` | `30` |

Azure AD redirect URI must be: `https://<SITE_DOMAIN>/signin-oidc`.

Append VAPID (not in the example file):
```
VAPID_PUBLIC_KEY=<from laptop>
VAPID_PRIVATE_KEY=<from laptop>
VAPID_SUBJECT=mailto:admin@schuler.de
```

---

## [USER] Step 7 — Verify Docker can pull images

Only proceed after admin confirms step 2 is done.

```bash
docker info | grep -i proxy       # expect 3 Environment values
docker pull alpine                  # must succeed
```

If `docker pull` fails, go back to admin — step 2 isn't right.

---

## [USER] Step 8 — Build and start the stack

```bash
cd ~/SchulerPark

docker compose -f docker-compose.yml -f docker-compose.prod.yml build \
  --build-arg http_proxy --build-arg https_proxy --build-arg no_proxy

docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

Build takes several minutes (NuGet restore, frontend build). The `--build-arg` flags forward your shell's proxy env into the build containers so NuGet/npm can reach the internet.

---

## [USER] Step 9 — Verify and first login

```bash
# All four containers should be Up / healthy
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps

# Watch Caddy obtain the certificate
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f caddy
# Look for: "certificate obtained successfully"

# App health via the domain
curl https://<SITE_DOMAIN>/api/health
```

Open `https://<SITE_DOMAIN>` in a browser. Log in with the seeded admin account:

- Email: `admin@schulerpark.local`
- Password: `Admin123!`

**Change this password immediately** via the profile page.

---

## [USER] Step 10 — Updating later

```bash
cd ~/SchulerPark
git pull
docker compose -f docker-compose.yml -f docker-compose.prod.yml build \
  --build-arg http_proxy --build-arg https_proxy --build-arg no_proxy
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

EF Core migrations run automatically at app startup.

If a new clone is done (or SELinux context is lost), re-run step 4.

---

## Admin handoff — paste-ready block

Copy this block into a ticket for the server admin. Fill in `<repo-path>` from `readlink -f ~/SchulerPark` first.

```bash
# 1. Packages
sudo dnf install -y openssl policycoreutils-python-utils

# 2. Docker daemon proxy
sudo mkdir -p /etc/systemd/system/docker.service.d
sudo tee /etc/systemd/system/docker.service.d/http-proxy.conf >/dev/null <<'EOF'
[Service]
Environment="HTTP_PROXY=http://web.schuler.de:3128"
Environment="HTTPS_PROXY=http://web.schuler.de:3128"
Environment="NO_PROXY=localhost,127.0.0.1,park,.local,.schuler.de,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16"
EOF
sudo systemctl daemon-reload
sudo systemctl restart docker

# 3. Firewall
sudo firewall-cmd --permanent --add-service=http
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --reload

# 4. SELinux relabel (use absolute path from `readlink -f`)
sudo chcon -Rt container_file_t <repo-path>/Caddyfile <repo-path>/scripts/
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `docker pull` — "connection refused" | Daemon proxy missing | Admin step 2 |
| Caddy logs: "permission denied" on `/etc/caddy/Caddyfile` | SELinux | Admin step 4 |
| Caddy can't obtain cert | Ports 80/443 blocked or DNS wrong | Admin step 3; check `dig +short <SITE_DOMAIN>` |
| App container restart loop | DB connection issue | Check `docker compose logs app`; confirm `DB_PASSWORD` hasn't changed between runs (old volume) |
| `npm`/`nuget` failures during build | Build containers can't see proxy | Ensure `--build-arg http_proxy --build-arg https_proxy` on the `build` command |
| Browser shows Cloudflare redirect loop | Cloudflare SSL set to Flexible | Set to **Full (strict)** in Cloudflare |
