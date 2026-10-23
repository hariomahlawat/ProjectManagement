-- Removes the Command desk demo data added by command-desk-demo-seed.sql (LOCAL DEVELOPMENT
-- DATABASES ONLY). Only records labelled "Demo · " (IPR numbers "DEMO-") are touched; their stages,
-- ToT, counters and other child rows go with them. Safe to run when there is nothing to remove.
--
-- Usage:
--   psql -v ON_ERROR_STOP=1 -d <development database> -f tools/dev-data/command-desk-demo-remove.sql
\encoding UTF8

BEGIN;

-- Proliferation entries have no foreign key to Projects, so they go first, by project.
DELETE FROM "ProliferationGranular"
WHERE "ProjectId" IN (SELECT "Id" FROM "Projects" WHERE "Name" LIKE 'Demo · %');

DELETE FROM "IprRecords" WHERE "IprFilingNumber" LIKE 'DEMO-%' AND "Title" LIKE 'Demo · %';
DELETE FROM "FfcRecords"
WHERE "OverallRemarks" LIKE 'Demo · %' OR "OverallRemarks" = 'Demo record for the Command desk';

-- Stages, ToT, plans, remarks and other project children cascade with the project.
DELETE FROM "Projects" WHERE "Name" LIKE 'Demo · %';

DELETE FROM "ActionTasks" WHERE "Title" LIKE 'Demo · %';
DELETE FROM "Visits" WHERE "VisitorName" LIKE 'Demo · %';
DELETE FROM "Trainings" WHERE "Notes" LIKE 'Demo · %';
DELETE FROM "Activities" WHERE "Title" LIKE 'Demo · %';

COMMIT;
