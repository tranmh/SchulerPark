#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────
# PostgreSQL backup script
#
# Designed to run inside the db-backup Docker container.
# Can also run standalone with proper env vars.
#
# Environment variables:
#   PGHOST       — PostgreSQL host (default: db)
#   PGPORT       — PostgreSQL port (default: 5432)
#   PGUSER       — PostgreSQL user (default: louise)
#   PGPASSWORD   — PostgreSQL password
#   PGDATABASE   — Database name (default: louise)
#   BACKUP_DIR   — Where to store dumps (default: /backups)
#   BACKUP_RETENTION_DAYS — Delete dumps older than N days (default: 30)
#   BACKUP_PASSPHRASE — If set, encrypt dumps with AES-256 (openssl enc).
#                       Decrypt: openssl enc -d -aes-256-cbc -pbkdf2 -pass env:BACKUP_PASSPHRASE -in f.sql.gz.enc | gunzip
# ──────────────────────────────────────────────────────────

set -euo pipefail

# Dumps contain PII (emails, names, plates) and credential hashes — never
# world-readable (owner-only files, and lock down the directory too).
umask 077

BACKUP_DIR="${BACKUP_DIR:-/backups}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
FILENAME="louise_${TIMESTAMP}.sql.gz"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR" 2>/dev/null || log "WARN: could not chmod 700 $BACKUP_DIR (bind mount owned by another user?)"

log "Starting backup: $FILENAME"
if [ -n "${BACKUP_PASSPHRASE:-}" ]; then
  FILENAME="${FILENAME}.enc"
  pg_dump | gzip | openssl enc -aes-256-cbc -pbkdf2 -pass env:BACKUP_PASSPHRASE \
    > "$BACKUP_DIR/$FILENAME"
else
  pg_dump | gzip > "$BACKUP_DIR/$FILENAME"
fi

SIZE=$(du -h "$BACKUP_DIR/$FILENAME" | cut -f1)
log "Backup complete: $FILENAME ($SIZE)"

# Clean up old backups. Match both the current `louise_` prefix and the legacy
# `schulerpark_` prefix (pre-rebrand) so old-named dumps are reaped too.
DELETED=$(find "$BACKUP_DIR" \( -name "louise_*.sql.gz" -o -name "louise_*.sql.gz.enc" -o -name "schulerpark_*.sql.gz" \) -type f -mtime +"$BACKUP_RETENTION_DAYS" -print -delete | wc -l)
if [ "$DELETED" -gt 0 ]; then
  log "Cleaned up $DELETED backup(s) older than $BACKUP_RETENTION_DAYS days."
fi

log "Done."
