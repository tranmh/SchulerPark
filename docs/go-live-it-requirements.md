# Go-Live: IT Requirements for All-Employees Rollout

What Schuler IT (and adjacent teams) need to deliver before opening LouisE to all employees. Code-side everything is wired; the gaps below are configuration, network access, and policy.

Server facts: host `park.schuler.de` @ `193.28.217.49`, Rocky Linux 10.1, project path `/home/Prache.Maurya/SchulerPark`.

## 1. Networking / DNS

| Item | What IT does | Why |
|---|---|---|
| Internal DNS A record | `park.schuler.de` → `193.28.217.49` on the corporate resolver | Employees on the LAN/VPN must resolve it. Today the app owner reaches it via PowerShell SSH tunnel, suggesting DNS isn't widely usable. |
| Proxy bypass | Add `park.schuler.de` and `193.28.217.49` to the McAfee proxy exclusion list / corporate PAC file | Without this, `https://park.schuler.de/` from employee laptops returns a 502 from `proxy02.schuler.de` — looks like an outage but is just the corporate proxy intercepting the request. |
| Firewall | Allow inbound 443/tcp (and 80/tcp for redirect) from employee subnets to `193.28.217.49` | Host-level `firewalld` is already open (see `docs/deploy-this-server.md` §3); corporate edge/segmentation ACLs are separate. |
| Outbound from the host | Allow egress to `login.microsoftonline.com:443` and (if push notifications are enabled) `*.push.services.mozilla.com` + `fcm.googleapis.com` | Backend must reach Microsoft's JWKS endpoint to validate Azure AD id_tokens; without this SSO fails silently. |

## 2. TLS / Certificate

Currently `Caddyfile` uses `tls internal` (self-signed). Employees will see a browser cert warning on every visit — blocker for go-live.

Two paths:

- **Internal corporate CA cert (recommended for intranet).** IT issues a cert for `park.schuler.de` from the corporate CA. Drop `cert.pem` + `key.pem` into a bind-mounted folder, swap `tls internal` for `tls /path/to/cert /path/to/key` in `Caddyfile`, recreate Caddy.
- **Public Let's Encrypt.** Only if `park.schuler.de` is also resolvable from the internet and inbound 80/443 is open from Let's Encrypt's IPs. Not typical for an intranet app.

After the cert is real, also restore HSTS in `Caddyfile` (currently `max-age=0` because self-signed certs and HSTS are incompatible).

## 3. Identity / Azure AD (Entra ID)

Deliverables IT needs to hand back:

- App Registration in the Schuler tenant, **platform = Single-page application** (not "Web")
- Redirect URI: `https://park.schuler.de/` (and `https://localhost/` if anyone keeps using the SSH tunnel)
- Tenant ID + Client ID (Client Secret is unused by this flow — only Tenant+Client are checked by `AzureAdSettings.IsConfigured`)
- Admin consent for `User.Read` if the tenant requires it
- Optional: Conditional Access policy scoping who can use the app, MFA enforcement, device-compliance requirements

Setup steps inside the app once values are issued: put them in `.env` as `AZURE_AD_TENANT_ID` / `AZURE_AD_CLIENT_ID`, then `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d` to recreate the app container.

## 4. Mail / SMTP

The app sends booking confirmations, lottery results, and expiry warnings via MailKit. Prod compose reads `SMTP_HOST` / `SMTP_PORT` / `SMTP_USERNAME` / `SMTP_PASSWORD` / `SMTP_FROM_ADDRESS`.

IT needs to provide:

- SMTP relay host + port (likely the corporate Exchange/365 relay or a dedicated app relay)
- Credentials, or have the relay accept `193.28.217.49` by IP allowlist
- Authorization for the `From:` address. `.env.production.example` defaults to `noreply@schulerpark.de`; the real domain should likely be `noreply@schuler.de`. SPF/DMARC for that sender must allow the relay.

Without this, email sending fails silently (fire-and-forget). Confirmations don't reach users → broken UX even if the app is up.

## 5. Server / Hosting (Linux ops)

For a sanctioned go-live, IT needs to confirm:

- **Patching policy** — who applies kernel/Docker CVEs and on what cadence.
- **Backup destination** — current setup writes nightly dumps to `/dbbackup` on the host. That bind mount must be on storage that's part of corporate backup, or needs an offsite copy (rsync to a NAS, scheduled).
- **Backup retention** — `BACKUP_RETENTION_DAYS=30` in `.env.production.example`. Confirm 30 days is sufficient for the data-protection officer.
- **Monitoring/alerting** — none today. Minimum: a check that `https://park.schuler.de/api/health` returns 200 from corporate monitoring (Zabbix/Nagios/PRTG), alerting whoever is on-call.
- **Log shipping** — container logs go to Docker's json-file driver only. If SIEM ingestion is required, wire `docker logs` into syslog / Loki / Splunk.
- **Sizing / HA** — single-host today. If "all employees" means >100 concurrent at lottery hours (10 PM Europe/Berlin), validate CPU/RAM sizing. The app supports `--scale app=3` but real HA requires a second host.

## 6. Security / Compliance

- **DSGVO sign-off** — the app implements `/api/profile/data-export` and `DELETE /api/profile/data`. Legal/DPO needs to validate the processing description and add the app to the *Verzeichnis von Verarbeitungstätigkeiten*.
- **Betriebsrat (works council)** — booking and lottery data is employee-tracking. In a German Schuler entity a *Betriebsvereinbarung* is almost always required before any system that logs employee activity is opened to all staff.
- **Penetration test / security review** — corporate InfoSec policy typically requires one before exposing an authenticated web app to all employees.
- **Secrets handling** — `.env` on the host contains DB password and JWT secret in plaintext. Confirm with InfoSec whether that's acceptable or whether they want Vault / Key Vault integration.

## 7. Owner tasks (not IT)

These don't need IT but must be done before go-live:

- Replace the seeded bootstrap admin (`admin@schulerpark.local` / `Admin123!`) — create a real admin, disable/delete the seed.
- Generate and set `VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY` in `.env` so push notifications work.
- Seed the six Schuler sites (Goeppingen, Erfurt, Hessdorf, Gemmingen, Weingarten, Netphen) with real slot capacity numbers via the admin UI.
- Configure GitHub Actions secrets for auto-deploy: `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_KEY`, `DEPLOY_PATH`.

## Suggested order of operations

1. Cert + DNS + proxy bypass — without these no one can load the page.
2. SMTP — without this users don't get confirmations.
3. AAD app registration — without this only local login works (functional, but not the chosen UX).
4. Monitoring + backup verification — for the go-live ticket.
5. Legal / Betriebsrat sign-off — runs in parallel with 1–4; usually the long pole.
