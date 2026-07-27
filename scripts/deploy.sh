#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────
# Interactive defensive deploy for SchulerPark / LouisE.
#
# Adapted from swiss-manager's scripts/deploy.sh, but SchulerPark is a
# SINGLE-BOX deploy: this repo lives ON the production intranet host
# (park.schuler.de / 193.28.217.49) and the live stack runs from it. There
# is therefore NO scp/ssh/tarball transfer — every step runs locally.
#
# Deploys the CURRENT WORKING TREE (docker build uses the build context as-is,
# so uncommitted edits are included). The git SHA is recorded for traceability
# and a dirty tree only warns.
#
# Steps:
#   0  preflight        (clean-tree warn, branch warn, capture DEPLOY_SHA)
#   1  snapshot image   (tag current schuler-park-app:latest for rollback —
#                        MUST happen before the build overwrites :latest)
#   2  resource check   (local RAM / CPU / disk — build competes with live app)
#   3  build            (docker compose build app — the real prod build)
#   4  runtime smoke    (ephemeral postgres + new image, runs EF MigrateAsync
#                        on a clean DB, polls /api/health — catches a bad
#                        migration BEFORE it touches the live DB)
#   5  DB backup        (existing db-backup.sh inside its container)
#   6  roll             (compose up -d --no-build app; migrations run on start;
#                        poll /api/health through Caddy)
#   7  health + smoke   (curl -k https://localhost/api/health + manual prompt)
#   8  cleanup          (keep last N rollback tags, prune dangling + build cache)
#
# Flags:
#   --yes        skip per-step prompts (destructive ops still confirm)
#   --rollback   restore from saved state in the state file
#   --cleanup    reclaim local disk (docker prune); no deploy
#
# State: /tmp/schulerpark-deploy.last (KEY=VALUE; sourced for --rollback).
# Lock:  /tmp/schulerpark-deploy.lock.
# ──────────────────────────────────────────────────────────

set -euo pipefail
IFS=$'\n\t'

# ─── Configuration (edit at top, no env-var indirection) ──
REPO="/home/Prache.Maurya/SchulerPark"
APP_SERVICE="app"
DB_SERVICE="db"
DB_BACKUP_SERVICE="db-backup"
IMAGE_NAME="schulerpark-app"          # compose names a `build:` image <project>-<service>
DB_USER="louise"
DB_NAME="louise"
BACKUP_GLOB="louise_*.sql.gz"         # db-backup.sh writes this prefix (legacy: schulerpark_*)
HEALTH_URL="https://localhost/api/health"   # through Caddy; self-signed cert → curl -k
# NB: all health curls pass --noproxy '*'. This box sits behind Schuler's McAfee
# proxy (HTTP_PROXY/HTTPS_PROXY set); no_proxy is semicolon-separated, which curl
# does NOT parse, so without --noproxy even 127.0.0.1/localhost is routed through
# the proxy and returns 502 — a false smoke-test/health failure.
HEALTH_TIMEOUT_SECONDS=180
LOCK_FILE="/tmp/schulerpark-deploy.lock"
# In the repo, NOT /tmp: the state file is `source`d on rollback, and anyone
# could pre-create a file in world-writable /tmp to run shell as the deploy
# user (who has docker access = root-equivalent).
STATE_FILE="$REPO/.deploy-state"

# Resource thresholds (Step 2)
DISK_ROOT_WARN_GB=5
DISK_ROOT_ABORT_GB=2
DISK_DOCKER_WARN_GB=8
DISK_DOCKER_ABORT_GB=3
MEM_AVAIL_WARN_MB=1500
MEM_AVAIL_ABORT_MB=800
LOAD_PER_CPU_WARN=2.0

# Post-deploy cleanup (Step 8)
KEEP_ROLLBACK_TAGS=2
BUILD_CACHE_MAX_GB=5

# Runtime-smoke (Step 4) — synthetic creds only; never reads prod .env.
SMOKE_HOST_PORT=18080

# ─── Flags ──
ASSUME_YES=0
MODE=deploy

