#!/usr/bin/env bash
set -euo pipefail

# ==== Parameters ==============================================================
BACKUP_SOURCE="${PM_FILE_BACKUP_SOURCE:-}"
DATA_ROOT="${PM_DATA_ROOT:-/srv/projectmanagement-data}"
# ============================================================================

# ==== Validation ==============================================================
if [[ -z "$BACKUP_SOURCE" ]]; then
  echo "PM_FILE_BACKUP_SOURCE must point to a directory containing a DataRoot backup" >&2
  exit 1
fi

if [[ ! -d "$BACKUP_SOURCE" ]]; then
  echo "Backup source not found: $BACKUP_SOURCE" >&2
  exit 1
fi
mkdir -p "$DATA_ROOT"
# ============================================================================

# ==== Execution ===============================================================
rsync -a --delete "$BACKUP_SOURCE/" "$DATA_ROOT/"
# ============================================================================

echo "Files restored from $BACKUP_SOURCE into $DATA_ROOT"
