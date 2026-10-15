#!/usr/bin/env bash
set -euo pipefail

# Use trusted dumps. See docs/disaster-recovery.md before use.
PGBIN="${PGBIN:-/usr/bin}"
HOST="${PGHOST:-localhost}"
PORT="${PGPORT:-5432}"
DBNAME="${PGDATABASE:-ProjectManagement}"
USER="${PGRESTORE_USER:-postgres}"
DUMP_FILE="${PM_DUMP_FILE:-}"
fail() { printf '%s\n' "$*" >&2; exit 1; }
[[ -n "$DUMP_FILE" && -f "$DUMP_FILE" ]] || fail "PM_DUMP_FILE must point to an existing dump file"
for tool in dropdb createdb pg_restore psql; do
  [[ -x "$PGBIN/$tool" || -x "$PGBIN/$tool.exe" ]] || fail "Required PostgreSQL utility not found: $tool"
done
[[ -n "$DBNAME" && ! "$DBNAME" =~ [[:cntrl:]] ]] || fail "Database name must contain no control characters"
ADMIN_DB=postgres
[[ "$DBNAME" != postgres ]] || ADMIN_DB=template1
# Bounded ASCII names avoid truncation even for long Unicode target names.
STEM="${DBNAME:0:16}"
STEM="${STEM//[^a-zA-Z0-9_]/_}"
STAMP="$(date -u +%Y%m%d%H%M%S)"
printf -v TOKEN '%04x%04x%04x%04x' "$RANDOM" "$RANDOM" "$RANDOM" "$RANDOM"
STAGE="${STEM}_stage_${STAMP}_${TOKEN}"
PREVIOUS="${STEM}_previous_${STAMP}_${TOKEN}"
export PRISM_RESTORE_TARGET="$DBNAME" PRISM_RESTORE_STAGE="$STAGE" PRISM_RESTORE_PREVIOUS="$PREVIOUS"
export PRISM_RESTORE_TARGET_OID=0 PRISM_RESTORE_STAGE_OID=0 PGCONNECT_TIMEOUT=10 PGCLIENTENCODING=UTF8
utf8_hex() {
  # Byte-wise encoding using Bash builtins; no xxd, Python or other runtime.
  local LC_ALL=C value="$1" hex="" byte code i
  for (( i=0; i<${#value}; i++ )); do
    # Some libc builds expose high bytes as surrogate character values.
    # Normalize to one byte before formatting, rather than emitting four hex digits.
    printf -v code '%d' "'${value:i:1}"
    printf -v byte '%02X' "$((code & 255))"
    hex+="$byte"
  done
  printf '%s' "$hex"
}
export PRISM_RESTORE_TARGET_HEX
PRISM_RESTORE_TARGET_HEX=$(utf8_hex "$DBNAME")
unset PGSERVICE PGSERVICEFILE PGHOSTADDR
COMMON=("--host=$HOST" "--port=$PORT" "--username=$USER" --no-password)
SQL_PREFIX=$(cat <<'SQL'
\getenv target_hex PRISM_RESTORE_TARGET_HEX
SELECT convert_from(decode(:'target_hex', 'hex'), 'UTF8') AS target \gset
\getenv stage PRISM_RESTORE_STAGE
\getenv previous PRISM_RESTORE_PREVIOUS
\getenv target_oid PRISM_RESTORE_TARGET_OID
\getenv stage_oid PRISM_RESTORE_STAGE_OID
SQL
)
SQL_PREFLIGHT=$(cat <<'SQL'
SELECT (octet_length(:'target') BETWEEN 1 AND current_setting('max_identifier_length')::int AND NOT EXISTS (SELECT FROM pg_database WHERE datname = :'target' AND datistemplate)) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Invalid target name or template database target.'; END $$;
\endif
SELECT (NOT EXISTS (SELECT FROM pg_database WHERE datname IN (:'stage', :'previous'))) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Generated database name already exists; nothing was changed.'; END $$;
\endif
SELECT 'PRISM_OID=' || COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0);
SQL
)
SQL_IDENTITY=$(cat <<'SQL'
SELECT 'PRISM_OID=' || oid::text FROM pg_database WHERE datname = current_database();
SQL
)
SQL_VALIDATE=$(cat <<'SQL'
SELECT (EXISTS (
 SELECT FROM pg_namespace n WHERE n.nspname NOT LIKE 'pg\_%' ESCAPE '\' AND n.nspname <> 'information_schema'
 AND (SELECT count(*) FROM pg_class c WHERE c.relnamespace = n.oid
      AND c.relkind IN ('r', 'p') AND c.relname IN ('Projects', 'AspNetUsers', '__EFMigrationsHistory')) = 3
)) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Restored PRISM schema is missing Projects, AspNetUsers or __EFMigrationsHistory in one user schema.'; END $$;
\endif
SELECT 'PRISM_OID=' || oid::text FROM pg_database WHERE datname = current_database();
SQL
)
SQL_CUTOVER=$(cat <<'SQL'
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '15s';
SELECT pg_advisory_xact_lock(726747384021::bigint);
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Database identities changed before cutover; inspect retained databases.'; END $$;
\endif
SELECT (NOT EXISTS (SELECT FROM pg_stat_activity WHERE datid IN (:'target_oid'::oid, :'stage_oid'::oid))
 AND NOT EXISTS (SELECT FROM pg_prepared_xacts WHERE database IN (:'target', :'stage', :'previous'))) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Active sessions or prepared transactions prevent cutover; stop the app and close connections, then retry.'; END $$;
\endif
SELECT (:'target_oid'::bigint <> 0) AS had_target \gset
\if :had_target
ALTER DATABASE :"target" RENAME TO :"previous";
\endif
ALTER DATABASE :"stage" RENAME TO :"target";
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Cutover identity check failed; transaction will roll back.'; END $$;
\endif
COMMIT;
SQL
)
SQL_ROLLBACK=$(cat <<'SQL'
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '15s';
SELECT pg_advisory_xact_lock(726747384021::bigint);
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Cannot safely roll back changed database identities; retain both copies for inspection.'; END $$;
\endif
SELECT (NOT EXISTS (SELECT FROM pg_stat_activity WHERE datid IN (:'target_oid'::oid, :'stage_oid'::oid))
 AND NOT EXISTS (SELECT FROM pg_prepared_xacts WHERE database IN (:'target', :'stage', :'previous'))) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Active sessions or prepared transactions prevent cutover; stop the app and close connections, then retry.'; END $$;
\endif
ALTER DATABASE :"target" RENAME TO :"stage";
SELECT (:'target_oid'::bigint <> 0) AS had_target \gset
\if :had_target
ALTER DATABASE :"previous" RENAME TO :"target";
\endif
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Rollback identity check failed; transaction will roll back.'; END $$;
\endif
COMMIT;
SQL
)

run_native() {
  local rc=0 label="$1"; shift
  "$@" || rc=$?
  (( rc == 0 )) || { printf '%s failed with exit code %s.\n' "$label" "$rc" >&2; return "$rc"; }
}
run_sql() {
  local phase="$1" database="$2" sql="$3" output rc=0 line marker="" count=0 hex i uri='postgresql:///'
  hex=$(utf8_hex "$database")
  for (( i=0; i<${#hex}; i+=2 )); do uri+="%${hex:i:2}"; done
  # The pipe is checked with pipefail; native stderr is never redirected away.
  output=$(printf '%s\n%s\n%s\n' "-- prism-restore: $phase" "$SQL_PREFIX" "$sql" |
    PGDATABASE="$database" "$PGBIN/psql" "${COMMON[@]}" "--dbname=$uri" -X --set=ON_ERROR_STOP=1 --tuples-only --no-align --file=-) || rc=$?
  printf '%s\n' "$output" >&2
  (( rc == 0 )) || { printf 'psql (%s) failed with exit code %s.\n' "$phase" "$rc" >&2; return "$rc"; }
  case "$phase" in
    preflight|identity|validate|activated)
      while IFS= read -r line; do
        line="${line%$'\r'}"
        if [[ "$line" =~ ^PRISM_OID=([0-9]+)$ ]]; then marker="${BASH_REMATCH[1]}"; count=$((count + 1)); fi
      done <<< "$output"
      (( count == 1 )) || { printf 'psql (%s) did not return one database identity.\n' "$phase" >&2; return 1; }
      printf '%s\n' "$marker"
      ;;
  esac
}
STAGE_OWNED=0 CUTOVER_ATTEMPTED=0 CUTOVER_COMMITTED=0
finish() {
  local rc=$?
  trap - EXIT
  if (( rc != 0 )); then
    if (( CUTOVER_COMMITTED )); then
      if run_sql rollback "$ADMIN_DB" "$SQL_ROLLBACK"; then
        printf 'Activation verification failed; original name restored. Staged copy retained: %s\n' "$STAGE" >&2
      else
        printf 'Automatic rollback failed. Inspect target=%s stage=%s previous=%s\n' "$DBNAME" "$STAGE" "$PREVIOUS" >&2
      fi
    elif (( CUTOVER_ATTEMPTED )); then
      printf 'Cutover failed or commit outcome unknown; both renames use one transaction. Inspect target=%s stage=%s previous=%s; no databases were deleted.\n' "$DBNAME" "$STAGE" "$PREVIOUS" >&2
    elif (( STAGE_OWNED )); then
      # DROP cannot share an OID-check transaction. A check/drop pair races with
      # administrative renames, so preserve the copy for deliberate cleanup.
      printf 'Automatic staging cleanup deferred for identity safety. Retained staging database: %s (OID %s). Inspect before deliberate cleanup.\n' "$STAGE" "$PRISM_RESTORE_STAGE_OID" >&2
    else
      printf 'If created, staging database was retained for inspection: %s\n' "$STAGE" >&2
    fi
  fi
  exit "$rc"
}
trap finish EXIT
run_native 'pg_restore (list)' "$PGBIN/pg_restore" --list -- "$DUMP_FILE"
PRISM_RESTORE_TARGET_OID=$(run_sql preflight "$ADMIN_DB" "$SQL_PREFLIGHT")
printf 'Staging database: %s; retained-name reservation: %s\n' "$STAGE" "$PREVIOUS"
run_native createdb "$PGBIN/createdb" "${COMMON[@]}" "--maintenance-db=$ADMIN_DB" --template=template0 -- "$STAGE"
PRISM_RESTORE_STAGE_OID=$(run_sql identity "$STAGE" "$SQL_IDENTITY")
STAGE_OWNED=1
run_native pg_restore "$PGBIN/pg_restore" "${COMMON[@]}" "--dbname=$STAGE" --single-transaction --exit-on-error -- "$DUMP_FILE"
VALIDATED_OID=$(run_sql validate "$STAGE" "$SQL_VALIDATE")
[[ "$VALIDATED_OID" == "$PRISM_RESTORE_STAGE_OID" ]] || fail "Staging database identity changed"
CUTOVER_ATTEMPTED=1
run_sql cutover "$ADMIN_DB" "$SQL_CUTOVER"
CUTOVER_COMMITTED=1
ACTIVATED_OID=$(run_sql activated "$DBNAME" "$SQL_VALIDATE")
[[ "$ACTIVATED_OID" == "$PRISM_RESTORE_STAGE_OID" ]] || fail "Activated database identity changed"
printf 'Database restore completed: %s from %s\n' "$DBNAME" "$DUMP_FILE"
if [[ "$PRISM_RESTORE_TARGET_OID" != 0 ]]; then printf 'Retained previous database: %s\n' "$PREVIOUS"
else printf 'No previous target database existed.\n'; fi
