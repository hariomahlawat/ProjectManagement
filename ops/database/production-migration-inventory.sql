-- PRISM ERP production migration and critical-schema inventory
-- Read-only. Run against the exact database used by the deployed IIS application.

SELECT
    current_database() AS database_name,
    current_schema() AS schema_name,
    current_user AS database_user,
    inet_server_addr() AS server_address,
    inet_server_port() AS server_port,
    version() AS postgres_version;

SELECT
    'ApplicationDbContext' AS migration_set,
    "MigrationId",
    "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

SELECT
    'MediaLibraryDbContext' AS migration_set,
    "MigrationId",
    "ProductVersion"
FROM "__EFMigrationsHistory_MediaLibrary"
ORDER BY "MigrationId";

SELECT
    'ApplicationDbContext' AS migration_set,
    COUNT(*) AS migration_count,
    MAX("MigrationId") AS latest_migration
FROM "__EFMigrationsHistory"
UNION ALL
SELECT
    'MediaLibraryDbContext',
    COUNT(*),
    MAX("MigrationId")
FROM "__EFMigrationsHistory_MediaLibrary";

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
    to_regclass(format('%I.%I', current_schema(), 'MediaAssets')) IS NOT NULL
        AS media_assets_present,
    to_regclass(format('%I.%I', current_schema(), 'MediaFaces')) IS NOT NULL
        AS media_faces_present,
    to_regclass(format('%I.%I', current_schema(), 'MediaPersons')) IS NOT NULL
        AS media_persons_present,
    to_regclass(format('%I.%I', current_schema(), 'MediaIdentityAudits')) IS NOT NULL
        AS media_identity_audits_present;

SELECT
    to_regprocedure(format(
        '%I.project_documents_build_search_vector(integer,text,text,integer,text)',
        current_schema())) IS NOT NULL AS project_search_builder_present,
    EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = current_schema()
          AND tablename = 'ProjectDocuments'
          AND indexname IN ('IX_ProjectDocuments_SearchVector', 'idx_project_documents_search')
    ) AS project_search_index_present;
