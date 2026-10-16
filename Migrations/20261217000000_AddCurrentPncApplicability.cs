using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261217000000_AddCurrentPncApplicability")]
public sealed class AddCurrentPncApplicability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "PncApplicable", table: "Projects", type: "boolean",
            nullable: false, defaultValue: true);

        // Only the active approved publication is historical authority. Missing,
        // orphaned, draft/pending pointers retain the previous policy default true.
        migrationBuilder.Sql("""
            UPDATE "Projects" AS project
            SET "PncApplicable" = plan."PncApplicable"
            FROM "PlanVersions" AS plan
            WHERE plan."ProjectId" = project."Id"
              AND plan."VersionNo" = project."ActivePlanVersionNo"
              AND plan."Status" = 'Approved';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "PncApplicable", table: "Projects");
}
