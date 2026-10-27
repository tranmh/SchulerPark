#!/usr/bin/env bats
# Unit tests for scripts/deploy.sh. The script is sourced without its final
# `main "$@"` line in a fresh bash, with REPO/STATE_FILE/LOCK_FILE pointed into
# $BATS_TEST_TMPDIR; docker, git, curl and date are fakes.

setup() {
  bats_load_library bats-support
  bats_load_library bats-assert
  bats_load_library bats-file
  load 'test_helper/fakes'
  fakes_setup
  fake_clock
  fake_cmd docker git curl sleep
  DEPLOY_SRC="$BATS_TEST_TMPDIR/deploy-lib.sh"
  sed -e '/^main "\$@"$/d' "$BATS_TEST_DIRNAME/../scripts/deploy.sh" > "$DEPLOY_SRC"
  DEPLOY_REPO="$BATS_TEST_TMPDIR/repo"
  DEPLOY_LOCK="$BATS_TEST_TMPDIR/deploy.lock"
  STATE="$DEPLOY_REPO/.deploy-state"
  mkdir -p "$DEPLOY_REPO"
  export DEPLOY_SRC DEPLOY_REPO DEPLOY_LOCK
  unset DEPLOY_INPUT
}

# deploy_eval CODE [script-args...] — run CODE with deploy.sh's functions loaded;
# stdin is $DEPLOY_INPUT (answers to prompts).
deploy_eval() {
  export DEPLOY_CODE="$1"; shift
  printf '%b' "${DEPLOY_INPUT-}" | bash -c 'source "$DEPLOY_SRC" "$@"
REPO="$DEPLOY_REPO"; STATE_FILE="$REPO/.deploy-state"; LOCK_FILE="$DEPLOY_LOCK"
eval "$DEPLOY_CODE"' deploy "$@"
}

write_state() {
  printf '%s\n' "$@" > "$STATE"
  chmod 600 "$STATE"
}

# ─── Arguments ──

@test "--help prints usage and exits 0" {
  run bash "$BATS_TEST_DIRNAME/../scripts/deploy.sh" --help
  assert_success
  assert_output --partial "Usage:"
  assert_output --partial "--rollback"
}

@test "an unknown argument fails with usage" {
  run bash "$BATS_TEST_DIRNAME/../scripts/deploy.sh" --bogus
  assert_failure 1
  assert_line "Unknown arg: --bogus"
  assert_output --partial "Usage:"
}

# ─── confirm / confirm_phrase ──

@test "confirm auto-answers yes with --yes" {
  run deploy_eval 'confirm "Go?"' --yes
  assert_success
  assert_output --partial "AUTO-YES: Go?"
}

@test "confirm --always-prompt still asks with --yes" {
  DEPLOY_INPUT='n\n' run deploy_eval 'confirm "Go?" --always-prompt' --yes
  assert_failure
  refute_output --partial "AUTO-YES"
}

@test "confirm accepts only a single y or Y" {
  DEPLOY_INPUT='Y\n' run deploy_eval 'confirm "Go?"'
  assert_success
  DEPLOY_INPUT='yes\n' run deploy_eval 'confirm "Go?"'
  assert_failure
  DEPLOY_INPUT='' run deploy_eval 'confirm "Go?"'
  assert_failure
}

@test "confirm_phrase requires the exact phrase" {
  DEPLOY_INPUT='restore\n' run deploy_eval 'confirm_phrase "Sure?" restore'
  assert_success
  DEPLOY_INPUT='Restore\n' run deploy_eval 'confirm_phrase "Sure?" restore'
  assert_failure
}

# ─── State file ──

@test "record_state writes a private, sourceable KEY=VALUE file" {
  run deploy_eval 'record_state A "two words; \$(touch pwned)"; record_state B x'
  assert_success
  assert_equal "$(stat -c '%a' "$STATE")" "600"
  run bash -c "cd '$BATS_TEST_TMPDIR' && source '$STATE' && printf '%s|%s' \"\$A\" \"\$B\""
  assert_output 'two words; $(touch pwned)|x'
  assert_file_not_exists "$BATS_TEST_TMPDIR/pwned"
}

