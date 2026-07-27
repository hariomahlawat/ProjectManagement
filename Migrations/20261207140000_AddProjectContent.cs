using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207140000_AddProjectContent")]
public partial class AddProjectContent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProjectBrief",
            table: "Projects",
            type: "character varying(2500)",
            maxLength: 2500,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ContentUpdatedAtUtc",
            table: "Projects",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContentUpdatedByUserId",
            table: "Projects",
            type: "character varying(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NarrativeMode",
            table: "ProjectBriefingDecks",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "CapabilityOverview");

        migrationBuilder.CreateTable(
            name: "ProjectCapabilityStatements",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Statement = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                DisplayOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectCapabilityStatements", x => x.Id);
                table.CheckConstraint(
                    "CK_ProjectCapabilityStatements_DisplayOrder_Positive",
                    "\"DisplayOrder\" >= 1");
                table.ForeignKey(
                    name: "FK_ProjectCapabilityStatements_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectCapabilityStatements_ProjectId_DisplayOrder",
            table: "ProjectCapabilityStatements",
            columns: new[] { "ProjectId", "DisplayOrder" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProjectCapabilityStatements");

        migrationBuilder.DropColumn(name: "NarrativeMode", table: "ProjectBriefingDecks");
        migrationBuilder.DropColumn(name: "ProjectBrief", table: "Projects");
        migrationBuilder.DropColumn(name: "ContentUpdatedAtUtc", table: "Projects");
        migrationBuilder.DropColumn(name: "ContentUpdatedByUserId", table: "Projects");
    }
}
