/*
 PRISM ERP - Proliferation data-quality audit
 Read-only queries. Run against the production PostgreSQL database before release.
 No data is modified by this script.
*/

-- 1. Detailed entries with an invalid or implausible proliferation date.
SELECT
    g."Id",
    g."ProjectId",
    p."Name" AS "ProjectName",
    p."CaseFileNumber" AS "ProjectCode",
    g."Source",
    g."ProliferationDate",
    g."UnitName",
    g."Quantity",
    g."ApprovalStatus",
    g."Remarks",
    g."LastUpdatedOnUtc"
FROM "ProliferationGranular" AS g
LEFT JOIN "Projects" AS p ON p."Id" = g."ProjectId"
WHERE g."ProliferationDate" < DATE '2000-01-01'
   OR g."ProliferationDate" > (CURRENT_DATE + INTERVAL '30 days')::date
ORDER BY g."ProliferationDate", p."Name", g."Id";

-- 2. Annual records with an invalid year.
SELECT
    y."Id",
    y."ProjectId",
    p."Name" AS "ProjectName",
    p."CaseFileNumber" AS "ProjectCode",
    y."Source",
    y."Year",
    y."TotalQuantity",
    y."ApprovalStatus",
    y."Remarks",
    y."LastUpdatedOnUtc"
FROM "ProliferationYearly" AS y
LEFT JOIN "Projects" AS p ON p."Id" = y."ProjectId"
WHERE y."Year" < 2000 OR y."Year" > 3000
ORDER BY y."Year", p."Name", y."Id";

-- 3. More than one approved annual row for the same project/source/year.
SELECT
    y."ProjectId",
    p."Name" AS "ProjectName",
    y."Source",
    y."Year",
    COUNT(*) AS "ApprovedRowCount",
    SUM(y."TotalQuantity") AS "CombinedQuantity",
    STRING_AGG(y."Id"::text, ', ' ORDER BY y."Id"::text) AS "RecordIds"
FROM "ProliferationYearly" AS y
LEFT JOIN "Projects" AS p ON p."Id" = y."ProjectId"
WHERE y."ApprovalStatus" = 1
GROUP BY y."ProjectId", p."Name", y."Source", y."Year"
HAVING COUNT(*) > 1
ORDER BY p."Name", y."Source", y."Year";

-- 4. Invalid quantities or unsupported source values.
SELECT 'Detailed' AS "RecordType", g."Id", g."ProjectId", g."Source", g."Quantity", g."ProliferationDate"::text AS "Period"
FROM "ProliferationGranular" AS g
WHERE g."Quantity" <= 0 OR g."Source" NOT IN (1, 2)
UNION ALL
SELECT 'Annual', y."Id", y."ProjectId", y."Source", y."TotalQuantity", y."Year"::text
FROM "ProliferationYearly" AS y
WHERE y."TotalQuantity" < 0 OR y."Source" NOT IN (1, 2)
ORDER BY "RecordType", "ProjectId", "Period";

/*
Correction procedure
--------------------
1. Verify every flagged row against the original return/source document.
2. Correct one identified record at a time through the PRISM Manage Records page where possible.
3. If direct SQL correction is unavoidable, take a database backup, use an explicit transaction,
   update only the verified record ID, and document the authority/source in the record remarks
   and organisational audit record.
4. Re-run this script and confirm that all four result sets are empty before release.

Do not infer or bulk-replace an invalid year (for example, year 24) without verifying the source record.
*/
