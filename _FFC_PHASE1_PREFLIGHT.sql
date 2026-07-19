-- FFC Phase 1 database preflight (read-only)
-- Run against the PRISM PostgreSQL database before applying migration
-- 20261201260000_HardenFfcFoundation.

-- 1. Duplicate active country/year records. This must return no rows.
SELECT
    r."CountryId",
    c."Name" AS "Country",
    r."Year",
    COUNT(*) AS "ActiveRecordCount",
    ARRAY_AGG(r."Id" ORDER BY r."Id") AS "RecordIds"
FROM "FfcRecords" r
JOIN "FfcCountries" c ON c."Id" = r."CountryId"
WHERE r."IsDeleted" = FALSE
GROUP BY r."CountryId", c."Name", r."Year"
HAVING COUNT(*) > 1
ORDER BY c."Name", r."Year";

-- 2. Duplicate links to the same core PRISM project within one FFC record.
-- This must return no rows.
SELECT
    fp."FfcRecordId",
    fp."LinkedProjectId",
    COUNT(*) AS "LinkCount",
    ARRAY_AGG(fp."Id" ORDER BY fp."Id") AS "FfcProjectIds"
FROM "FfcProjects" fp
WHERE fp."LinkedProjectId" IS NOT NULL
GROUP BY fp."FfcRecordId", fp."LinkedProjectId"
HAVING COUNT(*) > 1
ORDER BY fp."FfcRecordId", fp."LinkedProjectId";

-- 3. Installation dates before delivery dates. This must return no rows.
SELECT
    fp."Id" AS "FfcProjectId",
    fp."FfcRecordId",
    fp."Name",
    fp."DeliveredOn",
    fp."InstalledOn"
FROM "FfcProjects" fp
WHERE fp."DeliveredOn" IS NOT NULL
  AND fp."InstalledOn" IS NOT NULL
  AND fp."InstalledOn" < fp."DeliveredOn"
ORDER BY fp."FfcRecordId", fp."Id";

-- 4. Installed rows not marked delivered. These are repaired automatically by
-- the migration by setting IsDelivered = TRUE and using InstalledOn as the
-- delivery date only when DeliveredOn is currently NULL.
SELECT
    fp."Id" AS "FfcProjectId",
    fp."FfcRecordId",
    fp."Name",
    fp."DeliveredOn",
    fp."InstalledOn"
FROM "FfcProjects" fp
WHERE fp."IsInstalled" = TRUE
  AND fp."IsDelivered" = FALSE
ORDER BY fp."FfcRecordId", fp."Id";