usage() {
  cat >&2 <<EOF
Usage: $0 [--yes] [--rollback | --cleanup]

  --yes        skip per-step prompts (destructive ops still confirm)
  --rollback   restore from saved state in $STATE_FILE
  --cleanup    reclaim local disk (docker system prune + builder prune); no deploy
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --yes)      ASSUME_YES=1; shift ;;
    --rollback) MODE=rollback; shift ;;
    --cleanup)  MODE=cleanup; shift ;;
    -h|--help)  usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 1 ;;
  esac
done

# ─── Helpers ──
log()  { echo "[$(date '+%H:%M:%S')] $*" >&2; }

step() {
  local n="$1"; shift
  echo >&2
  echo "═══════════════════════════════════════════════════════════════" >&2
  echo "  Step $n: $*" >&2
  echo "═══════════════════════════════════════════════════════════════" >&2
}

# Run docker compose with the prod overlay. A function (not a string) so the
# multi-word command survives IFS=$'\n\t' without word-splitting surprises.
compose() { docker compose -f docker-compose.yml -f docker-compose.prod.yml "$@"; }

# confirm "prompt" [--always-prompt]
# Returns 0 on y/Y. With --yes, auto-yes unless --always-prompt is given.
confirm() {
  local prompt="$1"; local always_prompt=0; shift || true
  while [[ $# -gt 0 ]]; do
    case "$1" in --always-prompt) always_prompt=1 ;; esac
    shift
  done
  if [[ $ASSUME_YES -eq 1 && $always_prompt -eq 0 ]]; then
    log "AUTO-YES: $prompt"; return 0
  fi
  local ans=""
  read -r -p "$prompt [y/N] " ans || ans=""
  [[ "$ans" =~ ^[yY]$ ]]
}

# confirm with a literal phrase (e.g. "restore"). Always prompts.
confirm_phrase() {
  local prompt="$1" expected="$2" ans=""
  read -r -p "$prompt (type '$expected' to confirm) " ans || ans=""
  [[ "$ans" == "$expected" ]]
}

abort() { log "ABORT: $*"; exit 1; }

record_state() {
  local key="$1" value="$2"
  ( umask 077; printf '%s=%q\n' "$key" "$value" >> "$STATE_FILE" )
}

# The state file gets `source`d — refuse anything that isn't a private regular
# file owned by us.
verify_state_file() {
  [[ -f "$STATE_FILE" && ! -L "$STATE_FILE" ]] || abort "State file $STATE_FILE missing or is a symlink"
  [[ -O "$STATE_FILE" ]] || abort "State file $STATE_FILE is not owned by $(id -un) — refusing to source it"
  local perms
  perms=$(stat -c '%a' "$STATE_FILE")
  [[ "$perms" =~ ^[0-7]00$ ]] || abort "State file $STATE_FILE is group/world accessible (perms $perms) — refusing to source it"
}

# ─── Cleanup trap (runs on every exit) ──
LOCK_HELD=0
SMOKE_NET=""
SMOKE_DB_NAME=""
SMOKE_APP_NAME=""
cleanup() {
  local rc=$?
  if [[ -n "$SMOKE_APP_NAME" || -n "$SMOKE_DB_NAME" ]]; then
    docker rm -f "$SMOKE_APP_NAME" "$SMOKE_DB_NAME" >/dev/null 2>&1 || true
  fi
  if [[ -n "$SMOKE_NET" ]]; then
    docker network rm "$SMOKE_NET" >/dev/null 2>&1 || true
  fi
  if [[ $LOCK_HELD -eq 1 ]]; then
    rm -f "$LOCK_FILE" || true
  fi
  if [[ $rc -ne 0 && $MODE == "deploy" ]]; then
    log "Deploy did not complete cleanly (exit=$rc). State preserved at $STATE_FILE — '$0 --rollback' is available."
  fi
}
trap cleanup EXIT

acquire_lock() {
  if ! ( set -o noclobber; echo "$$" > "$LOCK_FILE" ) 2>/dev/null; then
    log "Deploy lock exists at $LOCK_FILE (pid $(cat "$LOCK_FILE" 2>/dev/null || echo unknown))"
    log "  If no other deploy is running, remove the stale lock with:  rm $LOCK_FILE"
    exit 1
  fi
  LOCK_HELD=1
}

# Polls /api/health through Caddy (self-signed → -k) until ok or timeout.
wait_for_health() {
  local deadline=$(( $(date +%s) + HEALTH_TIMEOUT_SECONDS ))
  log "Polling $HEALTH_URL until healthy (timeout ${HEALTH_TIMEOUT_SECONDS}s; migrations run during this window)..."
  while [[ $(date +%s) -lt $deadline ]]; do
    if curl -fsS -k --noproxy '*' --max-time 5 "$HEALTH_URL" >/dev/null 2>&1; then
      echo >&2; log "App is healthy ($HEALTH_URL responded ok)."
      return 0
    fi
    printf '.' >&2
    sleep 5
  done
  echo >&2
  log "Healthcheck timed out after ${HEALTH_TIMEOUT_SECONDS}s. Last app logs:"
  compose logs --tail=120 "$APP_SERVICE" >&2 || true
  return 1
}

# ─── Steps ──

step_0_preflight() {
  step 0 "Preflight (working tree + branch)"

  if [[ "$(pwd)" != "$REPO" ]]; then
    abort "Run from $REPO (current: $(pwd))"
  fi
  if ! command -v docker >/dev/null 2>&1; then
    abort "docker not found on PATH"
  fi
  if ! docker info >/dev/null 2>&1; then
    abort "docker daemon not reachable"
  fi

  local branch
  branch=$(git symbolic-ref --short HEAD 2>/dev/null || echo "")
  if [[ "$branch" != "master" ]]; then
    log "WARNING: current branch is '$branch' (not master)."
    confirm "Deploy from '$branch' anyway?" || abort "Cancelled — not on master"
  fi

  DEPLOY_SHA=$(git rev-parse HEAD)
  record_state DEPLOY_SHA "$DEPLOY_SHA"

  if [[ -n "$(git status --porcelain)" ]]; then
    log "WARNING: working tree has uncommitted changes — they WILL be included in the build:"
    git status --short >&2
    confirm "Deploy the working tree as-is (uncommitted changes included)?" \
      || abort "Cancelled — commit or stash first"
    record_state DEPLOY_DIRTY 1
  fi
  log "Deploying working tree at commit $DEPLOY_SHA"
}

step_1_snapshot_image() {
  step 1 "Snapshot current image for rollback (before the build overwrites :latest)"

  # Single daemon: step 3's build replaces $IMAGE_NAME:latest in-place, so we
  # must capture + tag the CURRENT image now, before it's gone.
  PREV_IMAGE_ID=$(docker images -q "$IMAGE_NAME:latest")
  PREV_IMAGE_ID="${PREV_IMAGE_ID//$'\r'/}"
  PREV_SHA=$(git rev-parse HEAD)   # best-effort; informational for working-tree deploys
  record_state PREV_SHA "$PREV_SHA"

  if [[ -z "$PREV_IMAGE_ID" ]]; then
    log "WARN: no existing $IMAGE_NAME:latest image. Code-only rollback will be unavailable (first deploy?)."
    record_state PREV_IMAGE_ID ""
    return 0
  fi

  ROLLBACK_TAG="${IMAGE_NAME}:rollback-$(date +%Y%m%d-%H%M%S)-${PREV_SHA:0:8}"
  confirm "Tag current image $PREV_IMAGE_ID as $ROLLBACK_TAG ?" || abort "Cancelled at image snapshot"
  docker tag "$PREV_IMAGE_ID" "$ROLLBACK_TAG"
  if ! docker image inspect "$ROLLBACK_TAG" >/dev/null 2>&1; then
    abort "docker tag claimed success but $ROLLBACK_TAG is not present"
  fi
  record_state PREV_IMAGE_ID "$PREV_IMAGE_ID"
  record_state ROLLBACK_TAG  "$ROLLBACK_TAG"
  log "Rollback image tagged: $ROLLBACK_TAG"
}

step_2_resource_check() {
  step 2 "Resource check (RAM / CPU / disk — local box)"

  local disk_root_kb disk_docker_kb mem_avail_mb mem_total_mb load_1m ncpu
  disk_root_kb=$(df --output=avail / 2>/dev/null | tail -n+2 | tr -d ' ')
  disk_docker_kb=$(df --output=avail /var/lib/docker 2>/dev/null | tail -n+2 | tr -d ' ' || true)
  [[ -z "${disk_docker_kb:-}" ]] && disk_docker_kb="$disk_root_kb"
  mem_avail_mb=$(awk '/MemAvailable/ {printf "%.0f", $2/1024; exit}' /proc/meminfo)
  mem_total_mb=$(awk '/MemTotal/    {printf "%.0f", $2/1024; exit}' /proc/meminfo)
  load_1m=$(awk '{print $1; exit}' /proc/loadavg)
  ncpu=$(nproc)

  if [[ -z "$disk_root_kb" || -z "$mem_avail_mb" || -z "$load_1m" || -z "$ncpu" ]]; then
    abort "Could not read local resource info"
  fi

  local disk_root_gb disk_docker_gb load_per_cpu
  disk_root_gb=$(awk -v kb="$disk_root_kb" 'BEGIN{printf "%.1f", kb/1024/1024}')
  disk_docker_gb=$(awk -v kb="$disk_docker_kb" 'BEGIN{printf "%.1f", kb/1024/1024}')
  load_per_cpu=$(awk -v l="$load_1m" -v n="$ncpu" 'BEGIN{printf "%.2f", l/n}')

  log "  / free:           ${disk_root_gb} GB"
  log "  /var/lib/docker:  ${disk_docker_gb} GB"
  log "  MemAvailable:     ${mem_avail_mb} MB / ${mem_total_mb} MB total"
  log "  Load (1m / cpus): ${load_1m} / ${ncpu}  (per-cpu ${load_per_cpu})"

  local errors=() warnings=()
  awk -v g="$disk_root_gb"   -v t="$DISK_ROOT_ABORT_GB"   'BEGIN{exit !(g<t)}' && errors+=("/ has only ${disk_root_gb} GB free (< ${DISK_ROOT_ABORT_GB} GB). Reclaim: 'journalctl --vacuum-time=2d'.")
  awk -v g="$disk_docker_gb" -v t="$DISK_DOCKER_ABORT_GB" 'BEGIN{exit !(g<t)}' && errors+=("/var/lib/docker has only ${disk_docker_gb} GB free (< ${DISK_DOCKER_ABORT_GB} GB). Reclaim: 'docker system prune -af' or '$0 --cleanup'.")
  awk -v m="$mem_avail_mb"   -v t="$MEM_AVAIL_ABORT_MB"   'BEGIN{exit !(m<t)}' && errors+=("MemAvailable ${mem_avail_mb} MB (< ${MEM_AVAIL_ABORT_MB} MB). The build may OOM. Free memory first.")

  if [[ ${#errors[@]} -gt 0 ]]; then
    for e in "${errors[@]}"; do log "ERROR: $e"; done
    abort "Resource hard limits violated; cannot deploy safely"
  fi

  awk -v g="$disk_root_gb"   -v t="$DISK_ROOT_WARN_GB"   'BEGIN{exit !(g<t)}' && warnings+=("/ has ${disk_root_gb} GB free (< ${DISK_ROOT_WARN_GB} GB recommended).")
  awk -v g="$disk_docker_gb" -v t="$DISK_DOCKER_WARN_GB" 'BEGIN{exit !(g<t)}' && warnings+=("/var/lib/docker has ${disk_docker_gb} GB free (< ${DISK_DOCKER_WARN_GB} GB recommended).")
  awk -v m="$mem_avail_mb"   -v t="$MEM_AVAIL_WARN_MB"   'BEGIN{exit !(m<t)}' && warnings+=("MemAvailable ${mem_avail_mb} MB (< ${MEM_AVAIL_WARN_MB} MB recommended).")
  awk -v p="$load_per_cpu"   -v t="$LOAD_PER_CPU_WARN"   'BEGIN{exit !(p>t)}' && warnings+=("Per-cpu load ${load_per_cpu} (> ${LOAD_PER_CPU_WARN}). Build will be slow.")

  if [[ ${#warnings[@]} -gt 0 ]]; then
    for w in "${warnings[@]}"; do log "WARN: $w"; done
    confirm "Continue despite warnings?" || abort "Cancelled at resource check"
  fi
}

step_3_build() {
  step 3 "Build image (docker compose build $APP_SERVICE)"

  log "Building (multi-stage: frontend npm build → backend dotnet publish). Cold: several min."
  if ! compose build "$APP_SERVICE"; then
    abort "Build failed — fix before deploying. The live container is still running unchanged."
  fi
  log "Build OK — $IMAGE_NAME:latest is the new image."
}

step_4_smoke() {
  step 4 "Runtime smoke (ephemeral postgres + new image: boot, EF migrate, /api/health)"

  # Boots the freshly built image against a throwaway postgres with SYNTHETIC
  # env only (never reads prod .env), exercising MigrateAsync() on a clean DB
  # and /api/health — so a bad migration is caught BEFORE it hits the live DB.
  local suffix="smoke-$$"
  SMOKE_NET="sp-${suffix}-net"
  SMOKE_DB_NAME="sp-${suffix}-db"
  SMOKE_APP_NAME="sp-${suffix}-app"

  log "Creating ephemeral network $SMOKE_NET..."
  docker network create "$SMOKE_NET" >/dev/null 2>&1 || abort "Failed to create $SMOKE_NET"

  log "Starting ephemeral postgres..."
  docker run -d --name "$SMOKE_DB_NAME" --network "$SMOKE_NET" \
    -e POSTGRES_USER=preflight -e POSTGRES_PASSWORD=preflight -e POSTGRES_DB=preflight \
    postgres:16-alpine >/dev/null 2>&1 || abort "Failed to start postgres"

  log "Waiting for postgres + preflight DB ready (max 60s)..."
  local pg_deadline=$(( $(date +%s) + 60 )) pg_ready=0
  while [[ $(date +%s) -lt $pg_deadline ]]; do
    if docker exec "$SMOKE_DB_NAME" psql -U preflight -d preflight -c 'SELECT 1' >/dev/null 2>&1; then
      pg_ready=1; break
    fi
    sleep 2
  done
  [[ $pg_ready -eq 1 ]] || { docker logs --tail 80 "$SMOKE_DB_NAME" >&2 || true; abort "Postgres not ready"; }

  log "Starting app container — EF MigrateAsync runs during startup..."
  if ! docker run -d --name "$SMOKE_APP_NAME" --network "$SMOKE_NET" \
        -p "127.0.0.1:${SMOKE_HOST_PORT}:8080" \
        -e "ConnectionStrings__Default=Host=${SMOKE_DB_NAME};Port=5432;Database=preflight;Username=preflight;Password=preflight" \
        -e "Jwt__Secret=preflight-smoke-secret-not-real-please-32chars" \
        -e "ASPNETCORE_ENVIRONMENT=Production" \
        "${IMAGE_NAME}:latest" >/dev/null 2>&1; then
    abort "Failed to start app container (is ${IMAGE_NAME}:latest present? port ${SMOKE_HOST_PORT} free?)"
  fi

  log "Polling http://127.0.0.1:${SMOKE_HOST_PORT}/api/health (timeout 90s)..."
  local app_deadline=$(( $(date +%s) + 90 )) ok=0
  while [[ $(date +%s) -lt $app_deadline ]]; do
    if [[ "$(docker inspect -f '{{.State.Status}}' "$SMOKE_APP_NAME" 2>/dev/null)" != "running" ]]; then
      log "App container exited unexpectedly. Logs:"
      docker logs --tail 120 "$SMOKE_APP_NAME" >&2 || true
      abort "App exited during smoke — common cause: failing EF migration or missing env"
    fi
    if curl -fsS --noproxy '*' --max-time 5 "http://127.0.0.1:${SMOKE_HOST_PORT}/api/health" >/dev/null 2>&1; then
      ok=1; break
    fi
    sleep 3
  done
  if [[ $ok -ne 1 ]]; then
    log "App did not pass /api/health within 90s. Logs:"
    docker logs --tail 120 "$SMOKE_APP_NAME" >&2 || true
    abort "Runtime smoke failed — /api/health never responded ok"
  fi
  log "Runtime smoke OK — boots, migrations apply, /api/health responds."

  docker rm -f "$SMOKE_APP_NAME" "$SMOKE_DB_NAME" >/dev/null 2>&1 || true
  docker network rm "$SMOKE_NET" >/dev/null 2>&1 || true
  SMOKE_APP_NAME=""; SMOKE_DB_NAME=""; SMOKE_NET=""

  confirm "Proceed with deploy of $DEPLOY_SHA?" || abort "Cancelled at preflight summary"
}

step_5_backup() {
  step 5 "DB backup on prod (before the new code migrates the live DB)"

  log "Invoking /usr/local/bin/db-backup.sh inside the $DB_BACKUP_SERVICE container."
  confirm "Proceed with backup?" --always-prompt || abort "Cancelled at backup"

  compose exec -T "$DB_BACKUP_SERVICE" /usr/local/bin/db-backup.sh

  local backup_path
  backup_path=$(compose exec -T "$DB_BACKUP_SERVICE" sh -c "ls -t /backups/${BACKUP_GLOB} 2>/dev/null | head -n1")
  backup_path="${backup_path//$'\r'/}"
  BACKUP_FILE="${backup_path##*/}"
  if [[ -z "$BACKUP_FILE" ]]; then
    abort "Could not locate a new backup (${BACKUP_GLOB}) in /backups/"
  fi
  if ! compose exec -T "$DB_BACKUP_SERVICE" test -s "/backups/$BACKUP_FILE"; then
    abort "Backup file /backups/$BACKUP_FILE is empty or missing"
  fi
  local size
  size=$(compose exec -T "$DB_BACKUP_SERVICE" du -h "/backups/$BACKUP_FILE" | awk '{print $1}')
  log "Backup OK: /backups/$BACKUP_FILE ($size)"
  record_state BACKUP_FILE "$BACKUP_FILE"
}

step_6_roll() {
  step 6 "Roll the app container (up -d --no-build; migrations run on start)"

  confirm "Recreate the $APP_SERVICE container with the new image?" || abort "Cancelled before roll"
  # --no-build: reuse exactly the image validated by the smoke test in step 4.
  compose up -d --no-build "$APP_SERVICE"

  if ! wait_for_health; then
    log "Container did not reach healthy state."
    if confirm "Rollback now (code-only)?" --always-prompt; then
      rollback_code_only
      exit 1
    fi
    abort "Healthcheck timeout; rollback declined"
  fi
}

step_7_health_and_smoke() {
  step 7 "Health check + manual smoke prompt"

  if ! curl -fsS -k --noproxy '*' --max-time 10 "$HEALTH_URL" >/dev/null 2>&1; then
    log "Health check against $HEALTH_URL failed."
    if confirm "Rollback now (code-only)?" --always-prompt; then
      rollback_code_only; exit 1
    fi
    abort "Health check failed; rollback declined"
  fi
  log "Health OK: $HEALTH_URL"

  cat >&2 <<'CHECKLIST'

────────────────────────────────────────────────────────────────
  MANUAL SMOKE TEST — do these in your browser (https://localhost/ via tunnel):
    1. Log in (admin@schulerpark.local).
    2. Open the locations list; pick a location.
    3. Create a booking for a future day.
    4. Check "My bookings" reflects it.
    5. (Optional) Trigger a manual lottery run as admin.
────────────────────────────────────────────────────────────────

Choose:
  [c]ontinue    deploy succeeded
  [r]ollback    code-only rollback (DB stays on new schema)
  [a]bort       leave new code running, exit script

CHECKLIST
  local choice=""
  if [[ $ASSUME_YES -eq 1 ]]; then
    log "AUTO-YES: smoke checklist (treating as [c]ontinue)."
    choice="c"
  else
    read -r -p "Choice: " choice || choice=""
  fi
  case "$choice" in
    c|C)
      record_state DEPLOY_RESULT success
      log "Deploy successful: $DEPLOY_SHA"
      ;;
    r|R)
      rollback_code_only
      if confirm "Also restore DB from ${BACKUP_FILE:-<none>}? (DESTRUCTIVE)" --always-prompt; then
        rollback_code_and_db
      fi
      exit 0
      ;;
    a|A)
      log "Aborted at smoke prompt — new code is running. Use '$0 --rollback' if needed."
      exit 0
      ;;
    *)
      log "Unrecognized choice; treating as abort. New code is running. Use '$0 --rollback' if needed."
      exit 0
      ;;
  esac
}

step_8_cleanup() {
  step 8 "Post-deploy cleanup (rollback tags + build cache)"

  log "Docker disk usage BEFORE cleanup:"
  df -h / | tail -1 >&2 || true
  docker system df >&2 || true

  local to_delete
  to_delete=$(docker images "$IMAGE_NAME" --filter "reference=$IMAGE_NAME:rollback-*" \
                --format '{{.CreatedAt}}\t{{.Repository}}:{{.Tag}}' \
              | sort -r | tail -n +$((KEEP_ROLLBACK_TAGS + 1)) | cut -f2)
  to_delete="${to_delete//$'\r'/}"

  if [[ -n "$to_delete" ]]; then
    log "Rollback tags to remove (keeping $KEEP_ROLLBACK_TAGS most recent):"
    echo "$to_delete" | sed 's/^/    /' >&2
  else
    log "No stale rollback tags to remove."
  fi
  log "Build cache will be trimmed to at most ${BUILD_CACHE_MAX_GB}GB (LRU evicted first)."

  if ! confirm "Proceed with cleanup?"; then
    log "Skipped cleanup. Run '$0 --cleanup' later to reclaim disk."
    return 0
  fi

  if [[ -n "$to_delete" ]]; then
    printf '%s\n' "$to_delete" | xargs -r -n1 docker rmi >&2 || log "WARN: a 'docker rmi' failed; continuing."
  fi
  docker image prune -f >&2 || true
  docker builder prune -f --max-used-space "${BUILD_CACHE_MAX_GB}GB" >&2 || true

  log "Docker disk usage AFTER cleanup:"
  df -h / | tail -1 >&2 || true
  docker system df >&2 || true
}

# ─── Rollback ──

rollback_code_only() {
  step "R" "Rollback (code-only — retag previous image)"

  if [[ -z "${ROLLBACK_TAG:-}" ]]; then
    abort "Cannot run code-only rollback: no ROLLBACK_TAG in state (no previous image was saved)"
  fi
  log "Re-tagging $ROLLBACK_TAG as $IMAGE_NAME:latest ..."
  docker tag "$ROLLBACK_TAG" "$IMAGE_NAME:latest"

  log "Recreating $APP_SERVICE from the restored image ..."
  compose up -d --no-build "$APP_SERVICE"

  if ! wait_for_health; then
    abort "Rollback container did not become healthy — manual intervention required"
  fi
  log "Code rollback complete. NOTE: this deploys the working tree, so the git"
  log "tree is unchanged. Migrations from this deploy remain applied (EF migrations"
  log "are additive — old code tolerates them). For a schema rollback, use --rollback"
  log "and answer yes to the DB restore."
}

rollback_code_and_db() {
  step "R+DB" "Rollback (code + DB restore)"

  if [[ -z "${BACKUP_FILE:-}" ]]; then
    abort "BACKUP_FILE missing from state — cannot restore DB"
  fi
  log "DESTRUCTIVE: restoring DB from /backups/$BACKUP_FILE — all data created since the backup will be LOST."
  confirm_phrase "Proceed?" "restore" || abort "Cancelled at DB restore"

  log "Verifying gzip integrity of /backups/$BACKUP_FILE ..."
  if ! compose exec -T "$DB_BACKUP_SERVICE" gunzip -t "/backups/$BACKUP_FILE"; then
    abort "Backup file is corrupt — refusing to drop schema."
  fi

  log "Stopping app ..."
  compose stop "$APP_SERVICE"

  log "Dropping public schema ..."
  compose exec -T "$DB_SERVICE" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" \
    -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"

  log "Restoring from $BACKUP_FILE ..."
  if ! compose exec -T "$DB_BACKUP_SERVICE" sh -c "gunzip -c '/backups/$BACKUP_FILE'" \
       | compose exec -T "$DB_SERVICE" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME"; then
    log "RESTORE FAILED — DB may be in a partial state. Manual intervention required."
    abort "DB restore failed mid-pipe"
  fi

  # Sanity: public schema has tables after restore.
  local tcount
  tcount=$(compose exec -T "$DB_SERVICE" psql -tA -U "$DB_USER" -d "$DB_NAME" \
            -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" | tr -d '[:space:]')
  if [[ -z "$tcount" || "$tcount" -lt 1 ]]; then
    abort "Restore completed but public schema has no tables — restore is suspect"
  fi
  log "DB restore verified ($tcount tables present)."

  if [[ -n "${ROLLBACK_TAG:-}" ]]; then
    log "Re-tagging $ROLLBACK_TAG as $IMAGE_NAME:latest ..."
    docker tag "$ROLLBACK_TAG" "$IMAGE_NAME:latest"
  fi

  log "Starting app ..."
  compose up -d --no-build "$APP_SERVICE"
  wait_for_health || abort "Container did not become healthy after DB restore — manual intervention required"
  log "Code + DB rollback complete."
}

# ─── Disk cleanup (standalone) ──

step_cleanup_disk() {
  step "C" "Reclaim local disk"

  log "Disk + Docker usage BEFORE:"
  df -h / >&2 || true; docker system df >&2 || true

  confirm "Run 'docker system prune -a -f'? (keeps volumes — DB safe)" --always-prompt \
    || abort "Cancelled at docker system prune"
  docker system prune -a -f >&2

  if confirm "Also run 'docker builder prune -a -f'? Next build will be slow once." --always-prompt; then
    docker builder prune -a -f >&2
  fi

  log "Disk + Docker usage AFTER:"
  df -h / >&2 || true; docker system df >&2 || true
  log "Cleanup complete."
}

# ─── Entry ──

main() {
  cd "$REPO" || abort "Cannot cd to $REPO"
  acquire_lock

  if [[ $MODE == "cleanup" ]]; then
    step_cleanup_disk
    return 0
  fi

  if [[ $MODE == "rollback" ]]; then
    [[ -f "$STATE_FILE" ]] || abort "No state file at $STATE_FILE — manual rollback required (retag image + compose up -d)"
    verify_state_file
    # shellcheck disable=SC1090
    source "$STATE_FILE"
    log "Loaded state from $STATE_FILE"
    log "  PREV_SHA     = ${PREV_SHA:-<unset>}"
    log "  ROLLBACK_TAG = ${ROLLBACK_TAG:-<unset>}"
    log "  BACKUP_FILE  = ${BACKUP_FILE:-<unset>}"
    log "  DEPLOY_SHA   = ${DEPLOY_SHA:-<unset>}"

    confirm_phrase "Rollback the prod stack?" "rollback" || abort "Cancelled rollback"
    rollback_code_only
    if confirm "Also restore DB from ${BACKUP_FILE:-<none>}? (DESTRUCTIVE)" --always-prompt; then
      rollback_code_and_db
    fi
    return 0
  fi

  rm -f "$STATE_FILE"
  ( umask 077; : > "$STATE_FILE" )
  step_0_preflight
  step_1_snapshot_image
  step_2_resource_check
  step_3_build
  step_4_smoke
  step_5_backup
  step_6_roll
  step_7_health_and_smoke
  step_8_cleanup
}

main "$@"