@test "verify_state_file accepts a private regular file" {
  write_state "X=1"
  run deploy_eval 'verify_state_file'
  assert_success
}

@test "verify_state_file refuses a group/world readable file" {
  write_state "X=1"
  chmod 644 "$STATE"
  run deploy_eval 'verify_state_file'
  assert_failure
  assert_output --partial "group/world accessible (perms 644)"
}

@test "verify_state_file refuses a symlink" {
  printf 'X=1\n' > "$BATS_TEST_TMPDIR/real"
  chmod 600 "$BATS_TEST_TMPDIR/real"
  ln -s "$BATS_TEST_TMPDIR/real" "$STATE"
  run deploy_eval 'verify_state_file'
  assert_failure
  assert_output --partial "missing or is a symlink"
}

# ─── Lock ──

@test "acquire_lock writes the pid and the exit trap removes it" {
  run deploy_eval 'acquire_lock; cat "$LOCK_FILE"'
  assert_success
  assert_output --regexp '^[0-9]+$'
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "an existing lock stops the run and is left in place" {
  echo 4242 > "$DEPLOY_LOCK"
  run deploy_eval 'acquire_lock; echo got-lock'
  assert_failure 1
  assert_output --partial "Deploy lock exists at $DEPLOY_LOCK (pid 4242)"
  refute_output --partial "got-lock"
  assert_file_exists "$DEPLOY_LOCK"
}

@test "a failing deploy points at --rollback on exit" {
  run deploy_eval 'abort boom'
  assert_failure 1
  assert_output --partial "ABORT: boom"
  assert_output --partial "Deploy did not complete cleanly (exit=1)"
}

# ─── Step 0 ──

@test "preflight aborts outside the repo directory" {
  run deploy_eval 'cd "$BATS_TEST_TMPDIR"; step_0_preflight'
  assert_failure
  assert_output --partial "ABORT: Run from $DEPLOY_REPO"
}

@test "preflight aborts when the docker daemon is unreachable" {
  export FAKE_DOCKER_INFO_RC=1
  run deploy_eval 'cd "$REPO"; step_0_preflight'
  assert_failure
  assert_output --partial "docker daemon not reachable"
}

@test "preflight on master with a clean tree records DEPLOY_SHA" {
  export FAKE_GIT_SYMBOLIC_REF_STDOUT=master FAKE_GIT_REV_PARSE_STDOUT=abc123 FAKE_GIT_STATUS_STDOUT=""
  run deploy_eval 'cd "$REPO"; step_0_preflight'
  assert_success
  assert_output --partial "Deploying working tree at commit abc123"
  run cat "$STATE"
  assert_output "DEPLOY_SHA=abc123"
}

@test "preflight off master is cancelled when declined" {
  export FAKE_GIT_SYMBOLIC_REF_STDOUT=feature FAKE_GIT_REV_PARSE_STDOUT=abc123
  DEPLOY_INPUT='n\n' run deploy_eval 'cd "$REPO"; step_0_preflight'
  assert_failure
  assert_output --partial "current branch is 'feature'"
  assert_output --partial "Cancelled — not on master"
}

@test "preflight with a dirty tree records DEPLOY_DIRTY under --yes" {
  export FAKE_GIT_SYMBOLIC_REF_STDOUT=master FAKE_GIT_REV_PARSE_STDOUT=abc123 FAKE_GIT_STATUS_STDOUT=" M x.cs"
  run deploy_eval 'cd "$REPO"; step_0_preflight' --yes
  assert_success
  assert_output --partial "uncommitted changes"
  assert_file_contains "$STATE" '^DEPLOY_DIRTY=1$'
}

# ─── Step 1 ──

@test "snapshot tags the current image with timestamp and sha" {
  export FAKE_DOCKER_IMAGES_STDOUT=img123 FAKE_GIT_REV_PARSE_STDOUT=0123456789abcdef
  run deploy_eval 'step_1_snapshot_image' --yes
  assert_success
  assert_equal "$(fake_calls_of docker | grep '^docker tag')" "docker tag img123 schulerpark-app:rollback-20261027-000000-01234567"
  assert_file_contains "$STATE" '^ROLLBACK_TAG=schulerpark-app:rollback-20261027-000000-01234567$'
  assert_file_contains "$STATE" '^PREV_IMAGE_ID=img123$'
}

@test "snapshot without an existing image records an empty PREV_IMAGE_ID" {
  export FAKE_DOCKER_IMAGES_STDOUT="" FAKE_GIT_REV_PARSE_STDOUT=abc
  run deploy_eval 'step_1_snapshot_image' --yes
  assert_success
  assert_output --partial "no existing schulerpark-app:latest image"
  assert_file_contains "$STATE" "^PREV_IMAGE_ID=''$"
  assert_equal "$(fake_calls_of docker | grep -c '^docker tag' || true)" "0"
}

@test "snapshot aborts when the rollback tag is missing after tagging" {
  export FAKE_DOCKER_IMAGES_STDOUT=img123 FAKE_GIT_REV_PARSE_STDOUT=abc FAKE_DOCKER_IMAGE_RC=1
  run deploy_eval 'step_1_snapshot_image' --yes
  assert_failure
  assert_output --partial "docker tag claimed success"
}

# ─── Step 2 ──

fake_resources() { # root_kb docker_kb mem_avail_kb load ncpu
  export FAKE_DF_ROOT_KB="$1" FAKE_DF_DOCKER_KB="$2" FAKE_NPROC_STDOUT="$5"
  fake_cmd nproc
  printf 'MemTotal: 16000000 kB\nMemAvailable: %s kB\n' "$3" > "$BATS_TEST_TMPDIR/meminfo"
  printf '%s 0.5 0.5 1/100 1\n' "$4" > "$BATS_TEST_TMPDIR/loadavg"
  cat > "$FAKE_BIN/df" <<'EOF'
#!/usr/bin/env bash
for last; do :; done
echo Avail
if [[ $last == /var/lib/docker ]]; then echo "$FAKE_DF_DOCKER_KB"; else echo "$FAKE_DF_ROOT_KB"; fi
EOF
  local real_awk; real_awk="$(command -v awk)"
  cat > "$FAKE_BIN/awk" <<EOF
#!/usr/bin/env bash
args=()
for a in "\$@"; do
  case "\$a" in
    /proc/meminfo) args+=("$BATS_TEST_TMPDIR/meminfo") ;;
    /proc/loadavg) args+=("$BATS_TEST_TMPDIR/loadavg") ;;
    *) args+=("\$a") ;;
  esac
