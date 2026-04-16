# Phase 14: CI/CD Auto-Deploy via SSH

## Overview

Auto-deploy to production on every push to master, after all CI jobs (backend, frontend, E2E) pass. Uses SSH to connect to the server and run `docker compose` to rebuild.

## Pipeline Flow

```
Push to master
    |
    v
[Backend Build & Test] ──┐
[Frontend Build & Test] ──┼── all pass ──> [Deploy to Production]
[E2E Tests (Playwright)] ─┘                       |
                                            SSH into server
                                            git pull
                                            docker compose up --build
                                            health check
```

- PRs: CI only (no deploy)
- Push to master: CI + deploy
- Any CI failure: deploy skipped

## GitHub Secrets Required

Set these in GitHub repo Settings > Secrets and variables > Actions:

| Secret | Example | Description |
|--------|---------|-------------|
| `DEPLOY_HOST` | `203.0.113.10` | Server IP or hostname |
| `DEPLOY_USER` | `deploy` | SSH username |
| `DEPLOY_KEY` | `-----BEGIN OPENSSH...` | SSH private key (full content) |
| `DEPLOY_PATH` | `/opt/schulerpark` | Absolute path to repo on server |

## Server Setup (one-time)

```bash
# 1. Install Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER

# 2. Clone repo
sudo mkdir -p /opt/schulerpark
sudo chown $USER:$USER /opt/schulerpark
git clone git@github.com:tranmh/SchulerPark.git /opt/schulerpark

# 3. Create production .env
cd /opt/schulerpark
cp .env.production.example .env
# Edit .env with real values (DB_PASSWORD, JWT_SECRET, SITE_DOMAIN, SMTP, VAPID keys, etc.)

# 4. Create deploy SSH key pair
ssh-keygen -t ed25519 -f ~/.ssh/deploy_key -N ""
cat ~/.ssh/deploy_key.pub >> ~/.ssh/authorized_keys

# 5. Copy the PRIVATE key content to GitHub secret DEPLOY_KEY:
cat ~/.ssh/deploy_key

# 6. First manual deploy to verify
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build

# 7. Verify
curl https://your-domain.com/api/health
```

## GitHub Environment (optional)

Create a `production` environment in GitHub repo Settings > Environments to:
- Add required reviewers (manual approval gate)
- Add deployment branch rules (only master)
- View deployment history
