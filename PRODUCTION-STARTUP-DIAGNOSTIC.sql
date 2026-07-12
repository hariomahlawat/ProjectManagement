-- PRISM ERP production startup diagnostic (read-only)
-- Run against the exact PostgreSQL database used by the IIS application.

SELECT
    current_database() AS database_name,
    current_schema() AS schema_name,
    current_user AS database_user,
    inet_server_addr() AS server_address,
    inet_server_port() AS server_port,
    version() AS postgres_version;

SELECT
    'ApplicationDbContext' AS migration_set,
    COUNT(*) AS applied_count,
    MAX("MigrationId") AS latest_applied
FROM "__EFMigrationsHistory"
UNION ALL
SELECT
    'MediaLibraryDbContext',
    COUNT(*),
    MAX("MigrationId")
FROM "__EFMigrationsHistory_MediaLibrary";

SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory_MediaLibrary"
ORDER BY "MigrationId";

SELECT
    column_name,
    data_type,
    is_nullable,
    column_default
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND table_name = 'ProjectStages'
  AND column_name IN ('ActualStart', 'CompletedOn', 'RequiresBackfill')
ORDER BY column_name;

SELECT
    constraint_row.conname AS constraint_name,
    pg_get_constraintdef(constraint_row.oid) AS constraint_definition
FROM pg_constraint AS constraint_row
WHERE constraint_row.conrelid = to_regclass(
        format('%I.%I', current_schema(), 'ProjectStages'))
  AND constraint_row.conname = 'CK_ProjectStages_CompletedHasDate';

SELECT
    COUNT(*) FILTER (
        WHERE "Status" = 'Completed'
          AND "CompletedOn" IS NULL
          AND COALESCE("RequiresBackfill", FALSE) = FALSE
    ) AS completed_without_date_or_backfill,
    COUNT(*) FILTER (
        WHERE "Status" = 'Completed'
          AND "ActualStart" IS NULL
          AND "RequiresBackfill" IS NULL
    ) AS legacy_nullable_backfill_rows,
    COUNT(*) FILTER (WHERE "RequiresBackfill" IS NULL) AS null_backfill_rows
FROM "ProjectStages";

SELECT
    to_regclass(format('%I.%I', current_schema(), 'MediaAssets')) AS media_assets,
    to_regclass(format('%I.%I', current_schema(), 'MediaFaces')) AS media_faces,
    to_regclass(format('%I.%I', current_schema(), 'MediaPersons')) AS media_persons,
    to_regclass(format('%I.%I', current_schema(), '__EFMigrationsHistory_MediaLibrary')) AS media_history;
