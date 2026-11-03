# Shared test doubles and fixtures for the scripts in scripts/.
#
# Usage in a .bats file:
#
#   setup() {
#     load test_helper
#     common_setup
#   }
#
# common_setup loads bats-support/bats-assert/bats-file, puts a folder of fake
# commands in front of PATH and points every fake at $FAKE_DIR (all under
# $BATS_TEST_TMPDIR). Nothing touches the repository, $HOME or the network.

SCRIPTS_DIR="$(cd "$BATS_TEST_DIRNAME/../scripts" && pwd)"
SEEDS_DIR="$BATS_TEST_DIRNAME/seeds"

common_setup() {
  bats_load_library bats-support
  bats_load_library bats-assert
  bats_load_library bats-file

  export FAKE_DIR="$BATS_TEST_TMPDIR/fakes"
  export FAKE_BIN="$FAKE_DIR/bin"
  export FAKE_LOG="$FAKE_DIR/calls.log"
  mkdir -p "$FAKE_BIN"
  : > "$FAKE_LOG"
  export PATH="$FAKE_BIN:$PATH"
}

# ─── Generic fake commands ──────────────────────────────────────────────────
#
# fake_cmd NAME
#   Installs a fake NAME on PATH. Every call appends "NAME args..." to
#   $FAKE_LOG. Responses are scripted with stub_response; a call matching no
#   rule prints nothing and exits 0 (or runs the fallback set with
#   fake_fallback).
fake_cmd() {
  local name="$1"
  cat > "$FAKE_BIN/$name" <<'FAKE'
#!/usr/bin/env bash
name="${0##*/}"
printf '%s\n' "$name $*" >> "$FAKE_LOG"
rules="$FAKE_DIR/$name.rules"
if [[ -f "$rules" ]]; then
  while IFS=$'\t' read -r n rc pattern; do
    # shellcheck disable=SC2053
    if [[ "$*" == $pattern ]]; then
      cat "$FAKE_DIR/$name.out.$n"
      exit "$rc"
    fi
  done < "$rules"
fi
if [[ -f "$FAKE_DIR/$name.fallback" ]]; then
  # shellcheck disable=SC1090
  source "$FAKE_DIR/$name.fallback"
fi
exit 0
FAKE
  chmod +x "$FAKE_BIN/$name"
}

# stub_response NAME 'GLOB' RC [STDOUT]
#   When the fake NAME is called with arguments matching GLOB (bash pattern
#   against "$*"), print STDOUT and exit RC. The first matching rule wins, so
#   add specific rules before broad ones. Installs the fake if needed.
stub_response() {
  local name="$1" pattern="$2" rc="$3" out="${4-}"
  [[ -x "$FAKE_BIN/$name" ]] || fake_cmd "$name"
  local rules="$FAKE_DIR/$name.rules" n=0
  [[ -f "$rules" ]] && n=$(wc -l < "$rules")
  if [[ -n "$out" ]]; then
    printf '%s\n' "$out" > "$FAKE_DIR/$name.out.$n"
  else
    : > "$FAKE_DIR/$name.out.$n"
  fi
  printf '%s\t%s\t%s\n' "$n" "$rc" "$pattern" >> "$rules"
}

# fake_fallback NAME 'BASH CODE'
#   Code run (sourced, "$@" = the call's arguments) when no rule matches.
fake_fallback() {
  local name="$1" code="$2"
  [[ -x "$FAKE_BIN/$name" ]] || fake_cmd "$name"
  printf '%s\n' "$code" > "$FAKE_DIR/$name.fallback"
}

# fake_calls [NAME]  — print the recorded calls (of NAME only, if given).
fake_calls() {
  if [[ -n "${1-}" ]]; then
    grep -E "^$1( |\$)" "$FAKE_LOG" || true
  else
    cat "$FAKE_LOG"
  fi
}

# assert_called NAME 'GLOB' / refute_called NAME 'GLOB'
#   GLOB is matched against the arguments of each recorded call of NAME.
assert_called() {
  local name="$1" pattern="$2" line
  while IFS= read -r line; do
    # shellcheck disable=SC2053
    [[ "${line#"$name "}" == $pattern ]] && return 0
  done < <(fake_calls "$name")
  fail "expected a call: $name $pattern
recorded calls:
$(fake_calls)"
}

refute_called() {
  local name="$1" pattern="$2" line
  while IFS= read -r line; do
    # shellcheck disable=SC2053
    if [[ "${line#"$name "}" == $pattern ]]; then
      fail "unexpected call: $line"
      return 1
    fi
  done < <(fake_calls "$name")
  return 0
}

