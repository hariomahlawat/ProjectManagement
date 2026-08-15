using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261208190000_AddCompendiumAdaptiveDossiers")]
    public partial class AddCompendiumAdaptiveDossiers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "DossierLayout", table: "CompendiumPresetProjects", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Automatic");
            migrationBuilder.AddColumn<int>(name: "DossierImageCount", table: "CompendiumPresetProjects", type: "integer", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<int>(name: "SupportingPhoto1Id", table: "CompendiumPresetProjects", type: "integer", nullable: true);
            migrationBuilder.AddColumn<double>(name: "SupportingPhoto1FocalX", table: "CompendiumPresetProjects", type: "double precision", nullable: false, defaultValue: 0.5);
            migrationBuilder.AddColumn<double>(name: "SupportingPhoto1FocalY", table: "CompendiumPresetProjects", type: "double precision", nullable: false, defaultValue: 0.5);
            migrationBuilder.AddColumn<string>(name: "SupportingPhoto1FitMode", table: "CompendiumPresetProjects", type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Fill");
            migrationBuilder.AddColumn<int>(name: "SupportingPhoto2Id", table: "CompendiumPresetProjects", type: "integer", nullable: true);
            migrationBuilder.AddColumn<double>(name: "SupportingPhoto2FocalX", table: "CompendiumPresetProjects", type: "double precision", nullable: false, defaultValue: 0.5);
            migrationBuilder.AddColumn<double>(name: "SupportingPhoto2FocalY", table: "CompendiumPresetProjects", type: "double precision", nullable: false, defaultValue: 0.5);
            migrationBuilder.AddColumn<string>(name: "SupportingPhoto2FitMode", table: "CompendiumPresetProjects", type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Fill");

            migrationBuilder.CreateTable(
                name: "ProjectTechnicalSpecificationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(750)", maxLength: 750, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTechnicalSpecificationItems", x => x.Id);
                    table.CheckConstraint("CK_ProjectTechnicalSpecificationItems_DisplayOrder_Positive", "\"DisplayOrder\" >= 1");
                    table.ForeignKey(
                        name: "FK_ProjectTechnicalSpecificationItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProjectTechnicalSpecificationItems_Project_Order",
                table: "ProjectTechnicalSpecificationItems",
                columns: new[] { "ProjectId", "DisplayOrder" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE "CompendiumPresets"
                SET "SettingsSchemaVersion" = 7
                WHERE "SettingsSchemaVersion" <= 6;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 7,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 6);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProjectTechnicalSpecificationItems");
            foreach (var column in new[]
            {
                "DossierLayout", "DossierImageCount", "SupportingPhoto1Id", "SupportingPhoto1FocalX", "SupportingPhoto1FocalY", "SupportingPhoto1FitMode",
                "SupportingPhoto2Id", "SupportingPhoto2FocalX", "SupportingPhoto2FocalY", "SupportingPhoto2FitMode"
            })
            {
                migrationBuilder.DropColumn(name: column, table: "CompendiumPresetProjects");
            }

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 6,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 7);
        }
    }
}
