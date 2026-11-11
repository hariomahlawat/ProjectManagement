-- Removes the demo MOLTI projects and SRE projects added by molti-sre-demo-seed.sql from a LOCAL
-- DEVELOPMENT database: every MOLTI or SRE project whose title starts with "Demo · ", with its
-- stages, years, remarks and attachment records (they cascade). Attachment files on disk, if any
-- were uploaded to a demo project, are left under the upload root. Never run against production.
--
-- Run: psql -h localhost -p 5433 -U postgres -d ProjectManagement -v ON_ERROR_STOP=1 -f tools/dev-data/molti-sre-demo-remove.sql
DELETE FROM "ItProjects"
WHERE "Module" IN ('MOLTI', 'SRE') AND "Title" LIKE 'Demo · %';
SELECT "Module", count(*) AS projects FROM "ItProjects" WHERE NOT "IsDeleted" GROUP BY 1 ORDER BY 1;
