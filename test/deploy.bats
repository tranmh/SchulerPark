#!/usr/bin/env bats
# Unit tests for scripts/deploy.sh. The script runs from a copy whose REPO and
# lock file point into $BATS_TEST_TMPDIR (test_helper.bash); docker, git, curl
# and the clock are fakes, so nothing touches a real Docker daemon or host.

bats_require_minimum_version 1.5.0

setup() {
  load test_helper
  common_setup
  fake_clock
  fake_docker
  fake_curl 0
  deploy_functions_copy
}

COMPOSE='compose -f docker-compose.yml -f docker-compose.prod.yml'

# ─── Flags ───────────────────────────────────────────────────────────────────

@test "--help prints usage and exits 0 without taking the lock" {
  run "$DEPLOY_SCRIPT" --help
  assert_success
  assert_output --partial 'Usage:'
  assert_output --partial '--rollback'
  assert_file_not_exists "$DEPLOY_LOCK"
  refute_called docker '*'
}

@test "an unknown flag prints usage and exits 1" {
  run "$DEPLOY_SCRIPT" --force
  assert_failure 1
  assert_output --partial 'Unknown arg: --force'
  assert_output --partial 'Usage:'
  refute_called docker '*'
}

# ─── confirm / confirm_phrase ────────────────────────────────────────────────

@test "confirm accepts y and Y" {
  run_deploy_fn confirm "Go?" <<< "y"
  assert_success
  run_deploy_fn confirm "Go?" <<< "Y"
  assert_success
}

@test "confirm rejects anything but a single y" {
  local ans
  for ans in "n" "yes" "" " y" "yy"; do
    run_deploy_fn confirm "Go?" <<< "$ans"
    assert_failure
  done
}

@test "confirm treats end of input as no" {
  run_deploy_fn confirm "Go?" < /dev/null
  assert_failure
}

@test "--always-prompt still asks when --yes is set" {
  run_deploy_fn ASSUME_YES=1 confirm "Drop?" --always-prompt <<< "n"
  assert_failure
  refute_output --partial 'AUTO-YES'

  run_deploy_fn ASSUME_YES=1 confirm "Drop?" --always-prompt <<< "y"
  assert_success
}

@test "confirm_phrase needs the exact phrase" {
  run_deploy_fn confirm_phrase "Proceed?" restore <<< "restore"
  assert_success

  local ans
  for ans in "RESTORE" "y" "restore " ""; do
    run_deploy_fn confirm_phrase "Proceed?" restore <<< "$ans"
    assert_failure
  done
}

# ─── record_state / verify_state_file ────────────────────────────────────────

@test "record_state appends owner-only and quotes values so sourcing never runs them" {
  local evil='x $(touch "$BATS_TEST_TMPDIR/pwned") `id`; rm -rf /'
  # the value reaches the sub-shell through its environment
  EVIL="$evil" run_deploy_fn eval 'record_state DEPLOY_SHA abc; record_state BACKUP_FILE "$EVIL"'
  assert_success
  assert_equal "$(stat -c '%a' "$DEPLOY_STATE")" "600"

  run bash -c 'source "$1"; printf "%s|%s" "$DEPLOY_SHA" "$BACKUP_FILE"' _ "$DEPLOY_STATE"
  assert_success
  assert_output "abc|$evil"
  assert_file_not_exists "$BATS_TEST_TMPDIR/pwned"
}

@test "verify_state_file accepts a private regular file" {
  make_default_state_file
  run_deploy_fn verify_state_file
  assert_success
}

@test "verify_state_file aborts when the state file is missing" {
  run_deploy_fn verify_state_file
  assert_failure
  assert_output --partial 'missing or is a symlink'
}

@test "verify_state_file refuses a symlink even to a private file" {
  local target="$BATS_TEST_TMPDIR/elsewhere"
  ( umask 077; echo 'ROLLBACK_TAG=x' > "$target" )
  ln -s "$target" "$DEPLOY_STATE"

  run_deploy_fn verify_state_file
  assert_failure
  assert_output --partial 'missing or is a symlink'
}

@test "verify_state_file refuses group or world accessible files" {
  make_default_state_file
  local mode
  for mode in 640 604 644 660 666; do
    chmod "$mode" "$DEPLOY_STATE"
    run_deploy_fn verify_state_file
    assert_failure
    assert_output --partial "group/world accessible (perms $mode)"
  done
}

# ─── Lock and exit trap ──────────────────────────────────────────────────────

