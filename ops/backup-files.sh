#!/usr/bin/env bash
set -euo pipefail

# ==== Parameters ==============================================================
DATA_ROOT="${PM_DATA_ROOT:-/srv/projectmanagement-data}"
BACKUP_ROOT="${PM_FILE_BACKUP_ROOT:-/mnt/pm-backups/files}"
# ============================================================================

# ==== Validation ==============================================================
if [[ ! -d "$DATA_ROOT" ]]; then
  echo "Data root not found: $DATA_ROOT" >&2
  exit 1
fi
mkdir -p "$BACKUP_ROOT"
# ============================================================================

# ==== Execution ===============================================================
TIMESTAMP=$(date +"%Y%m%d-%H%M%S")
DEST="$BACKUP_ROOT/Data-$TIMESTAMP"
rsync -a --delete "$DATA_ROOT/" "$DEST/"
# ============================================================================

echo "File backup complete: $DEST"
