#!/usr/bin/env bats
# Unit tests for scripts/deploy.sh. The script runs from a copy whose REPO and
# lock file point into $BATS_TEST_TMPDIR (test_helper.bash); docker, git, curl
# and the clock are fakes, so nothing touches a real Docker daemon or host.

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