@test "acquire_lock writes the pid and the exit trap removes the lock" {
  run_deploy_fn eval 'acquire_lock; cat "$LOCK_FILE"'
  assert_success
  assert_output --regexp '^[0-9]+$'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "an existing lock stops the deploy and is left in place" {
  echo 4242 > "$DEPLOY_LOCK"

  run_deploy_fn acquire_lock
  assert_failure 1
  assert_output --partial "Deploy lock exists at $DEPLOY_LOCK (pid 4242)"
  assert_output --partial "rm $DEPLOY_LOCK"
  assert_file_exists "$DEPLOY_LOCK"
  assert_equal "$(cat "$DEPLOY_LOCK")" "4242"
}

@test "abort in deploy mode points at --rollback; other modes stay quiet" {
  run_deploy_fn abort "boom"
  assert_failure 1
  assert_output --partial 'ABORT: boom'
  assert_output --partial 'Deploy did not complete cleanly (exit=1)'

  run_deploy_fn MODE=rollback abort "boom"
  assert_failure 1
  refute_output --partial 'Deploy did not complete cleanly'
}

# ─── Step 0: preflight ───────────────────────────────────────────────────────

@test "preflight aborts outside the repo" {
  fake_git master
  run_deploy_fn eval 'cd /; step_0_preflight'
  assert_failure
  assert_output --partial "Run from $DEPLOY_REPO (current: /)"
  refute_called docker 'info'
}

@test "preflight aborts when the docker daemon is unreachable" {
  fake_git master
  stub_response docker 'info' 1
  run_deploy_fn step_0_preflight
  assert_failure
  assert_output --partial 'docker daemon not reachable'
  refute_called git 'rev-parse*'
}

@test "preflight off master asks and aborts on no" {
  fake_git feature/x
  run_deploy_fn step_0_preflight <<< "n"
  assert_failure
  assert_output --partial "current branch is 'feature/x' (not master)"
  assert_output --partial 'Cancelled — not on master'
}

@test "preflight off master continues on yes" {
  fake_git feature/x
  run_deploy_fn step_0_preflight <<< "y"
  assert_success
  assert_file_contains "$DEPLOY_STATE" "^DEPLOY_SHA=$DEFAULT_DEPLOY_SHA\$"
}

@test "preflight on a dirty tree lists changes and records DEPLOY_DIRTY on yes" {
  fake_git master "$DEFAULT_DEPLOY_SHA" ' M backend/Program.cs'
  run_deploy_fn step_0_preflight <<< "y"
  assert_success
  assert_output --partial 'uncommitted changes'
  assert_output --partial 'M backend/Program.cs'
  assert_file_contains "$DEPLOY_STATE" '^DEPLOY_DIRTY=1$'
}

@test "preflight on a dirty tree aborts on no" {
  fake_git master "$DEFAULT_DEPLOY_SHA" ' M backend/Program.cs'
  run_deploy_fn step_0_preflight <<< "n"
  assert_failure
  assert_output --partial 'Cancelled — commit or stash first'
  assert_file_not_contains "$DEPLOY_STATE" 'DEPLOY_DIRTY'
}

# ─── Step 1: snapshot ────────────────────────────────────────────────────────

@test "snapshot without a current image records an empty PREV_IMAGE_ID and tags nothing" {
  fake_git master
  run_deploy_fn step_1_snapshot_image
  assert_success
  assert_output --partial 'Code-only rollback will be unavailable'
  assert_file_contains "$DEPLOY_STATE" "^PREV_IMAGE_ID=''\$"
  refute_called docker 'tag *'
}

@test "snapshot tags the current image as rollback-<time>-<sha8> and records it" {
  fake_git master
  stub_response docker 'images -q schulerpark-app:latest' 0 $'sha256abc\r'
  run_deploy_fn ASSUME_YES=1 step_1_snapshot_image
  assert_success
  local tag=schulerpark-app:rollback-20261103-083000-01234567
  assert_called docker "tag sha256abc $tag"
  assert_file_contains "$DEPLOY_STATE" '^PREV_IMAGE_ID=sha256abc$'
  assert_file_contains "$DEPLOY_STATE" "^ROLLBACK_TAG=$tag\$"
  assert_file_contains "$DEPLOY_STATE" "^PREV_SHA=$DEFAULT_DEPLOY_SHA\$"
}

@test "snapshot aborts when the rollback tag does not show up" {
  fake_git master
  stub_response docker 'images -q *' 0 'sha256abc'
  stub_response docker 'image inspect *' 1
  run_deploy_fn ASSUME_YES=1 step_1_snapshot_image
  assert_failure
  assert_output --partial 'docker tag claimed success but'
  assert_file_not_contains "$DEPLOY_STATE" 'ROLLBACK_TAG'
}

@test "snapshot declined does not tag" {
  fake_git master
  stub_response docker 'images -q *' 0 'sha256abc'
  run_deploy_fn step_1_snapshot_image <<< "n"
  assert_failure
  assert_output --partial 'Cancelled at image snapshot'
  refute_called docker 'tag *'
}

# ─── Step 2: resources ───────────────────────────────────────────────────────

@test "resource check aborts below the hard disk limits" {
  stub_response df '--output=avail*' 0 $'Avail\n1048576'
  run_deploy_fn ASSUME_YES=1 step_2_resource_check
  assert_failure
  assert_output --partial '/ has only 1.0 GB free (< 2 GB)'
  assert_output --partial '/var/lib/docker has only 1.0 GB free (< 3 GB)'
  assert_output --partial 'Resource hard limits violated'
}

@test "resource check aborts when disk info cannot be read" {
  stub_response df '*' 0 'Avail'
  run_deploy_fn ASSUME_YES=1 step_2_resource_check
  assert_failure
  assert_output --partial 'Could not read local resource info'
}

# ─── Step 3: build ───────────────────────────────────────────────────────────

@test "build runs compose build with the prod overlay" {
  run_deploy_fn step_3_build
  assert_success
  assert_called docker "$COMPOSE build app"
}

@test "a failing build aborts and leaves the live container alone" {
  stub_response docker 'compose * build app' 1
  run_deploy_fn step_3_build
  assert_failure
  assert_output --partial 'Build failed'
  refute_called docker 'compose * up *'
}

# ─── Step 4: runtime smoke ───────────────────────────────────────────────────

@test "runtime smoke passes, removes its containers and network" {
  stub_response docker 'inspect -f *' 0 'running'
  run_deploy_fn ASSUME_YES=1 DEPLOY_SHA="$DEFAULT_DEPLOY_SHA" step_4_smoke
  assert_success
  assert_output --partial 'Runtime smoke OK'
  assert_called docker 'network create sp-smoke-*-net'
  assert_called docker 'run -d --name sp-smoke-*-db *postgres:16-alpine'
  assert_called docker 'run -d --name sp-smoke-*-app *-p 127.0.0.1:18080:8080*schulerpark-app:latest'
  assert_called docker 'rm -f sp-smoke-*-app sp-smoke-*-db'
  assert_called docker 'network rm sp-smoke-*-net'
}

@test "runtime smoke never passes the prod env file to the app" {
  stub_response docker 'inspect -f *' 0 'running'
  run_deploy_fn ASSUME_YES=1 DEPLOY_SHA="$DEFAULT_DEPLOY_SHA" step_4_smoke
  assert_success
  refute_called docker 'run *--env-file*'
  assert_called docker 'run * -e ASPNETCORE_ENVIRONMENT=Production *'
}

@test "runtime smoke aborts when the app container exits and cleans up via the trap" {
  stub_response docker 'inspect -f *' 0 'exited'
  run_deploy_fn step_4_smoke
  assert_failure
  assert_output --partial 'App exited during smoke'
  assert_called docker 'logs --tail 120 sp-smoke-*-app'
  assert_called docker 'rm -f sp-smoke-*-app sp-smoke-*-db'
  assert_called docker 'network rm sp-smoke-*-net'
}

@test "runtime smoke aborts when postgres never becomes ready" {
  fake_clock "$FAKE_CLOCK_EPOCH_DEFAULT" 7
  stub_response docker 'exec sp-smoke-* psql *' 2
  run_deploy_fn step_4_smoke
  assert_failure
  assert_output --partial 'Postgres not ready'
  refute_called docker 'run -d --name sp-smoke-*-app*'
  assert_called docker 'network rm sp-smoke-*-net'
}

@test "runtime smoke aborts when /api/health never answers" {
  fake_clock "$FAKE_CLOCK_EPOCH_DEFAULT" 7
  stub_response docker 'inspect -f *' 0 'running'
  # setup's healthy curl rule would match first, so replace it
  rm -f "$FAKE_DIR"/curl.*
  fake_curl 7
  run_deploy_fn step_4_smoke
  assert_failure
  assert_output --partial 'Runtime smoke failed'
}

# ─── Step 5: backup ──────────────────────────────────────────────────────────

@test "backup always prompts, even with --yes, and does nothing on no" {
  make_state_file
  run_deploy_fn ASSUME_YES=1 step_5_backup <<< "n"
  assert_failure
  assert_output --partial 'Cancelled at backup'
  refute_called docker 'compose *exec*'
}

@test "backup runs db-backup.sh and records the newest dump name" {
  make_state_file
  stub_response docker 'compose * exec -T db-backup sh -c *' 0 $'/backups/louise_20261103_083000.sql.gz\r'
  stub_response docker 'compose * exec -T db-backup du -h *' 0 $'12K\t/backups/louise_20261103_083000.sql.gz'
  run_deploy_fn step_5_backup <<< "y"
  assert_success
  assert_called docker "$COMPOSE exec -T db-backup /usr/local/bin/db-backup.sh"
  assert_called docker "$COMPOSE exec -T db-backup test -s /backups/louise_20261103_083000.sql.gz"
  assert_output --partial 'Backup OK: /backups/louise_20261103_083000.sql.gz (12K)'
  assert_file_contains "$DEPLOY_STATE" '^BACKUP_FILE=louise_20261103_083000.sql.gz$'
}

@test "backup aborts when no dump can be found" {
  make_state_file
  run_deploy_fn step_5_backup <<< "y"
  assert_failure
  assert_output --partial 'Could not locate a new backup (louise_*.sql.gz)'
  assert_file_not_contains "$DEPLOY_STATE" 'BACKUP_FILE'
}

@test "backup aborts on an empty dump" {
  make_state_file
  stub_response docker 'compose * exec -T db-backup sh -c *' 0 '/backups/louise_20261103_083000.sql.gz'
  stub_response docker 'compose * exec -T db-backup test -s *' 1
  run_deploy_fn step_5_backup <<< "y"
  assert_failure
  assert_output --partial 'is empty or missing'
  assert_file_not_contains "$DEPLOY_STATE" 'BACKUP_FILE'
}

# ─── Step 6: roll ────────────────────────────────────────────────────────────

@test "roll recreates the app without building and waits for health" {
  run_deploy_fn ASSUME_YES=1 step_6_roll
  assert_success
  assert_called docker "$COMPOSE up -d --no-build app"
  refute_called docker "$COMPOSE build*"
  assert_called curl "-fsS -k --noproxy * --max-time 5 https://localhost/api/health"
}

@test "roll declined does not touch the container" {
  run_deploy_fn step_6_roll <<< "n"
  assert_failure
  assert_output --partial 'Cancelled before roll'
  refute_called docker 'compose * up *'
}

@test "roll health timeout shows app logs and aborts when rollback is declined" {
  rm -f "$FAKE_DIR"/curl.*
  fake_curl 22
  run_deploy_fn ASSUME_YES=1 HEALTH_TIMEOUT_SECONDS=4 step_6_roll <<< "n"
  assert_failure
  assert_output --partial 'Healthcheck timed out after 4s'
  assert_called docker "$COMPOSE logs --tail=120 app"
  assert_output --partial 'Healthcheck timeout; rollback declined'
  refute_called docker 'tag *'
}

@test "roll health timeout with accepted rollback but no saved image aborts" {
  rm -f "$FAKE_DIR"/curl.*
  fake_curl 22
  run_deploy_fn ASSUME_YES=1 HEALTH_TIMEOUT_SECONDS=4 step_6_roll <<< "y"
  assert_failure
  assert_output --partial 'Cannot run code-only rollback: no ROLLBACK_TAG'
}

# ─── Step 7: health + manual smoke ───────────────────────────────────────────

@test "health with --yes records success" {
  make_state_file
  run_deploy_fn ASSUME_YES=1 DEPLOY_SHA=abc123 step_7_health_and_smoke
  assert_success
  assert_output --partial 'Deploy successful: abc123'
  assert_file_contains "$DEPLOY_STATE" '^DEPLOY_RESULT=success$'
}

@test "manual smoke [a]bort exits 0 without recording success" {
  make_state_file
  run_deploy_fn step_7_health_and_smoke <<< "a"
  assert_success
  assert_output --partial 'Aborted at smoke prompt'
  assert_file_not_contains "$DEPLOY_STATE" 'DEPLOY_RESULT'
}

@test "manual smoke with an unknown choice is treated as abort" {
  make_state_file
  run_deploy_fn step_7_health_and_smoke <<< "x"
  assert_success
  assert_output --partial 'Unrecognized choice; treating as abort'
  assert_file_not_contains "$DEPLOY_STATE" 'DEPLOY_RESULT'
}

@test "manual smoke [r]ollback retags the previous image and keeps the DB on no" {
  make_state_file
  run_deploy_fn ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" BACKUP_FILE="$DEFAULT_BACKUP_FILE" \
    step_7_health_and_smoke <<< $'r\nn'
  assert_success
  assert_called docker "tag $DEFAULT_ROLLBACK_TAG schulerpark-app:latest"
  assert_output --partial 'Code rollback complete'
  refute_called docker 'compose * psql*'
}

@test "a failing health check aborts when rollback is declined" {
  rm -f "$FAKE_DIR"/curl.*
  fake_curl 7
  run_deploy_fn ASSUME_YES=1 step_7_health_and_smoke <<< "n"
  assert_failure
  assert_output --partial 'Health check failed; rollback declined'
}

# ─── Step 8: cleanup ─────────────────────────────────────────────────────────

ROLLBACK_IMAGES=$'2026-11-03 08:00:00 +0000 UTC\tschulerpark-app:rollback-20261103-080000-aaaaaaaa
2026-10-01 08:00:00 +0000 UTC\tschulerpark-app:rollback-20261001-080000-bbbbbbbb
2026-11-01 08:00:00 +0000 UTC\tschulerpark-app:rollback-20261101-080000-cccccccc
2026-09-01 08:00:00 +0000 UTC\tschulerpark-app:rollback-20260901-080000-dddddddd'

@test "cleanup keeps the two newest rollback tags and removes the rest" {
  stub_response docker 'images schulerpark-app *' 0 "$ROLLBACK_IMAGES"
  run_deploy_fn ASSUME_YES=1 step_8_cleanup
  assert_success
  assert_called docker 'rmi schulerpark-app:rollback-20261001-080000-bbbbbbbb'
  assert_called docker 'rmi schulerpark-app:rollback-20260901-080000-dddddddd'
  refute_called docker 'rmi schulerpark-app:rollback-20261103-*'
  refute_called docker 'rmi schulerpark-app:rollback-20261101-*'
  assert_called docker 'image prune -f'
  assert_called docker 'builder prune -f --max-used-space 5GB'
}

@test "cleanup with no more than two rollback tags removes none" {
  stub_response docker 'images schulerpark-app *' 0 "$(head -n2 <<< "$ROLLBACK_IMAGES")"
  run_deploy_fn ASSUME_YES=1 step_8_cleanup
  assert_success
  assert_output --partial 'No stale rollback tags to remove.'
  refute_called docker 'rmi *'
}

@test "cleanup declined removes nothing" {
  stub_response docker 'images schulerpark-app *' 0 "$ROLLBACK_IMAGES"
  run_deploy_fn step_8_cleanup <<< "n"
  assert_success
  assert_output --partial 'Skipped cleanup'
  refute_called docker 'rmi *'
  refute_called docker '* prune *'
}

@test "a failing rmi only warns" {
  stub_response docker 'images schulerpark-app *' 0 "$ROLLBACK_IMAGES"
  stub_response docker 'rmi *' 1
  run_deploy_fn ASSUME_YES=1 step_8_cleanup
  assert_success
  assert_output --partial "WARN: a 'docker rmi' failed; continuing."
  assert_called docker 'image prune -f'
}

# ─── Rollback functions ──────────────────────────────────────────────────────

@test "code-only rollback retags, recreates the app and waits for health" {
  run_deploy_fn ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" rollback_code_only
  assert_success
  assert_called docker "tag $DEFAULT_ROLLBACK_TAG schulerpark-app:latest"
  assert_called docker "$COMPOSE up -d --no-build app"
  assert_output --partial 'Code rollback complete'
}

@test "code-only rollback aborts when the restored container is unhealthy" {
  rm -f "$FAKE_DIR"/curl.*
  fake_curl 22
  run_deploy_fn ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" HEALTH_TIMEOUT_SECONDS=4 rollback_code_only
  assert_failure
  assert_output --partial 'Rollback container did not become healthy'
}

@test "DB rollback without a BACKUP_FILE aborts before touching anything" {
  run_deploy_fn rollback_code_and_db <<< "restore"
  assert_failure
  assert_output --partial 'BACKUP_FILE missing from state'
  refute_called docker 'compose *'
}

@test "DB rollback needs the typed phrase" {
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "y"
  assert_failure
  assert_output --partial 'Cancelled at DB restore'
  refute_called docker 'compose *'
}

@test "DB rollback refuses a corrupt backup before dropping the schema" {
  stub_response docker 'compose * exec -T db-backup gunzip -t *' 1
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "restore"
  assert_failure
  assert_output --partial 'Backup file is corrupt'
  refute_called docker 'compose * stop *'
  refute_called docker '*DROP SCHEMA*'
}

@test "DB rollback stops the app, drops, restores, verifies and restarts" {
  stub_response docker 'compose * exec -T db psql -tA *' 0 ' 12 '
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" \
    rollback_code_and_db <<< "restore"
  assert_success
  assert_called docker "$COMPOSE exec -T db-backup gunzip -t /backups/$DEFAULT_BACKUP_FILE"
  assert_called docker "$COMPOSE stop app"
  assert_called docker "$COMPOSE exec -T db psql -v ON_ERROR_STOP=1 -U louise -d louise -c DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
  assert_called docker "$COMPOSE exec -T db-backup sh -c gunzip -c '/backups/$DEFAULT_BACKUP_FILE'"
  assert_called docker "tag $DEFAULT_ROLLBACK_TAG schulerpark-app:latest"
  assert_called docker "$COMPOSE up -d --no-build app"
  assert_output --partial 'DB restore verified (12 tables present)'
  assert_output --partial 'Code + DB rollback complete'
}

@test "DB rollback ordering: stop before drop, drop before restore" {
  stub_response docker 'compose * exec -T db psql -tA *' 0 '3'
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "restore"
  assert_success
  local stop drop restore
  stop=$(grep -n -- ' stop app$' "$FAKE_LOG" | head -n1 | cut -d: -f1)
  drop=$(grep -n -- 'DROP SCHEMA' "$FAKE_LOG" | head -n1 | cut -d: -f1)
  restore=$(grep -n -- "gunzip -c" "$FAKE_LOG" | head -n1 | cut -d: -f1)
  (( stop < drop && drop < restore ))
}

@test "DB rollback aborts when the restored schema has no tables" {
  stub_response docker 'compose * exec -T db psql -tA *' 0 '0'
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "restore"
  assert_failure
  assert_output --partial 'public schema has no tables'
  refute_called docker 'compose * up *'
}

@test "DB rollback aborts when psql fails mid-restore" {
  stub_response docker 'compose * exec -T db psql -v ON_ERROR_STOP=1 -U louise -d louise' 3
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "restore"
  assert_failure
  assert_output --partial 'RESTORE FAILED'
  assert_output --partial 'DB restore failed mid-pipe'
  refute_called docker 'compose * up *'
}

# ─── main: whole script ──────────────────────────────────────────────────────

@test "--rollback without a state file aborts" {
  run "$DEPLOY_SCRIPT" --rollback
  assert_failure
  assert_output --partial "No state file at $DEPLOY_STATE"
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "--rollback refuses a world-readable state file without sourcing it" {
  printf 'touch %q\n' "$BATS_TEST_TMPDIR/pwned" > "$DEPLOY_STATE"
  chmod 644 "$DEPLOY_STATE"
  run "$DEPLOY_SCRIPT" --rollback <<< "rollback"
  assert_failure
  assert_output --partial 'group/world accessible'
  assert_file_not_exists "$BATS_TEST_TMPDIR/pwned"
  refute_called docker 'tag *'
}

@test "--rollback loads the state, asks for the phrase and rolls code back" {
  make_default_state_file
  run "$DEPLOY_SCRIPT" --rollback <<< $'rollback\nn'
  assert_success
  assert_output --partial "ROLLBACK_TAG = $DEFAULT_ROLLBACK_TAG"
  assert_output --partial "BACKUP_FILE  = $DEFAULT_BACKUP_FILE"
  assert_called docker "tag $DEFAULT_ROLLBACK_TAG schulerpark-app:latest"
  refute_called docker '*DROP SCHEMA*'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "--rollback with a wrong phrase changes nothing" {
  make_default_state_file
  run "$DEPLOY_SCRIPT" --rollback <<< "y"
  assert_failure
  assert_output --partial 'Cancelled rollback'
  refute_called docker 'tag *'
  refute_called docker 'compose *'
}

@test "--rollback is refused while another deploy holds the lock" {
  make_default_state_file
  echo 999 > "$DEPLOY_LOCK"
  run "$DEPLOY_SCRIPT" --rollback <<< "rollback"
  assert_failure 1
  assert_output --partial '(pid 999)'
  refute_called docker '*'
  assert_file_exists "$DEPLOY_LOCK"
}

@test "--cleanup prunes images and asks separately for the build cache" {
  run "$DEPLOY_SCRIPT" --cleanup <<< $'y\nn'
  assert_success
  assert_called docker 'system prune -a -f'
  refute_called docker 'builder prune*'
  assert_output --partial 'Cleanup complete.'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "--cleanup with --yes still asks before pruning" {
  run "$DEPLOY_SCRIPT" --cleanup --yes <<< "n"
  assert_failure
  assert_output --partial 'Cancelled at docker system prune'
  refute_called docker '*prune*'
}

@test "a deploy starts from a fresh private state file and stops on preflight failure" {
  make_default_state_file
  fake_git master
  stub_response docker 'info' 1
  run "$DEPLOY_SCRIPT" --yes
  assert_failure
  assert_output --partial 'docker daemon not reachable'
  assert_output --partial 'Deploy did not complete cleanly (exit=1)'
  assert_file_exists "$DEPLOY_STATE"
  assert_equal "$(stat -c '%a' "$DEPLOY_STATE")" "600"
  assert_file_not_contains "$DEPLOY_STATE" 'ROLLBACK_TAG'
  refute_called docker 'compose *'
  assert_file_not_exists "$DEPLOY_LOCK"
}

# ─── Flags: combinations and edge cases ──────────────────────────────────────

@test "-h is --help: usage goes to stderr only, nothing on stdout" {
  run --separate-stderr "$DEPLOY_SCRIPT" -h
  assert_success
  assert_output ""
  [[ "$stderr" == *"Usage:"* ]] || fail "usage missing on stderr: $stderr"
  [[ "$stderr" == *"$DEPLOY_STATE"* ]] || fail "state file path missing from usage: $stderr"
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "an unknown flag after a valid one still exits 1 before taking the lock" {
  run "$DEPLOY_SCRIPT" --yes --bogus
  assert_failure 1
  assert_output --partial 'Unknown arg: --bogus'
  assert_file_not_exists "$DEPLOY_LOCK"
  refute_called docker '*'
}

@test "an empty argument is rejected" {
  run "$DEPLOY_SCRIPT" ""
  assert_failure 1
  assert_output --partial 'Unknown arg: '
  assert_output --partial 'Usage:'
}

@test "of --rollback and --cleanup the last one wins" {
  # cleanup mode prompts for the prune; rollback mode would abort on the missing state file
  run "$DEPLOY_SCRIPT" --rollback --cleanup <<< "n"
  assert_failure
  assert_output --partial 'Cancelled at docker system prune'
  refute_output --partial 'No state file'

  run "$DEPLOY_SCRIPT" --cleanup --rollback <<< "n"
  assert_failure
  assert_output --partial 'No state file'
  refute_called docker '*prune*'
}

# ─── Pure helpers ────────────────────────────────────────────────────────────

@test "log prefixes the message with the clock time" {
  run_deploy_fn log "hello world"
  assert_success
  assert_output '[08:30:00] hello world'
}

@test "step prints a numbered banner" {
  run_deploy_fn step 3 "Build image"
  assert_success
  assert_line --index 0 '═══════════════════════════════════════════════════════════════'
  assert_line --index 1 '  Step 3: Build image'
  assert_line --index 2 '═══════════════════════════════════════════════════════════════'
}

@test "compose always applies the prod overlay" {
  run_deploy_fn compose ps --services
  assert_success
  assert_called docker "$COMPOSE ps --services"
}

@test "confirm with --yes answers itself and leaves stdin untouched" {
  run_deploy_fn ASSUME_YES=1 eval 'rest=; confirm "Go?"; IFS= read -r rest || true; echo "rest=$rest"' <<< "n"
  assert_success
  assert_output --partial 'AUTO-YES: Go?'
  assert_output --partial 'rest=n'
}

@test "confirm_phrase ignores --yes and treats end of input as no" {
  run_deploy_fn ASSUME_YES=1 confirm_phrase "Rollback?" rollback < /dev/null
  assert_failure
  refute_output --partial 'AUTO-YES'
}

@test "record_state round-trips empty values, spaces and newlines" {
  run_deploy_fn eval 'record_state EMPTY ""; record_state SPACED "a b  c"; record_state MULTI "$(printf "l1\nl2")"'
  assert_success
  run bash -c 'source "$1"; printf "[%s][%s][%s]" "$EMPTY" "$SPACED" "$MULTI"' _ "$DEPLOY_STATE"
  assert_success
  assert_output $'[][a b  c][l1\nl2]'
}

@test "record_state appends to the existing state" {
  make_state_file DEPLOY_SHA=abc
  run_deploy_fn record_state BACKUP_FILE x.sql.gz
  assert_success
  assert_file_contains "$DEPLOY_STATE" '^DEPLOY_SHA=abc$'
  assert_file_contains "$DEPLOY_STATE" '^BACKUP_FILE=x.sql.gz$'
}

@test "verify_state_file accepts every owner-only mode" {
  make_default_state_file
  local mode
  for mode in 400 600 700; do
    chmod "$mode" "$DEPLOY_STATE"
    run_deploy_fn verify_state_file
    assert_success
  done
  chmod 600 "$DEPLOY_STATE"
}

@test "verify_state_file refuses a directory at the state path" {
  mkdir -p "$DEPLOY_STATE"
  run_deploy_fn verify_state_file
  assert_failure
  assert_output --partial 'missing or is a symlink'
}

@test "the lock is released when a step aborts" {
  run_deploy_fn eval 'acquire_lock; abort "boom"'
  assert_failure 1
  assert_output --partial 'ABORT: boom'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "wait_for_health keeps polling until the app answers" {
  rm -f "$FAKE_DIR"/curl.*
  fake_fallback curl 'n=$(cat "$FAKE_DIR/curl.count" 2>/dev/null || echo 0); n=$((n + 1)); echo "$n" > "$FAKE_DIR/curl.count"; [[ $n -ge 3 ]] && exit 0; exit 7'
  run_deploy_fn wait_for_health
  assert_success
  assert_output --partial 'App is healthy'
  assert_equal "$(fake_calls curl | wc -l)" 3
  refute_called docker 'compose * logs *'
}

@test "wait_for_health returns 1 at the deadline and shows the app logs" {
  rm -f "$FAKE_DIR"/curl.*
  fake_curl 7
  run_deploy_fn HEALTH_TIMEOUT_SECONDS=3 wait_for_health
  assert_failure 1
  assert_output --partial 'Healthcheck timed out after 3s'
  assert_called docker "$COMPOSE logs --tail=120 app"
}

# ─── Step 2: resource boundaries ─────────────────────────────────────────────

@test "resource check logs the free space and passes with ample disk" {
  stub_response df '--output=avail *' 0 $'Avail\n104857600'
  run_deploy_fn ASSUME_YES=1 step_2_resource_check
  assert_success
  assert_output --partial '/ free:           100.0 GB'
  assert_output --partial '/var/lib/docker:  100.0 GB'
  refute_output --partial 'has only'
  refute_output --partial 'GB free (<'
}

@test "resource check falls back to / when /var/lib/docker cannot be measured" {
  stub_response df '--output=avail /var/lib/docker' 1
  stub_response df '--output=avail /' 0 $'Avail\n104857600'
  run_deploy_fn ASSUME_YES=1 step_2_resource_check
  assert_success
  assert_output --partial '/var/lib/docker:  100.0 GB'
}

@test "resource check at exactly the abort limits only warns" {
  stub_response df '--output=avail /var/lib/docker' 0 $'Avail\n3145728'
  stub_response df '--output=avail /' 0 $'Avail\n2097152'
  run_deploy_fn step_2_resource_check <<< "n"
  assert_failure
  refute_output --partial 'Resource hard limits violated'
  assert_output --partial 'WARN: / has 2.0 GB free (< 5 GB recommended).'
  assert_output --partial 'WARN: /var/lib/docker has 3.0 GB free (< 8 GB recommended).'
  assert_output --partial 'Cancelled at resource check'
}

@test "resource check at exactly the warn limits does not warn about disk" {
  stub_response df '--output=avail /var/lib/docker' 0 $'Avail\n8388608'
  stub_response df '--output=avail /' 0 $'Avail\n5242880'
  run_deploy_fn ASSUME_YES=1 step_2_resource_check
  assert_success
  refute_output --partial 'GB free (<'
}

@test "resource check warnings are accepted with a yes" {
  stub_response df '--output=avail *' 0 $'Avail\n4194304'
  run_deploy_fn step_2_resource_check <<< "y"
  assert_success
  assert_output --partial 'WARN: / has 4.0 GB free (< 5 GB recommended).'
  refute_output --partial 'Cancelled'
}

# ─── Step 4: more failure modes ──────────────────────────────────────────────

@test "runtime smoke points the app at the ephemeral postgres with synthetic creds only" {
  stub_response docker 'inspect -f *' 0 'running'
  run_deploy_fn ASSUME_YES=1 DEPLOY_SHA=abc step_4_smoke
  assert_success
  assert_called docker 'run -d --name sp-smoke-*-app --network sp-smoke-*-net -p 127.0.0.1:18080:8080 -e ConnectionStrings__Default=Host=sp-smoke-*-db;Port=5432;Database=preflight;Username=preflight;Password=preflight -e Jwt__Secret=* -e ASPNETCORE_ENVIRONMENT=Production schulerpark-app:latest'
  assert_called docker 'exec sp-smoke-*-db psql -U preflight -d preflight -c SELECT 1'
  assert_called curl '-fsS --noproxy * --max-time 5 http://127.0.0.1:18080/api/health'
  assert_output --partial 'Proceed with deploy of abc?'
}

@test "runtime smoke aborts when the network cannot be created" {
  stub_response docker 'network create *' 1
  run_deploy_fn step_4_smoke
  assert_failure
  assert_output --partial 'Failed to create sp-smoke-'
  refute_called docker 'run *'
}

@test "runtime smoke aborts when postgres fails to start and removes the network" {
  stub_response docker 'run -d --name sp-smoke-*-db *' 1
  run_deploy_fn step_4_smoke
  assert_failure
  assert_output --partial 'Failed to start postgres'
  refute_called docker 'run -d --name sp-smoke-*-app*'
  assert_called docker 'network rm sp-smoke-*-net'
}

@test "runtime smoke aborts when the app container fails to start and cleans up" {
  stub_response docker 'run -d --name sp-smoke-*-app *' 1
  run_deploy_fn step_4_smoke
  assert_failure
  assert_output --partial 'Failed to start app container'
  assert_called docker 'rm -f sp-smoke-*-app sp-smoke-*-db'
  assert_called docker 'network rm sp-smoke-*-net'
}

@test "runtime smoke cleans up before asking and a no stops the deploy" {
  stub_response docker 'inspect -f *' 0 'running'
  run_deploy_fn DEPLOY_SHA=abc step_4_smoke <<< "n"
  assert_failure
  assert_output --partial 'Runtime smoke OK'
  assert_output --partial 'Cancelled at preflight summary'
  assert_called docker 'rm -f sp-smoke-*-app sp-smoke-*-db'
  assert_called docker 'network rm sp-smoke-*-net'
}

# ─── Steps 6/7: rollback branches ────────────────────────────────────────────

@test "roll health timeout with accepted rollback retags the saved image and exits 1" {
  rm -f "$FAKE_DIR"/curl.*
  fake_curl 22
  run_deploy_fn ASSUME_YES=1 HEALTH_TIMEOUT_SECONDS=4 ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" step_6_roll <<< "y"
  assert_failure 1
  assert_called docker "tag $DEFAULT_ROLLBACK_TAG schulerpark-app:latest"
  assert_equal "$(fake_calls docker | grep -c ' up -d --no-build app$')" 2
}

@test "a failing health check with accepted rollback retags and exits 1" {
  rm -f "$FAKE_DIR"/curl.*
  # the step-7 probe (--max-time 10) fails; the rollback's health poll (--max-time 5) succeeds
  fake_fallback curl '[[ "$*" == *"--max-time 10"* ]] && exit 7; exit 0'
  run_deploy_fn ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" step_7_health_and_smoke <<< "y"
  assert_failure 1
  assert_output --partial 'Health check against https://localhost/api/health failed.'
  assert_called docker "tag $DEFAULT_ROLLBACK_TAG schulerpark-app:latest"
  assert_output --partial 'Code rollback complete'
}

@test "manual smoke accepts an upper-case continue" {
  make_state_file
  run_deploy_fn DEPLOY_SHA=abc step_7_health_and_smoke <<< "C"
  assert_success
  assert_output --partial 'Deploy successful: abc'
  assert_file_contains "$DEPLOY_STATE" '^DEPLOY_RESULT=success$'
}

@test "manual smoke with no answer at all is treated as abort" {
  make_state_file
  run_deploy_fn step_7_health_and_smoke < /dev/null
  assert_success
  assert_output --partial 'Unrecognized choice; treating as abort'
  assert_file_not_contains "$DEPLOY_STATE" 'DEPLOY_RESULT'
}

@test "manual smoke [r]ollback plus DB restore drops and restores the schema" {
  make_state_file
  stub_response docker 'compose * exec -T db psql -tA *' 0 '5'
  run_deploy_fn ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" BACKUP_FILE="$DEFAULT_BACKUP_FILE" \
    step_7_health_and_smoke <<< $'r\ny\nrestore'
  assert_success
  assert_called docker '*DROP SCHEMA*'
  assert_output --partial 'Code + DB rollback complete'
  assert_file_not_contains "$DEPLOY_STATE" 'DEPLOY_RESULT'
}

# ─── Rollback functions: more branches ───────────────────────────────────────

@test "DB rollback without a saved image restores and restarts without retagging" {
  stub_response docker 'compose * exec -T db psql -tA *' 0 '7'
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "restore"
  assert_success
  refute_called docker 'tag *'
  assert_called docker "$COMPOSE up -d --no-build app"
  assert_output --partial 'DB restore verified (7 tables present)'
}

@test "DB rollback aborts when the table count cannot be read" {
  stub_response docker 'compose * exec -T db psql -tA *' 0
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "restore"
  assert_failure
  assert_output --partial 'public schema has no tables'
  refute_called docker 'compose * up *'
}

@test "DB rollback stops when the schema drop fails, before restoring" {
  stub_response docker 'compose * exec -T db psql -v ON_ERROR_STOP=1 -U louise -d louise -c DROP SCHEMA*' 2
  run_deploy_fn BACKUP_FILE="$DEFAULT_BACKUP_FILE" rollback_code_and_db <<< "restore"
  assert_failure
  assert_called docker "$COMPOSE stop app"
  refute_called docker '*gunzip -c*'
  refute_called docker 'compose * up *'
}

# ─── main: more whole-script paths ───────────────────────────────────────────

@test "--yes does not bypass the rollback phrase" {
  make_default_state_file
  run "$DEPLOY_SCRIPT" --yes --rollback < /dev/null
  assert_failure
  assert_output --partial 'Cancelled rollback'
  refute_called docker 'tag *'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "--rollback with DB restore accepted drops and restores the schema" {
  make_default_state_file
  stub_response docker 'compose * exec -T db psql -tA *' 0 '9'
  run "$DEPLOY_SCRIPT" --rollback <<< $'rollback\ny\nrestore'
  assert_success
  assert_called docker "tag $DEFAULT_ROLLBACK_TAG schulerpark-app:latest"
  assert_called docker '*DROP SCHEMA*'
  assert_called docker "$COMPOSE exec -T db-backup sh -c gunzip -c '/backups/$DEFAULT_BACKUP_FILE'"
  assert_output --partial 'Code + DB rollback complete'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "a deploy aborts when the repo directory is missing" {
  rm -rf "$DEPLOY_REPO"
  run "$DEPLOY_SCRIPT" --yes
  assert_failure 1
  assert_output --partial "Cannot cd to $DEPLOY_REPO"
  assert_file_not_exists "$DEPLOY_LOCK"
  refute_called docker '*'
}

@test "--cleanup with both prunes accepted keeps volumes" {
  run "$DEPLOY_SCRIPT" --cleanup <<< $'y\ny'
  assert_success
  assert_called docker 'system prune -a -f'
  assert_called docker 'builder prune -a -f'
  refute_called docker '*--volumes*'
  assert_output --partial 'Cleanup complete.'
}

@test "--cleanup fails when docker system prune fails and releases the lock" {
  stub_response docker 'system prune *' 1
  run "$DEPLOY_SCRIPT" --cleanup <<< "y"
  assert_failure
  refute_output --partial 'Cleanup complete.'
  refute_called docker 'builder prune*'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "a full --yes deploy runs the steps in order and records success" {
  fake_git master
  stub_response docker 'images -q schulerpark-app:latest' 0 'sha256abc'
  stub_response docker 'inspect -f *' 0 'running'
  stub_response docker 'compose * exec -T db-backup sh -c *' 0 "/backups/$DEFAULT_BACKUP_FILE"
  stub_response docker 'compose * exec -T db-backup du -h *' 0 $'12K\t/backups/'"$DEFAULT_BACKUP_FILE"
  stub_response df '--output=avail *' 0 $'Avail\n104857600'

  # the backup step always prompts, even with --yes
  run "$DEPLOY_SCRIPT" --yes <<< "y"
  assert_success
  assert_output --partial "Deploy successful: $DEFAULT_DEPLOY_SHA"
  assert_file_contains "$DEPLOY_STATE" "^DEPLOY_SHA=$DEFAULT_DEPLOY_SHA\$"
  assert_file_contains "$DEPLOY_STATE" '^PREV_IMAGE_ID=sha256abc$'
  assert_file_contains "$DEPLOY_STATE" '^ROLLBACK_TAG=schulerpark-app:rollback-'
  assert_file_contains "$DEPLOY_STATE" "^BACKUP_FILE=$DEFAULT_BACKUP_FILE\$"
  assert_file_contains "$DEPLOY_STATE" '^DEPLOY_RESULT=success$'
  assert_file_not_exists "$DEPLOY_LOCK"
  refute_called docker '*DROP SCHEMA*'
  refute_output --partial 'Deploy did not complete cleanly'

  local tag build smoke backup up
  tag=$(grep -n '^docker tag sha256abc' "$FAKE_LOG" | head -n1 | cut -d: -f1)
  build=$(grep -n ' build app$' "$FAKE_LOG" | head -n1 | cut -d: -f1)
  smoke=$(grep -n '^docker network create sp-smoke-' "$FAKE_LOG" | head -n1 | cut -d: -f1)
  backup=$(grep -n 'db-backup.sh$' "$FAKE_LOG" | head -n1 | cut -d: -f1)
  up=$(grep -n ' up -d --no-build app$' "$FAKE_LOG" | head -n1 | cut -d: -f1)
  (( tag < build && build < smoke && smoke < backup && backup < up )) \
    || fail "steps out of order: tag=$tag build=$build smoke=$smoke backup=$backup up=$up"
}
