#!/usr/bin/env bash
set -euo pipefail

# ==== Parameters ==============================================================
PGBIN="${PGBIN:-/usr/bin}"
HOST="${PGHOST:-localhost}"
PORT="${PGPORT:-5432}"
DBNAME="${PGDATABASE:-ProjectManagement}"
USER="${PGUSER:-pm_backup}"
BACKUP_DIR="${PM_BACKUP_DIR:-/srv/projectmanagement-data/backups/db}"
# ============================================================================

# ==== Preparation ============================================================
mkdir -p "$BACKUP_DIR"
TIMESTAMP=$(date +"%Y%m%d-%H%M%S")
BACKUP_FILE="$BACKUP_DIR/${DBNAME}-${TIMESTAMP}.dump"
# ============================================================================

# ==== Execution ==============================================================
"$PGBIN/pg_dump" \
  --format=custom \
  --host="$HOST" \
  --port="$PORT" \
  --username="$USER" \
  --file="$BACKUP_FILE" \
  "$DBNAME"
# ============================================================================

# ==== Output =================================================================
echo "Database backup created: $BACKUP_FILE"
# ============================================================================