# fake_noop NAME — a fake that does nothing and is not logged.
fake_noop() {
  printf '#!/usr/bin/env bash\nexit 0\n' > "$FAKE_BIN/$1"
  chmod +x "$FAKE_BIN/$1"
}

# ─── Clock ──────────────────────────────────────────────────────────────────
#
# fake_clock [EPOCH] [STEP]
#   Replaces `date` with a deterministic clock. It starts at EPOCH
#   (default 2026-11-03 08:30:00 UTC) and advances STEP seconds (default 1)
#   on every call, so deadline loops (`while [[ $(date +%s) -lt $deadline ]]`)
#   end without real waiting. Formatting is done by the real date in UTC.
#   Also installs a no-op `sleep`.
FAKE_CLOCK_EPOCH_DEFAULT=1793694600   # 2026-11-03T08:30:00Z

fake_clock() {
  local epoch="${1:-$FAKE_CLOCK_EPOCH_DEFAULT}" step="${2:-1}"
  local real_date
  real_date="$(PATH="${PATH#"$FAKE_BIN:"}" command -v date)"
  printf '%s\n' "$epoch" > "$FAKE_DIR/clock.now"
  cat > "$FAKE_BIN/date" <<FAKE
#!/usr/bin/env bash
now=\$(cat "\$FAKE_DIR/clock.now")
echo \$(( now + $step )) > "\$FAKE_DIR/clock.now"
TZ=UTC exec "$real_date" -d "@\$now" "\$@"
FAKE
  chmod +x "$FAKE_BIN/date"
  fake_noop sleep
}

# ─── PostgreSQL / backup boundary (scripts/db-backup.sh) ────────────────────
#
# fake_pg_dump [SQL_FILE]
#   pg_dump that prints SQL_FILE (default: the seed dump) and logs the call
#   plus the libpq env vars it saw ("pg_dump-env PGHOST=... ").
fake_pg_dump() {
  local sql="${1:-$SEEDS_DIR/louise_seed.sql}"
  fake_fallback pg_dump "printf 'pg_dump-env PGHOST=%s PGDATABASE=%s PGUSER=%s\n' \"\${PGHOST-}\" \"\${PGDATABASE-}\" \"\${PGUSER-}\" >> \"\$FAKE_LOG\"; cat '$sql'"
}

# fake_pg_dump_failing [RC] — pg_dump that writes partial output then fails.
fake_pg_dump_failing() {
  local rc="${1:-1}"
  fake_fallback pg_dump "echo '-- partial'; echo 'pg_dump: error: connection refused' >&2; exit $rc"
}

# fake_openssl
#   openssl stand-in: logs the call and "encrypts" stdin by prefixing the
#   $FAKE_ENC_MARKER line, so tests can tell an encrypted dump from plain gzip.
#   Logs "openssl-env passphrase-set=yes" when BACKUP_PASSPHRASE reached it.
FAKE_ENC_MARKER="FAKE-AES-256-CBC"
fake_openssl() {
  fake_fallback openssl "printf 'openssl-env passphrase-set=%s\n' \"\${BACKUP_PASSPHRASE:+yes}\" >> \"\$FAKE_LOG\"; echo '$FAKE_ENC_MARKER'; cat"
}

# backup_env — env vars for running db-backup.sh inside $BATS_TEST_TMPDIR.
backup_env() {
  export BACKUP_DIR="$BATS_TEST_TMPDIR/backups"
  export PGHOST=db PGPORT=5432 PGUSER=louise PGDATABASE=louise PGPASSWORD=not-a-secret
  unset BACKUP_PASSPHRASE BACKUP_RETENTION_DAYS
}

# make_backup_file NAME AGE_DAYS [DIR]
#   Creates a gzip dump NAME in DIR (default $BACKUP_DIR) whose mtime is
#   AGE_DAYS days in the past (relative to now, as `find -mtime` compares).
make_backup_file() {
  local name="$1" age="$2" dir="${3:-$BACKUP_DIR}"
  mkdir -p "$dir"
  printf 'old dump %s\n' "$name" | gzip -c > "$dir/$name"
  touch -d "-${age} days" "$dir/$name"
}

# ─── Deploy boundary (scripts/deploy.sh) ────────────────────────────────────
#
# deploy.sh hard-codes REPO (the prod checkout) and LOCK_FILE in /tmp and runs
# main at the end. deploy_script_copy writes a copy to $BATS_TEST_TMPDIR with
# REPO pointing at a scratch dir and the lock file in the tmp dir;
# deploy_functions_copy also drops the trailing `main "$@"` so single
# functions can be called with run_deploy_fn.
export_deploy_paths() {
  export DEPLOY_REPO="$BATS_TEST_TMPDIR/prod-repo"
  export DEPLOY_LOCK="$BATS_TEST_TMPDIR/schulerpark-deploy.lock"
  export DEPLOY_STATE="$DEPLOY_REPO/.deploy-state"
  mkdir -p "$DEPLOY_REPO"
}

deploy_script_copy() {
  export_deploy_paths
  export DEPLOY_SCRIPT="$BATS_TEST_TMPDIR/deploy.sh"
  sed -e "s#^REPO=.*#REPO=\"$DEPLOY_REPO\"#" \
      -e "s#^LOCK_FILE=.*#LOCK_FILE=\"$DEPLOY_LOCK\"#" \
      "$SCRIPTS_DIR/deploy.sh" > "$DEPLOY_SCRIPT"
  chmod +x "$DEPLOY_SCRIPT"
}

deploy_functions_copy() {
  deploy_script_copy
  export DEPLOY_FUNCS="$BATS_TEST_TMPDIR/deploy-functions.sh"
  sed -e '/^main "\$@"$/d' "$DEPLOY_SCRIPT" > "$DEPLOY_FUNCS"
}

# run_deploy_fn [VAR=VALUE...] FUNCTION [ARGS...]
#   Runs one deploy.sh function in a fresh bash from $DEPLOY_REPO (the script's
#   set -euo pipefail, IFS and EXIT trap stay out of the bats process).
#   Leading VAR=VALUE words override the script's globals after it is loaded,
#   e.g. run_deploy_fn ASSUME_YES=1 confirm "ok?".
#   Answers to prompts go on stdin: run_deploy_fn confirm "ok?" <<< "y"
run_deploy_fn() {
  local overrides=()
  while [[ "${1-}" =~ ^[A-Z_][A-Z0-9_]*= ]]; do overrides+=("$1"); shift; done
  [[ -n "${DEPLOY_FUNCS-}" ]] || deploy_functions_copy
  run bash -c '
    funcs="$1"; n="$2"; shift 2
    args=("$@"); set --   # the script parses its flags from "$@" on load
    source "$funcs"
    for ((i = 0; i < n; i++)); do printf -v "${args[0]%%=*}" "%s" "${args[0]#*=}"; args=("${args[@]:1}"); done
    cd "$REPO"
    "${args[@]}"
  ' _ "$DEPLOY_FUNCS" "${#overrides[@]}" "${overrides[@]}" "$@"
}

# fake_git [BRANCH] [SHA] [PORCELAIN]
#   git for preflight/snapshot: branch, HEAD sha and `status` output.
DEFAULT_DEPLOY_SHA=0123456789abcdef0123456789abcdef01234567
fake_git() {
  local branch="${1-master}" sha="${2:-$DEFAULT_DEPLOY_SHA}" porcelain="${3-}"
  stub_response git 'symbolic-ref*' 0 "$branch"
  stub_response git 'rev-parse HEAD' 0 "$sha"
  stub_response git 'status --porcelain' 0 "$porcelain"
  stub_response git 'status --short' 0 "$porcelain"
}

# fake_curl [RC] — every curl call exits RC (default 0 = healthy).
fake_curl() {
  stub_response curl '*' "${1:-0}" ""
}

# fake_docker — docker that accepts everything (exit 0, no output); script
# specific answers with stub_response docker 'images -q*' 0 'sha256:...'.
fake_docker() {
  fake_cmd docker
}

# make_state_file [KEY=VALUE...]
#   Writes $DEPLOY_STATE the way record_state does (printf %q, mode 600).
make_state_file() {
  [[ -n "${DEPLOY_STATE-}" ]] || export_deploy_paths
  ( umask 077; : > "$DEPLOY_STATE" )
  local kv
  for kv in "$@"; do
    printf '%s=%q\n' "${kv%%=*}" "${kv#*=}" >> "$DEPLOY_STATE"
  done
  chmod 600 "$DEPLOY_STATE"
}

# make_default_state_file — the state of a deploy that got past step 5.
DEFAULT_ROLLBACK_TAG=schulerpark-app:rollback-20261103-083000-fedcba98
DEFAULT_BACKUP_FILE=louise_20261103_083000.sql.gz
make_default_state_file() {
  make_state_file \
    DEPLOY_SHA="$DEFAULT_DEPLOY_SHA" \
    PREV_SHA=fedcba9876543210fedcba9876543210fedcba98 \
    PREV_IMAGE_ID=sha256deadbeef \
    ROLLBACK_TAG="$DEFAULT_ROLLBACK_TAG" \
    BACKUP_FILE="$DEFAULT_BACKUP_FILE"
}
