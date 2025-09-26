using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectNumberAndStageGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProjectNumber",
                table: "Projects",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ProjectNumber",
                table: "Projects",
                column: "ProjectNumber",
                unique: true,
                filter: "\"ProjectNumber\" IS NOT NULL");

            migrationBuilder.Sql("""
                ALTER TABLE "ProjectStages"
                ADD CONSTRAINT "CK_ProjectStages_CompletedHasDate"
                CHECK (NOT("Status" = 'Completed' AND "CompletedOn" IS NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ProjectStages"
                DROP CONSTRAINT IF EXISTS "CK_ProjectStages_CompletedHasDate";
                """);

            migrationBuilder.DropIndex(
                name: "IX_Projects_ProjectNumber",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectNumber",
                table: "Projects");
        }
    }
}
