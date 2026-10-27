# Shared test doubles for the scripts in scripts/.
#
# Usage (in setup()):
#   load 'test_helper/fakes'
#   fakes_setup                  # PATH folder of fakes + call log in $BATS_TEST_TMPDIR
#   fake_cmd docker git curl     # recording fakes for external commands
#   fake_clock                   # deterministic `date` (FAKE_EPOCH, UTC)
#   fake_pg_dump                 # pg_dump that prints a seed file
#
# Every fake appends one line "<name> <args...>" to $FAKE_CALLS. Behaviour of a
# recording fake is steered per command through environment variables, with the
# name upper-cased and '-' turned into '_':
#   FAKE_<NAME>_STDOUT   printed on stdout (default: nothing)
#   FAKE_<NAME>_RC       exit status (default: 0)
# Per-subcommand overrides use the first argument too, e.g. for `docker images`:
#   FAKE_DOCKER_IMAGES_STDOUT / FAKE_DOCKER_IMAGES_RC

FAKES_SEEDS_DIR="$BATS_TEST_DIRNAME/seeds"

# 2026-10-27 00:00:00 UTC
FAKE_EPOCH_DEFAULT=1793059200

fakes_setup() {
  FAKE_BIN="$BATS_TEST_TMPDIR/fakebin"
  FAKE_CALLS="$BATS_TEST_TMPDIR/calls.log"
  mkdir -p "$FAKE_BIN"
  : > "$FAKE_CALLS"
  export FAKE_BIN FAKE_CALLS
  export PATH="$FAKE_BIN:$PATH"
}

# fake_cmd name... — install recording fakes on the fake PATH folder.
fake_cmd() {
  local name
  for name in "$@"; do
    cat > "$FAKE_BIN/$name" <<'EOF'
#!/usr/bin/env bash
name="$(basename "$0")"
echo "$name $*" >> "$FAKE_CALLS"
var="FAKE_$(printf '%s' "$name" | tr 'a-z-' 'A-Z_')"
sub=""
[[ $# -gt 0 ]] && sub="${var}_$(printf '%s' "$1" | tr 'a-z-' 'A-Z_' | tr -c 'A-Z0-9_' '_')"
out_var="${var}_STDOUT"; rc_var="${var}_RC"
if [[ -n "$sub" && -n "${!sub_out:-}" ]]; then :; fi
sub_out="${sub}_STDOUT"; sub_rc="${sub}_RC"
if [[ -n "$sub" && -n "${!sub_out+x}" ]]; then printf '%s\n' "${!sub_out}"
elif [[ -n "${!out_var:-}" ]]; then printf '%s\n' "${!out_var}"
fi
if [[ -n "$sub" && -n "${!sub_rc:-}" ]]; then exit "${!sub_rc}"; fi
exit "${!rc_var:-0}"
EOF
    chmod +x "$FAKE_BIN/$name"
  done
}

# fake_clock [epoch] — `date` always answers for a fixed instant in UTC.
# `date +FORMAT` and `date '+FORMAT'` work as with the real date.
fake_clock() {
  export FAKE_EPOCH="${1:-${FAKE_EPOCH:-$FAKE_EPOCH_DEFAULT}}"
  local real_date
  real_date="$(command -v date)"
  cat > "$FAKE_BIN/date" <<EOF
#!/usr/bin/env bash
TZ=UTC exec "$real_date" -d "@\${FAKE_EPOCH}" "\$@"
EOF
  chmod +x "$FAKE_BIN/date"
}

# fake_pg_dump [seed-file] — pg_dump prints the seed (default: the minimal
# LouisE dump in seeds/) and records the call; FAKE_PG_DUMP_RC makes it fail.
fake_pg_dump() {
  export FAKE_PG_DUMP_SEED="${1:-$FAKES_SEEDS_DIR/louise_minimal.sql}"
  cat > "$FAKE_BIN/pg_dump" <<'EOF'
#!/usr/bin/env bash
echo "pg_dump $*" >> "$FAKE_CALLS"
cat "$FAKE_PG_DUMP_SEED"
exit "${FAKE_PG_DUMP_RC:-0}"
EOF
  chmod +x "$FAKE_BIN/pg_dump"
}

# fake_calls_of name — the recorded argument lines of one fake.
fake_calls_of() {
  grep -E "^$1( |$)" "$FAKE_CALLS" || true
}

# make_backup_file dir name [touch-date] — an old/new dump for retention tests.
make_backup_file() {
  local dir="$1" name="$2" when="${3:-}"
  mkdir -p "$dir"
  printf 'dump\n' | gzip > "$dir/$name"
  [[ -n "$when" ]] && touch -d "$when" "$dir/$name"
  return 0
}
