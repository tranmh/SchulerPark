# Manual Service Restart Runbook

How to restart the SchulerPark stack on `park.schuler.de` after a `git pull`, config change, or general "something is wrong, bounce it" situation.

All commands run from `/home/Prache.Maurya/SchulerPark`.

The prod stack is launched with two compose files. Define this alias once per shell to keep the commands short:

```bash
alias dcp='docker compose -f docker-compose.yml -f docker-compose.prod.yml'
```

The rest of this doc uses `dcp`.

## 1. Decide which restart you need

| Situation | Command |
|---|---|
| Code change pulled (backend or frontend) | **Rebuild** — section 2 |
| `Caddyfile` edited | **Reload caddy** — section 3 |
| `.env` or compose file edited | **Recreate** — section 4 |
| App misbehaving, no code/config change | **In-place restart** — section 5 |
| Full stack bounce (last resort) | **Down + up** — section 6 |

When in doubt, section 2 (rebuild) is safe and covers most cases.

## 2. Rebuild after `git pull`

The frontend is baked into the `app` image at build time, so any backend or frontend change requires `--build`. Caddy and the database stay up untouched.

```bash
git pull
dcp up -d --build
```

Expected output ends with:
```
Container schulerpark-app-1 Recreated
Container schulerpark-app-1 Started
```

App downtime is roughly the time it takes the new container to start (~10s). Caddy will return 502 during that window.

## 3. Reload Caddy after `Caddyfile` changes

`Caddyfile` is bind-mounted, so a reload is *normally* enough — no rebuild, no container recreate, zero downtime.

```bash
dcp exec caddy caddy validate --config /etc/caddy/Caddyfile   # check syntax first
dcp exec caddy caddy reload --config /etc/caddy/Caddyfile
```

If the Caddyfile has a syntax error, the reload (or validate) fails and the old config stays active.

> **Gotcha — single-file bind mount.** The `Caddyfile` is mounted as a single file, not a
> directory. Most editors save by writing a new file and renaming over the old one, which
> changes the inode and **silently breaks the mount** — the container keeps seeing the old
> content, so `caddy reload` re-reads stale config and your change appears to do nothing.
> If a reload doesn't take effect, force-recreate the container so the file is re-mounted:
>
> ```bash
> dcp up -d --force-recreate caddy
> ```

## 4. Recreate after `.env` or compose changes

Environment variables and compose-level settings are only read when a container is created. `restart` won't pick them up.

```bash
dcp up -d
```

Compose detects the diff and recreates only the affected services.

## 5. In-place restart (no config change)

Useful for clearing transient bad state — stuck connections, runaway memory, etc.

```bash
dcp restart app          # just the app
dcp restart app caddy    # app + caddy
dcp restart              # everything
```

This stops and starts the existing container; it does not recreate it. Faster than recreate, but won't pick up new images or env vars.

## 6. Full stack bounce (last resort)

Only when sections 2–5 don't help. This stops everything cleanly, then brings it back.

```bash
dcp down
dcp up -d --build
```

Volumes are preserved (`postgres_data`, `caddy_data`, `caddy_config`, `db_backups`) — no data loss. **Do not** add `-v`; that would wipe the database.

## 7. Verify

```bash
dcp ps
```

All four services should show `Up`:
- `schulerpark-app-1`
- `schulerpark-caddy`
- `schulerpark-db` (Healthy)
- `schulerpark-db-backup`

Then probe the health endpoint:

```bash
curl -k --noproxy '*' https://localhost/api/health
```

Expected: `{"status":"healthy","timestamp":"..."}` with HTTP 200.

> **Important — corporate proxy gotcha.** A plain `curl https://localhost/...` from this host is intercepted by the McAfee corporate proxy (`proxy02.schuler.de`) and returns a 502 "Cannot Connect" HTML page that looks like an app failure but is not. Always pass `--noproxy '*'` for local probes. The `app` and `caddy` containers themselves have `NO_PROXY=*` set, so the proxy does not affect Caddy → app traffic.

If you suspect Caddy itself can't reach the app, probe from inside the caddy container — this bypasses the host proxy entirely:

```bash
docker exec schulerpark-caddy wget -qO- http://app:8080/api/health
```

## 8. Logs

```bash
dcp logs --tail=50 app
dcp logs --tail=50 caddy
dcp logs -f app          # follow live
```

## Common pitfalls

- **`docker compose restart` after `git pull` doesn't apply the new code.** Use `up -d --build` (section 2). `restart` only restarts the existing container with the existing image.
- **Editing `Caddyfile` and running `dcp restart caddy` works, but a reload (section 3) is faster and doesn't drop connections.**
- **502 from `curl https://localhost/...` on this host is almost always the corporate proxy, not Caddy.** Re-run with `--noproxy '*'` before debugging further.
- **Don't run `docker compose down -v`** — the `-v` removes named volumes, which means losing the database. Plain `down` is safe.
