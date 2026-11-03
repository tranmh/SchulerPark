#!/usr/bin/env bats
# Smoke tests for the shared doubles in test_helper.bash: they prove the fakes,
# the clock and the fixtures work with the real scripts.

setup() {
  load test_helper
  common_setup
}

@test "fake_cmd records calls and answers with the first matching rule" {
  stub_response docker 'images -q *' 0 'sha256:abc'
  stub_response docker '*' 3 'other'

  run docker images -q schulerpark-app:latest
  assert_success
  assert_output 'sha256:abc'

  run docker ps
  assert_failure 3
  assert_output 'other'

  assert_called docker 'images -q schulerpark-app:latest'
  refute_called docker 'rmi*'
}

@test "fake_clock starts at the fixed epoch and advances on every call" {
  fake_clock

  run date +%Y%m%d_%H%M%S
  assert_output '20261103_083000'
  run date +%s
  assert_output "$((FAKE_CLOCK_EPOCH_DEFAULT + 1))"
}

@test "db-backup.sh with fake pg_dump writes an owner-only gzip of the seed dump" {
  fake_clock
  fake_pg_dump
  backup_env

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial 'Backup complete: louise_20261103_083000.sql.gz'

  local dump="$BACKUP_DIR/louise_20261103_083000.sql.gz"
  assert_file_exists "$dump"
  assert_equal "$(stat -c '%a' "$dump")" "600"
  assert_equal "$(stat -c '%a' "$BACKUP_DIR")" "700"
  run diff <(gzip -dc "$dump") "$SEEDS_DIR/louise_seed.sql"
  assert_success
  assert_called pg_dump-env 'PGHOST=db PGDATABASE=louise PGUSER=louise'
}

@test "db-backup.sh pipes through openssl when BACKUP_PASSPHRASE is set" {
  fake_clock
  fake_pg_dump
  fake_openssl
  backup_env
  export BACKUP_PASSPHRASE=fixture-passphrase

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success

  local dump="$BACKUP_DIR/louise_20261103_083000.sql.gz.enc"
  assert_file_exists "$dump"
  assert_file_not_exists "$BACKUP_DIR/louise_20261103_083000.sql.gz"
  assert_equal "$(head -n1 "$dump")" "$FAKE_ENC_MARKER"
  assert_called openssl 'enc -aes-256-cbc -pbkdf2 -pass env:BACKUP_PASSPHRASE'
  assert_called openssl-env 'passphrase-set=yes'
}

@test "make_backup_file ages dumps so retention removes only the old ones" {
  fake_clock
  fake_pg_dump
  backup_env
  make_backup_file louise_20260901_020000.sql.gz 40
  make_backup_file schulerpark_20250101_020000.sql.gz 400
  make_backup_file louise_20261101_020000.sql.gz 2

  run "$SCRIPTS_DIR/db-backup.sh"
  assert_success
  assert_output --partial 'Cleaned up 2 backup(s) older than 30 days.'
  assert_file_not_exists "$BACKUP_DIR/louise_20260901_020000.sql.gz"
  assert_file_not_exists "$BACKUP_DIR/schulerpark_20250101_020000.sql.gz"
  assert_file_exists "$BACKUP_DIR/louise_20261101_020000.sql.gz"
}

@test "run_deploy_fn calls a single deploy.sh function with overrides and stdin" {
  run_deploy_fn ASSUME_YES=1 confirm "Proceed?"
  assert_success
  assert_output --partial 'AUTO-YES: Proceed?'

  run_deploy_fn confirm "Proceed?" <<< "n"
  assert_failure
}

@test "make_default_state_file passes verify_state_file and round-trips via source" {
  deploy_functions_copy
  make_default_state_file

  run_deploy_fn verify_state_file
  assert_success

  run bash -c 'source "$1"; echo "$ROLLBACK_TAG|$BACKUP_FILE"' _ "$DEPLOY_STATE"
  assert_output "$DEFAULT_ROLLBACK_TAG|$DEFAULT_BACKUP_FILE"
}

@test "deploy.sh copy runs preflight against fake git and docker" {
  fake_docker
  fake_git master
  deploy_functions_copy
  make_state_file

  run_deploy_fn step_0_preflight
  assert_success
  assert_output --partial "Deploying working tree at commit $DEFAULT_DEPLOY_SHA"
  assert_called docker 'info'
  assert_file_contains "$DEPLOY_STATE" "^DEPLOY_SHA=$DEFAULT_DEPLOY_SHA\$"
}
