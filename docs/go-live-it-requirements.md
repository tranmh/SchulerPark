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

**Status: relay reachable and accepting (verified 2026-09-21).** The app is
configured for the Schuler mail gateway `mgate01.schulergroup.com:25`
(unauthenticated relay) as `noreply@schuler.de` — in `.env` and defaulted in
`.env.production.example` / `docker-compose.prod.yml`. The outbound REJECT rule
found on 2026-09-10 has been lifted:

- Host and app-container network → `mgate01`/`mgate02` port 25: connects, `220`
  banner, `EHLO` accepted (8BITMIME, SIZE 25 MB, STARTTLS offered).
- Relay allowlist: `MAIL FROM:<noreply@schuler.de>` and `RCPT TO:` an external
  (`andritz.com`) mailbox both answered `250` — the box (`193.28.217.49`) is
  accepted without auth.
- One end-to-end test message was queued by the gateway without refusal.
- Ports 587/465 remain refused; irrelevant, the app uses 25.

The prod app container already runs with `Smtp__Host=mgate01.schulergroup.com`,
`Smtp__Port=25` — no config change or restart needed.

Still open:

- Confirm SPF/DMARC for `noreply@schuler.de` covers mail relayed via `mgate01`
  (matters for delivery to external mailboxes; internal delivery works regardless).
  Check whether the 2026-09-21 test mail landed in the inbox or in spam.
- Trigger a real app email (e.g. a booking confirmation) and check
  `docker logs schulerpark-app-1` for "Email sent" — as of 2026-09-21 the app has
  not attempted a send since the firewall was opened.

Retest command: `python3 -c "import smtplib; smtplib.SMTP('mgate01.schulergroup.com',25,timeout=10).noop()"`.

If the relay regresses, email sending fails silently (fire-and-forget). Confirmations don't reach users → broken UX even if the app is up.

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
- Register the self-hosted GitHub Actions runner on this box for auto-deploy (needs a
  registration token from a repo *admin*; `prache19` only has write). Steps in
  `docs/deploy-this-server.md`. The old `DEPLOY_*` SSH secrets are obsolete — inbound
  SSH to the box is firewalled.

## Suggested order of operations

1. Cert + DNS + proxy bypass — without these no one can load the page.
2. SMTP — without this users don't get confirmations. *(Resolved 2026-09-21 — outbound TCP 25 open, mgate01 accepts the box, see §4; only the SPF/DMARC confirmation and a first real app send remain.)*
3. AAD app registration — without this only local login works (functional, but not the chosen UX).
4. Monitoring + backup verification — for the go-live ticket.
5. Legal / Betriebsrat sign-off — runs in parallel with 1–4; usually the long pole.