done
exec "$real_awk" "\${args[@]}"
EOF
  chmod +x "$FAKE_BIN/df" "$FAKE_BIN/awk"
}

@test "resource check passes silently with ample resources" {
  fake_resources 52428800 52428800 4096000 1.0 4
  run deploy_eval 'step_2_resource_check'
  assert_success
  assert_output --partial "/ free:           50.0 GB"
  refute_output --partial "WARN"
}

@test "resource check aborts below the root disk hard limit" {
  fake_resources 1992294 52428800 4096000 1.0 4
  run deploy_eval 'step_2_resource_check' --yes
  assert_failure
  assert_output --partial "ERROR: / has only 1.9 GB free"
  assert_output --partial "Resource hard limits violated"
}

@test "resource check aborts below the memory hard limit" {
  fake_resources 52428800 52428800 716800 1.0 4
  run deploy_eval 'step_2_resource_check' --yes
  assert_failure
  assert_output --partial "MemAvailable 700 MB (< 800 MB)"
}

@test "resource check warns at exactly the root hard limit and high load" {
  fake_resources 2097152 52428800 4096000 10 4
  DEPLOY_INPUT='n\n' run deploy_eval 'step_2_resource_check'
  assert_failure
  refute_output --partial "ERROR"
  assert_output --partial "WARN: / has 2.0 GB free (< 5 GB recommended)."
  assert_output --partial "WARN: Per-cpu load 2.50 (> 2.0)"
  assert_output --partial "Cancelled at resource check"
}

