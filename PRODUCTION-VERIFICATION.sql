-- Read-only checks to run after the updated application starts successfully.

-- 1. The corrected constraint must mention CompletedOn and RequiresBackfill,
--    and must NOT require ActualStart.
SELECT pg_get_constraintdef(c.oid) AS project_stage_completion_rule
FROM pg_constraint AS c
WHERE c.conrelid = to_regclass(format('%I.%I', current_schema(), 'ProjectStages'))
  AND c.conname = 'CK_ProjectStages_CompletedHasDate';

-- 2. Confirm the reconciliation migration is recorded.
SELECT "MigrationId"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261201150000_ReconcileProjectStageCompletionConstraint';

-- 3. Confirm the latest deployed Media Library migration is recorded.
SELECT "MigrationId"
FROM "__EFMigrationsHistory_MediaLibrary"
WHERE "MigrationId" = '20260630113000_AddIdentityReferenceGovernance';

-- 4. Confirm critical date nullability.
SELECT column_name, is_nullable
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND table_name = 'ProjectStages'
  AND column_name IN ('ActualStart', 'CompletedOn', 'RequiresBackfill')
ORDER BY column_name;
