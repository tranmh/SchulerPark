#!/usr/bin/env bats
# Unit tests for scripts/db-backup.sh (pg_dump and date are faked).

setup() {
  bats_load_library bats-support
  bats_load_library bats-assert
  bats_load_library bats-file
  load 'test_helper/fakes'
  fakes_setup
  fake_clock
  fake_pg_dump
  SCRIPT="$BATS_TEST_DIRNAME/../scripts/db-backup.sh"
  export BACKUP_DIR="$BATS_TEST_TMPDIR/backups"
  unset BACKUP_PASSPHRASE BACKUP_RETENTION_DAYS
}

@test "writes a gzipped dump named after the timestamp" {
  run bash "$SCRIPT"
  assert_success
  assert_file_exists "$BACKUP_DIR/louise_20261027_000000.sql.gz"
  gunzip -c "$BACKUP_DIR/louise_20261027_000000.sql.gz" | cmp - "$FAKE_PG_DUMP_SEED"
  assert_equal "$(fake_calls_of pg_dump | wc -l)" "1"
}

@test "logs start, completion with size and done" {
  run bash "$SCRIPT"
  assert_success
  assert_line "[2026-10-27 00:00:00] Starting backup: louise_20261027_000000.sql.gz"
  assert_line --regexp '^\[2026-10-27 00:00:00\] Backup complete: louise_20261027_000000\.sql\.gz \(.+\)$'
  assert_line "[2026-10-27 00:00:00] Done."
  refute_output --partial "Cleaned up"
}

@test "creates a missing nested backup directory owner-only" {
  export BACKUP_DIR="$BATS_TEST_TMPDIR/a/b/backups"
  run bash "$SCRIPT"
  assert_success
  assert_equal "$(stat -c '%a' "$BACKUP_DIR")" "700"
}

@test "locks down an existing world-readable directory and writes the dump 600" {
  mkdir -p "$BACKUP_DIR"
  chmod 755 "$BACKUP_DIR"
  run bash "$SCRIPT"
  assert_success
  assert_equal "$(stat -c '%a' "$BACKUP_DIR")" "700"
  assert_equal "$(stat -c '%a' "$BACKUP_DIR/louise_20261027_000000.sql.gz")" "600"
}

@test "encrypts the dump when BACKUP_PASSPHRASE is set" {
  command -v openssl >/dev/null || skip "openssl not installed in the runner"
  export BACKUP_PASSPHRASE="s3cret-test-pass"
  run bash "$SCRIPT"
  assert_success
  local f="$BACKUP_DIR/louise_20261027_000000.sql.gz.enc"
  assert_file_exists "$f"
  assert_file_not_exists "$BACKUP_DIR/louise_20261027_000000.sql.gz"
  assert_line --partial "Backup complete: louise_20261027_000000.sql.gz.enc"
  # the documented decrypt command restores the plain dump
  openssl enc -d -aes-256-cbc -pbkdf2 -pass env:BACKUP_PASSPHRASE -in "$f" | gunzip | cmp - "$FAKE_PG_DUMP_SEED"
  # and the file is not a plain gzip
  run gunzip -t "$f"
  assert_failure
}

@test "an empty BACKUP_PASSPHRASE writes an unencrypted dump" {
  export BACKUP_PASSPHRASE=""
  run bash "$SCRIPT"
  assert_success
  assert_file_exists "$BACKUP_DIR/louise_20261027_000000.sql.gz"
  assert_file_not_exists "$BACKUP_DIR/louise_20261027_000000.sql.gz.enc"
}

@test "fails when pg_dump fails" {
  export FAKE_PG_DUMP_RC=1
  run bash "$SCRIPT"
  assert_failure
  refute_output --partial "Backup complete"
  refute_output --partial "Done."
}

@test "deletes old current, encrypted and legacy dumps but keeps recent and unrelated files" {
  make_backup_file "$BACKUP_DIR" "louise_20200101_000000.sql.gz" "40 days ago"
  make_backup_file "$BACKUP_DIR" "louise_20200101_000000.sql.gz.enc" "40 days ago"
  make_backup_file "$BACKUP_DIR" "schulerpark_20200101_000000.sql.gz" "40 days ago"
  make_backup_file "$BACKUP_DIR" "louise_20261020_000000.sql.gz" "5 days ago"
  make_backup_file "$BACKUP_DIR" "other_20200101.sql.gz" "40 days ago"
  run bash "$SCRIPT"
  assert_success
  assert_file_not_exists "$BACKUP_DIR/louise_20200101_000000.sql.gz"
  assert_file_not_exists "$BACKUP_DIR/louise_20200101_000000.sql.gz.enc"
  assert_file_not_exists "$BACKUP_DIR/schulerpark_20200101_000000.sql.gz"
  assert_file_exists "$BACKUP_DIR/louise_20261020_000000.sql.gz"
  assert_file_exists "$BACKUP_DIR/other_20200101.sql.gz"
  assert_file_exists "$BACKUP_DIR/louise_20261027_000000.sql.gz"
  assert_line "[2026-10-27 00:00:00] Cleaned up 3 backup(s) older than 30 days."
}

@test "keeps a dump younger than the default 30 day retention" {
  make_backup_file "$BACKUP_DIR" "louise_20260930_000000.sql.gz" "29 days ago"
  run bash "$SCRIPT"
  assert_success
  assert_file_exists "$BACKUP_DIR/louise_20260930_000000.sql.gz"
  refute_output --partial "Cleaned up"
}

@test "honours a custom BACKUP_RETENTION_DAYS" {
  export BACKUP_RETENTION_DAYS=5
  make_backup_file "$BACKUP_DIR" "louise_20261018_000000.sql.gz" "8 days ago"
  make_backup_file "$BACKUP_DIR" "louise_20261025_000000.sql.gz" "2 days ago"
  run bash "$SCRIPT"
  assert_success
  assert_file_not_exists "$BACKUP_DIR/louise_20261018_000000.sql.gz"
  assert_file_exists "$BACKUP_DIR/louise_20261025_000000.sql.gz"
  assert_line --partial "Cleaned up 1 backup(s) older than 5 days."
}

@test "does not reap old directories that match the dump pattern" {
  mkdir -p "$BACKUP_DIR/louise_old.sql.gz"
  touch -d "40 days ago" "$BACKUP_DIR/louise_old.sql.gz"
  run bash "$SCRIPT"
  assert_success
  assert_dir_exists "$BACKUP_DIR/louise_old.sql.gz"
}