# ─── Step 5 ──

FAKE_COMPOSE_FN='docker() { local IFS=" "; echo "docker $*" >> "$FAKE_CALLS"
  case "$*" in
    *"ls -t"*) printf "%b" "${LS_OUT-}" ;;
    *" test -s "*) return "${TEST_RC:-0}" ;;
    *" du -h "*) printf "12K\t/backups/x\n" ;;
    *"gunzip -t"*) return "${GUNZIP_RC:-0}" ;;
  esac; }'

@test "backup records the newest dump file" {
  export LS_OUT='/backups/louise_20261027_000000.sql.gz\r\n'
  DEPLOY_INPUT='y\n' run deploy_eval "$FAKE_COMPOSE_FN; step_5_backup" --yes
  assert_success
  assert_output --partial "Backup OK: /backups/louise_20261027_000000.sql.gz (12K)"
  assert_file_contains "$STATE" '^BACKUP_FILE=louise_20261027_000000.sql.gz$'
  run fake_calls_of docker
  assert_line --partial "exec -T db-backup /usr/local/bin/db-backup.sh"
}

@test "backup always prompts, even with --yes" {
  DEPLOY_INPUT='n\n' run deploy_eval "$FAKE_COMPOSE_FN; step_5_backup" --yes
  assert_failure
  assert_output --partial "Cancelled at backup"
  assert_equal "$(fake_calls_of docker)" ""
}

@test "backup aborts when no dump is found" {
  export LS_OUT=''
  DEPLOY_INPUT='y\n' run deploy_eval "$FAKE_COMPOSE_FN; step_5_backup"
  assert_failure
  assert_output --partial "Could not locate a new backup (louise_*.sql.gz)"
}

@test "backup aborts on an empty dump" {
  export LS_OUT='/backups/louise_1.sql.gz\n' TEST_RC=1
  DEPLOY_INPUT='y\n' run deploy_eval "$FAKE_COMPOSE_FN; step_5_backup"
  assert_failure
  assert_output --partial "Backup file /backups/louise_1.sql.gz is empty or missing"
}

# ─── wait_for_health ──

@test "wait_for_health returns once curl succeeds" {
  run deploy_eval 'wait_for_health'
  assert_success
  assert_output --partial "App is healthy"
  run fake_calls_of curl
  assert_output "curl -fsS -k --noproxy * --max-time 5 https://localhost/api/health"
}

@test "wait_for_health times out and dumps app logs" {
  export FAKE_CURL_RC=7 FAKE_TICKS="$BATS_TEST_TMPDIR/ticks"
  cat > "$FAKE_BIN/date" <<'EOF'
#!/usr/bin/env bash
if [[ "$1" == "+%s" ]]; then
  n=$(cat "$FAKE_TICKS" 2>/dev/null || echo 0); echo $((n + 100)) > "$FAKE_TICKS"; echo $((1793059200 + n))
else echo 00:00:00; fi
EOF
  run deploy_eval 'wait_for_health'
  assert_failure
  assert_output --partial "Healthcheck timed out after 180s"
  assert_equal "$(fake_calls_of curl | wc -l)" "1"
  run fake_calls_of docker
  assert_line --partial "logs --tail=120 app"
}

# ─── Step 8 ──

@test "cleanup keeps the two newest rollback tags" {
  export FAKE_DOCKER_IMAGES_STDOUT=$'2026-10-01 10:00:00 +0000 UTC\tschulerpark-app:rollback-a\n2026-10-10 10:00:00 +0000 UTC\tschulerpark-app:rollback-b\n2026-10-20 10:00:00 +0000 UTC\tschulerpark-app:rollback-c\n2026-09-01 10:00:00 +0000 UTC\tschulerpark-app:rollback-d'
  run deploy_eval 'step_8_cleanup' --yes
  assert_success
  run fake_calls_of docker
  assert_line "docker rmi schulerpark-app:rollback-a"
  assert_line "docker rmi schulerpark-app:rollback-d"
  refute_line "docker rmi schulerpark-app:rollback-b"
  refute_line "docker rmi schulerpark-app:rollback-c"
  assert_line "docker builder prune -f --max-used-space 5GB"
}

