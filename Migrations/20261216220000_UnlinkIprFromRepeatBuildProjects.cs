using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

/// <summary>
/// Removes invalid IPR ownership links from Repeat Build projects without deleting any IPR record.
/// The records become unassigned and are surfaced by the existing IPR project-linkage follow-up flow,
/// where they can be linked to the correct original development project when known.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261216220000_UnlinkIprFromRepeatBuildProjects")]
public sealed class UnlinkIprFromRepeatBuildProjects : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Repeat Build IPR-link cleanup is supported only on the PostgreSQL production provider.");
        }

        // Preserve the patent/copyright record, filing data, status, notes and attachments. Only the
        // invalid association to a Repeat Build project is removed. The Search V2 trigger on IprRecords
        // queues each changed IPR record automatically when Search V2 is installed.
        migrationBuilder.Sql(
            """
            UPDATE "IprRecords" AS i
            SET "ProjectId" = NULL
            FROM "Projects" AS p
            WHERE i."ProjectId" = p."Id"
              AND p."IsBuild" = TRUE;
            """);

        // Project search projections also contain an IPR summary. Re-index affected Repeat Build
        // projects so stale IPR summary terms disappear immediately after the association cleanup.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regprocedure('search_v2_enqueue(text,text)') IS NOT NULL THEN
                    PERFORM search_v2_enqueue('Project', p."Id"::text)
                    FROM "Projects" AS p
                    WHERE p."IsBuild" = TRUE;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally irreversible. The migration cannot infer which original project, if any,
        // should own an IPR record that had been incorrectly linked to a Repeat Build project.
    }
}
