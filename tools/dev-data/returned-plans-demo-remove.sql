-- Removes the "Returned to you" demo data added by returned-plans-demo-seed.sql (LOCAL
-- DEVELOPMENT DATABASES ONLY). Only projects named "Demo · Return …" are touched; their stages,
-- plan versions and approval logs go with them. Safe to run when there is nothing to remove.
--
-- Usage:
--   psql -v ON_ERROR_STOP=1 -d <development database> -f tools/dev-data/returned-plans-demo-remove.sql
\encoding UTF8

BEGIN;
DELETE FROM "Projects" WHERE "Name" LIKE 'Demo · Return %';
COMMIT;
