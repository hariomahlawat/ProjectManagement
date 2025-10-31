using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class ProjectLegacyImport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArmService",
                table: "Projects",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostLakhs",
                table: "Projects",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "YearOfDevelopment",
                table: "Projects",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectLegacyImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectCategoryId = table.Column<int>(type: "integer", nullable: false),
                    TechnicalCategoryId = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ImportedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RowsReceived = table.Column<int>(type: "integer", nullable: false),
                    RowsImported = table.Column<int>(type: "integer", nullable: false),
                    SourceFileHashSha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectLegacyImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectLegacyImports_ProjectCategories_ProjectCategoryId",
                        column: x => x.ProjectCategoryId,
                        principalTable: "ProjectCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectLegacyImports_TechnicalCategories_TechnicalCategoryId",
                        column: x => x.TechnicalCategoryId,
                        principalTable: "TechnicalCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectLegacyImport_Category_Tech",
                table: "ProjectLegacyImports",
                columns: new[] { "ProjectCategoryId", "TechnicalCategoryId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectLegacyImports");

            migrationBuilder.DropColumn(
                name: "ArmService",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CostLakhs",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "YearOfDevelopment",
                table: "Projects");
        }
    }
}
