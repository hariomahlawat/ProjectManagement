using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Enforces the final ToT domain boundary for Repeat Build projects.
/// Repeat Builds are not ToT entities, so historical ToT state is removed once while
/// project-level documents/photos are preserved with only their ToT association cleared.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216210000_RemoveTotDataFromRepeatBuildProjects")]
public sealed class RemoveTotDataFromRepeatBuildProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Repeat Build ToT cleanup is supported only on the PostgreSQL production provider.");
        }

        // Published project documents and project photos are reusable project assets. Preserve them and
        // remove only their ToT relationship before deleting the parent ProjectTot rows.
        migrationBuilder.Sql(
            """
            UPDATE "ProjectDocuments" AS d
            SET "TotId" = NULL
            FROM "ProjectTots" AS t
            INNER JOIN "Projects" AS p ON p."Id" = t."ProjectId"
            WHERE d."TotId" = t."Id"
              AND p."IsBuild" = TRUE;

            UPDATE "ProjectPhotos" AS ph
            SET "TotId" = NULL
            FROM "ProjectTots" AS t
            INNER JOIN "Projects" AS p ON p."Id" = t."ProjectId"
            WHERE ph."TotId" = t."Id"
              AND p."IsBuild" = TRUE;
            """);

        // ToT-specific document requests are workflow data, not reusable project assets. Delete
        // them outright so a pending ToT upload request cannot reappear as a generic document
        // approval after its ToT association is removed. Published documents themselves are kept.
        migrationBuilder.Sql(
            """
            DELETE FROM "ProjectDocumentRequests" AS r
            USING "ProjectTots" AS t, "Projects" AS p
            WHERE r."TotId" = t."Id"
              AND t."ProjectId" = p."Id"
              AND p."IsBuild" = TRUE;
            """);

        // ToT-scoped remarks are ToT data rather than general project remarks. Remark audits and
        // mentions are removed automatically through their configured cascade foreign keys.
        migrationBuilder.Sql(
            """
            DELETE FROM "Remarks" AS r
            USING "Projects" AS p
            WHERE r."ProjectId" = p."Id"
              AND p."IsBuild" = TRUE
              AND r."Scope" = 'TransferOfTechnology';
            """);

        // Requests are independent of ProjectTot and must be deleted explicitly.
        migrationBuilder.Sql(
            """
            DELETE FROM "ProjectTotRequests" AS r
            USING "Projects" AS p
            WHERE r."ProjectId" = p."Id"
              AND p."IsBuild" = TRUE;
            """);

        // Delete the ToT state last, after child associations have been detached.
        migrationBuilder.Sql(
            """
            DELETE FROM "ProjectTots" AS t
            USING "Projects" AS p
            WHERE t."ProjectId" = p."Id"
              AND p."IsBuild" = TRUE;
            """);

        // Search V2 is derived state. Remove stale ToT projections/work items immediately instead
        // of waiting for the next full reconciliation. The guards keep the cleanup safe on an
        // environment where Search V2 tables have not yet been created for any reason.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('"SearchEntries"') IS NOT NULL THEN
                    DELETE FROM "SearchEntries" AS s
                    USING "Projects" AS p
                    WHERE s."EntityType" = 'ProjectTot'
                      AND s."EntityKey" = p."Id"::text
                      AND p."IsBuild" = TRUE;
                END IF;

                IF to_regclass('"SearchIndexWorkItems"') IS NOT NULL THEN
                    DELETE FROM "SearchIndexWorkItems" AS w
                    USING "Projects" AS p
                    WHERE w."EntityType" = 'ProjectTot'
                      AND w."EntityKey" = p."Id"::text
                      AND p."IsBuild" = TRUE;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally irreversible: removed ToT data belonged to projects for which ToT is not
        // a valid domain concept. A rollback must not recreate synthetic or incomplete ToT state.
    }
}
