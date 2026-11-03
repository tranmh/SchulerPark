#!/usr/bin/env bats
# Unit tests for scripts/db-backup.sh: pg_dump, openssl and the clock are
# faked (test_helper.bash); gzip, find and du are the real tools.

setup() {
  load test_helper
  common_setup
  fake_clock
  fake_pg_dump
  backup_env
}

# age_file PATH SECONDS — set the mtime of PATH to SECONDS before the real now
# (epoch based, so no DST shift moves a file across a -mtime day boundary).
age_file() {
  local path="$1" secs="$2"
  touch -d "@$(( EPOCHSECONDS - secs ))" "$path"
}

# old_dump NAME AGE_DAYS — a dump in $BACKUP_DIR AGE_DAYS days plus one hour old.
old_dump() {
  mkdir -p "$BACKUP_DIR"
  printf 'old dump %s\n' "$1" | gzip -c > "$BACKUP_DIR/$1"
  age_file "$BACKUP_DIR/$1" $(( $2 * 86400 + 3600 ))
}

@test "logs start, completion with size and done, all timestamped" {
  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_line '[2026-11-03 08:30:01] Starting backup: louise_20261103_083000.sql.gz'
  assert_line --regexp '^\[2026-11-03 08:30:02\] Backup complete: louise_20261103_083000\.sql\.gz \([0-9.,]+[KMG]?\)$'
  assert_line '[2026-11-03 08:30:03] Done.'
  refute_output --partial 'Cleaned up'
}

@test "creates a missing nested BACKUP_DIR with owner-only permissions" {
  export BACKUP_DIR="$BATS_TEST_TMPDIR/a/b/backups"

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_dir_exists "$BACKUP_DIR"
  assert_equal "$(stat -c '%a' "$BACKUP_DIR")" "700"
  assert_file_exists "$BACKUP_DIR/louise_20261103_083000.sql.gz"
}

@test "locks down an existing world-readable BACKUP_DIR" {
  mkdir -p "$BACKUP_DIR"
  command chmod 755 "$BACKUP_DIR"

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_equal "$(stat -c '%a' "$BACKUP_DIR")" "700"
}

@test "the plain dump is valid gzip of the pg_dump output" {
  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success

  run gzip -t "$BACKUP_DIR/louise_20261103_083000.sql.gz"
  assert_success
  run bash -c 'gzip -dc "$1" | grep -c "CREATE SCHEMA IF NOT EXISTS shell_fixture"' _ "$BACKUP_DIR/louise_20261103_083000.sql.gz"
  assert_output '1'
}

@test "an empty BACKUP_PASSPHRASE writes an unencrypted dump" {
  fake_openssl
  export BACKUP_PASSPHRASE=""

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_file_exists "$BACKUP_DIR/louise_20261103_083000.sql.gz"
  assert_file_not_exists "$BACKUP_DIR/louise_20261103_083000.sql.gz.enc"
  refute_called openssl '*'
}

@test "the encrypted dump is owner-only and logged under its .enc name" {
  fake_openssl
  export BACKUP_PASSPHRASE=fixture-passphrase

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial 'Backup complete: louise_20261103_083000.sql.gz.enc ('
  assert_equal "$(stat -c '%a' "$BACKUP_DIR/louise_20261103_083000.sql.gz.enc")" "600"
}

@test "a failing pg_dump fails the backup and never reports completion" {
  fake_pg_dump_failing 2

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_failure
  assert_output --partial 'pg_dump: error: connection refused'
  refute_output --partial 'Backup complete'
  refute_output --partial 'Done.'
}

@test "a failing openssl fails the encrypted backup" {
  fake_fallback openssl 'cat > /dev/null; echo "bad decrypt" >&2; exit 1'
  export BACKUP_PASSPHRASE=fixture-passphrase

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_failure
  refute_output --partial 'Backup complete'
}

@test "a BACKUP_DIR that cannot be chmodded only warns and the backup still runs" {
  stub_response chmod '*' 1

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial "WARN: could not chmod 700 $BACKUP_DIR (bind mount owned by another user?)"
  assert_output --partial 'Backup complete: louise_20261103_083000.sql.gz'
}

@test "default retention is 30 days: a 30-day-old dump is kept, a 31-day-old one deleted" {
  old_dump louise_20261004_020000.sql.gz 30
  old_dump louise_20261003_020000.sql.gz 31

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial 'Cleaned up 1 backup(s) older than 30 days.'
  assert_file_exists "$BACKUP_DIR/louise_20261004_020000.sql.gz"
  assert_file_not_exists "$BACKUP_DIR/louise_20261003_020000.sql.gz"
}

@test "BACKUP_RETENTION_DAYS overrides the retention window" {
  export BACKUP_RETENTION_DAYS=7
  old_dump louise_20261027_020000.sql.gz 7
  old_dump louise_20261020_020000.sql.gz 14

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial 'Cleaned up 1 backup(s) older than 7 days.'
  assert_file_exists "$BACKUP_DIR/louise_20261027_020000.sql.gz"
  assert_file_not_exists "$BACKUP_DIR/louise_20261020_020000.sql.gz"
}

@test "retention of 0 days never deletes the dump just written" {
  export BACKUP_RETENTION_DAYS=0
  old_dump louise_20261101_020000.sql.gz 2

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial 'Cleaned up 1 backup(s) older than 0 days.'
  assert_file_exists "$BACKUP_DIR/louise_20261103_083000.sql.gz"
  assert_file_not_exists "$BACKUP_DIR/louise_20261101_020000.sql.gz"
}

@test "retention reaps old encrypted and legacy dumps but leaves other files alone" {
  old_dump louise_20260901_020000.sql.gz.enc 60
  old_dump schulerpark_20250101_020000.sql.gz 400
  old_dump notes.sql.gz 400
  old_dump schulerpark_20250101_020000.sql.gz.enc 400
  mkdir -p "$BACKUP_DIR/louise_dir.sql.gz"
  age_file "$BACKUP_DIR/louise_dir.sql.gz" $(( 400 * 86400 ))

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial 'Cleaned up 2 backup(s) older than 30 days.'
  assert_file_not_exists "$BACKUP_DIR/louise_20260901_020000.sql.gz.enc"
  assert_file_not_exists "$BACKUP_DIR/schulerpark_20250101_020000.sql.gz"
  assert_file_exists "$BACKUP_DIR/notes.sql.gz"
  assert_file_exists "$BACKUP_DIR/schulerpark_20250101_020000.sql.gz.enc"
  assert_dir_exists "$BACKUP_DIR/louise_dir.sql.gz"
}

@test "a non-numeric BACKUP_RETENTION_DAYS fails instead of deleting anything" {
  export BACKUP_RETENTION_DAYS=abc
  old_dump louise_20260901_020000.sql.gz 60

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_failure
  assert_file_exists "$BACKUP_DIR/louise_20260901_020000.sql.gz"
  refute_output --partial 'Done.'
}
