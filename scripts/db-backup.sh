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
#   PGUSER       — PostgreSQL user (default: schulerpark)
#   PGPASSWORD   — PostgreSQL password
#   PGDATABASE   — Database name (default: schulerpark)
#   BACKUP_DIR   — Where to store dumps (default: /backups)
#   BACKUP_RETENTION_DAYS — Delete dumps older than N days (default: 30)
# ──────────────────────────────────────────────────────────

set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/backups}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
FILENAME="schulerpark_${TIMESTAMP}.sql.gz"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*"; }

mkdir -p "$BACKUP_DIR"

log "Starting backup: $FILENAME"
pg_dump | gzip > "$BACKUP_DIR/$FILENAME"

SIZE=$(du -h "$BACKUP_DIR/$FILENAME" | cut -f1)
log "Backup complete: $FILENAME ($SIZE)"

# Clean up old backups
DELETED=$(find "$BACKUP_DIR" -name "schulerpark_*.sql.gz" -type f -mtime +"$BACKUP_RETENTION_DAYS" -print -delete | wc -l)
if [ "$DELETED" -gt 0 ]; then
  log "Cleaned up $DELETED backup(s) older than $BACKUP_RETENTION_DAYS days."
fi

log "Done."
