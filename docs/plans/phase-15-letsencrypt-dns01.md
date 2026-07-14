# Phase 15: Let's Encrypt TLS via DNS-01 (intranet-safe)

> **⚠️ SUPERSEDED (2026-07-08). DNS-01 is not viable for this zone.**
> A lookup showed `schuler.de` runs on **Cloudflare *secondary* DNS** (read-only
> replica; hidden master `ns1.schuler-isp.net`). You cannot write the
> `_acme-challenge` TXT record through the Cloudflare API on a secondary zone, so
> the `caddy-dns/cloudflare` + `CF_API_TOKEN` design below **cannot work**.
>
> **Chosen path instead:** stock Caddy auto-issues via the **inbound HTTP-01 /
> TLS-ALPN-01** challenge. This needs Schuler IT to open **inbound :80/:443** to
> `193.28.217.49` (the box becomes internet-reachable). No DNS token, no DNS
> plugin, no `Dockerfile.caddy`. Implemented in the `Caddyfile` (split public
> Let's-Encrypt block vs internal-CA localhost/IP block) and `docker-compose.prod.yml`
> (ACME env + corporate-proxy egress). The DNS topology + rejected alternatives are
> recorded in the `project-dns-topology` memory. The rest of this document is kept
> for historical context only.

## Context

The production deployment (Phase 12) was originally specced for Let's Encrypt
auto-HTTPS, but the live box (`193.28.217.49`, domain `park.schuler.de`) sits on
the Schuler **intranet**, so it was switched to Caddy's self-signed internal CA
(`tls internal`) as a stopgap — browsers show a cert warning.

This phase restores **publicly-trusted Let's Encrypt** certs **without** exposing
the box to the internet, using the **DNS-01** ACME challenge. Caddy proves it owns
`park.schuler.de` by writing a `_acme-challenge` TXT record through the DNS
provider's API — no inbound `:80`/`:443` from the internet is required, so the box
stays fully intranet.

## What changed

| File | Change |
|------|--------|
| `Caddyfile` | `park.schuler.de` now uses `tls { dns cloudflare {env.CF_API_TOKEN} }`; full-strength HSTS restored. `localhost`/`127.0.0.1`/`193.28.217.49` kept on `tls internal` (for `deploy.sh`'s `curl -k https://localhost/api/health` probe and transitional direct-IP access). |
| `Dockerfile.caddy` (new) | Builds Caddy via `xcaddy` with the `caddy-dns/cloudflare` plugin baked in (stock `caddy:2-alpine` has no DNS plugins). Provider is overridable via the `CADDY_DNS_MODULE` build arg. |
| `docker-compose.prod.yml` | `caddy` service now `build`s `Dockerfile.caddy`; passes `ACME_EMAIL`, `CF_API_TOKEN`; proxy vars are parameterized (`CADDY_HTTP(S)_PROXY`) with `NO_PROXY` covering internal hops. |
| `.env.production.example`, `.env.example` | New vars: `ACME_EMAIL`, `CF_API_TOKEN`, `CADDY_HTTP_PROXY`, `CADDY_HTTPS_PROXY`; `SITE_DOMAIN` defaults to `park.schuler.de`. |

SSO/Azure AD is intentionally left **off** — it activates only when the
`AZURE_AD_*` vars are set, and the login UI hides the Microsoft button otherwise.
The prod env template leaves them empty (local-auth-only).

## ⚠️ Remaining manual prerequisite — REQUIRED before this works

Let's Encrypt cannot issue a cert until Caddy can write a DNS TXT record. You must
obtain, from whoever administers the **schuler.de DNS zone**:

1. **A DNS provider API token** with `DNS:Edit` rights on the zone → set as
   `CF_API_TOKEN` in `.env`.
2. **Confirm the provider.** The default assumes **Cloudflare**. If `schuler.de`
   is hosted elsewhere (Azure DNS, Route53, in-house, …), change **both**:
   - `CADDY_DNS_MODULE` build arg in `Dockerfile.caddy` (e.g.
     `github.com/caddy-dns/azure`), and
   - the `dns <provider>` directive in the `Caddyfile`.
   Provider list: <https://github.com/caddy-dns>
3. **Outbound access.** Caddy must reach the ACME API + the DNS API. If the box
   has no direct outbound internet, set `CADDY_HTTPS_PROXY` to the McAfee proxy URL.

> If you cannot get a zone API token, DNS-01 is not possible. The fallback is to
> have Schuler IT issue an **internal-CA cert** for `park.schuler.de` (already
> trusted on employee machines) and use `tls /path/cert /path/key` — no ACME.

## Deploy steps (once the token is in place)

```bash
# 1. Fill in .env: SITE_DOMAIN, ACME_EMAIL, CF_API_TOKEN (+ proxy if needed)

# 2. Build the custom Caddy image (deploy.sh only builds `app`, not caddy)
docker compose -f docker-compose.yml -f docker-compose.prod.yml build caddy

# 3. Recreate Caddy. NOTE: the Caddyfile is a single-file bind mount, so a plain
#    `caddy reload` does NOT pick up edits — force-recreate the container.
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --force-recreate caddy

# 4. Watch it obtain the cert
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs -f caddy
# look for: "certificate obtained successfully" for park.schuler.de

# 5. Verify (publicly-trusted now — no -k needed for the real domain)
curl -I https://park.schuler.de/api/health
```

## Verification checklist

- [ ] `docker compose ... build caddy` succeeds (xcaddy pulls the DNS plugin).
- [ ] Caddy logs show a cert obtained for `park.schuler.de` (not the internal CA).
- [ ] `https://park.schuler.de` loads with **no** browser warning.
- [ ] `curl -k https://localhost/api/health` still returns 200 (deploy.sh probe).
- [ ] HSTS header present on `park.schuler.de`, absent on the internal-CA hosts.
