using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201090000_RemoveIndustryPartnerAssociationRole")]
    public partial class RemoveIndustryPartnerAssociationRole : Migration
    {
        // SECTION: Remove role column and rebuild uniqueness
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "IndustryPartnerId", "ProjectId"
                               ORDER BY "LinkedOnUtc" DESC, "Id" DESC
                           ) AS rn
                    FROM "IndustryPartnerProjectAssociations"
                    WHERE "IsActive" = TRUE
                )
                UPDATE "IndustryPartnerProjectAssociations"
                SET "IsActive" = FALSE,
                    "DeactivatedUtc" = COALESCE("DeactivatedUtc", NOW())
                WHERE "Id" IN (SELECT "Id" FROM ranked WHERE rn > 1);
            """);

            migrationBuilder.DropIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId_ProjectId_Role",
                table: "IndustryPartnerProjectAssociations");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "IndustryPartnerProjectAssociations");

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId_ProjectId",
                table: "IndustryPartnerProjectAssociations",
                columns: new[] { "IndustryPartnerId", "ProjectId" },
                unique: true,
                filter: "\"IsActive\" = true");
        }

        // SECTION: Restore role column and original uniqueness
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId_ProjectId",
                table: "IndustryPartnerProjectAssociations");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "IndustryPartnerProjectAssociations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartnerProjectAssociations_IndustryPartnerId_ProjectId_Role",
                table: "IndustryPartnerProjectAssociations",
                columns: new[] { "IndustryPartnerId", "ProjectId", "Role" },
                unique: true,
                filter: "\"IsActive\" = true");
        }
    }
}