@test "cleanup declined removes nothing" {
  export FAKE_DOCKER_IMAGES_STDOUT=$'2026-10-01\tschulerpark-app:rollback-a\n2026-10-02\tschulerpark-app:rollback-b\n2026-10-03\tschulerpark-app:rollback-c'
  DEPLOY_INPUT='n\n' run deploy_eval 'step_8_cleanup'
  assert_success
  assert_output --partial "Skipped cleanup"
  run fake_calls_of docker
  refute_line --partial "docker rmi"
  refute_line --partial "prune"
}

# ─── Rollback ──

@test "rollback_code_only needs a ROLLBACK_TAG" {
  run deploy_eval 'rollback_code_only'
  assert_failure
  assert_output --partial "no ROLLBACK_TAG in state"
}

@test "rollback_code_and_db needs a BACKUP_FILE" {
  run deploy_eval 'rollback_code_and_db'
  assert_failure
  assert_output --partial "BACKUP_FILE missing from state"
}

@test "DB restore refuses a corrupt backup before dropping the schema" {
  export GUNZIP_RC=1
  DEPLOY_INPUT='restore\n' run deploy_eval "$FAKE_COMPOSE_FN; BACKUP_FILE=louise_1.sql.gz; rollback_code_and_db"
  assert_failure
  assert_output --partial "Backup file is corrupt"
  run fake_calls_of docker
  refute_line --partial "DROP SCHEMA"
  refute_line --partial " stop app"
}

@test "--rollback without a state file aborts and releases the lock" {
  run deploy_eval 'main' --rollback
  assert_failure
  assert_output --partial "No state file at $STATE"
  assert_file_not_exists "$DEPLOY_LOCK"
}

@test "--rollback refuses to source a world-readable state file" {
  write_state "touch '$BATS_TEST_TMPDIR/pwned'"
  chmod 644 "$STATE"
  run deploy_eval 'main' --rollback
  assert_failure
  assert_output --partial "group/world accessible"
  assert_file_not_exists "$BATS_TEST_TMPDIR/pwned"
}

@test "--rollback is cancelled without the exact phrase" {
  write_state "ROLLBACK_TAG=schulerpark-app:rollback-x"
  DEPLOY_INPUT='yes\n' run deploy_eval 'main' --rollback
  assert_failure
  assert_output --partial "ROLLBACK_TAG = schulerpark-app:rollback-x"
  assert_output --partial "Cancelled rollback"
  assert_equal "$(fake_calls_of docker)" ""
}

@test "--rollback retags the saved image and recreates the app" {
  write_state "ROLLBACK_TAG=schulerpark-app:rollback-x" "BACKUP_FILE=louise_1.sql.gz"
  DEPLOY_INPUT='rollback\nn\n' run deploy_eval 'main' --rollback
  assert_success
  assert_output --partial "Code rollback complete"
  run fake_calls_of docker
  assert_line "docker tag schulerpark-app:rollback-x schulerpark-app:latest"
  assert_line "docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --no-build app"
  refute_line --partial "psql"
}

@test "--cleanup prunes images but builder cache only when confirmed" {
  DEPLOY_INPUT='y\nn\n' run deploy_eval 'main' --cleanup --yes
  assert_success
  run fake_calls_of docker
  assert_line "docker system prune -a -f"
  refute_line --partial "builder prune"
}

@test "--cleanup declined prunes nothing" {
  DEPLOY_INPUT='n\n' run deploy_eval 'main' --cleanup
  assert_failure
  assert_output --partial "Cancelled at docker system prune"
  run fake_calls_of docker
  refute_line --partial "prune"
}
