#!/usr/bin/env bash
set -euo pipefail

# ==== Parameters ==============================================================
PGBIN="${PGBIN:-/usr/bin}"
HOST="${PGHOST:-localhost}"
PORT="${PGPORT:-5432}"
DBNAME="${PGDATABASE:-ProjectManagement}"
USER="${PGRESTORE_USER:-postgres}"
DUMP_FILE="${PM_DUMP_FILE:-}"
# ============================================================================

# ==== Validation ==============================================================
if [[ -z "$DUMP_FILE" ]]; then
  echo "PM_DUMP_FILE must point to a pg_dump custom-format file" >&2
  exit 1
fi

if [[ ! -f "$DUMP_FILE" ]]; then
  echo "Dump file not found: $DUMP_FILE" >&2
  exit 1
fi
# ============================================================================

# ==== Drop and recreate database =============================================
"$PGBIN/dropdb" --if-exists --host="$HOST" --port="$PORT" --username="$USER" "$DBNAME"
"$PGBIN/createdb" --host="$HOST" --port="$PORT" --username="$USER" "$DBNAME"
# ============================================================================

# ==== Restore ================================================================
"$PGBIN/pg_restore" \
  --host="$HOST" \
  --port="$PORT" \
  --username="$USER" \
  --dbname="$DBNAME" \
  --clean \
  --if-exists \
  "$DUMP_FILE"
# ============================================================================

echo "Database restored from $DUMP_FILE"
