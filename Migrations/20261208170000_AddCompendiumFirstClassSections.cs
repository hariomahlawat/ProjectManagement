using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261208170000_AddCompendiumFirstClassSections")]
    public partial class AddCompendiumFirstClassSections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompendiumPresetSections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PresetId = table.Column<long>(type: "bigint", nullable: false),
                    SectionKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompendiumPresetSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompendiumPresetSections_CompendiumPresets_PresetId",
                        column: x => x.PresetId,
                        principalTable: "CompendiumPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<long>(
                name: "CustomSectionId",
                table: "CompendiumPresetProjects",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NarrativeSourceOverride",
                table: "CompendiumPresetProjects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CompendiumPresetSections_Preset_Key",
                table: "CompendiumPresetSections",
                columns: new[] { "PresetId", "SectionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CompendiumPresetSections_Preset_Name",
                table: "CompendiumPresetSections",
                columns: new[] { "PresetId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CompendiumPresetSections_Preset_SortOrder",
                table: "CompendiumPresetSections",
                columns: new[] { "PresetId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompendiumPresetProjects_CustomSectionId",
                table: "CompendiumPresetProjects",
                column: "CustomSectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompendiumPresetProjects_CompendiumPresetSections_CustomSectionId",
                table: "CompendiumPresetProjects",
                column: "CustomSectionId",
                principalTable: "CompendiumPresetSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Migrate Phase-25 project-attached section names into independent, ordered sections.
            // The legacy key is deterministic and stays well inside the 40-character contract.
            migrationBuilder.Sql("""
                INSERT INTO "CompendiumPresetSections"
                    ("PresetId", "SectionKey", "Name", "NormalizedName", "SortOrder")
                SELECT source."PresetId",
                       'legacy-' || source."AnchorId"::text,
                       source."Name",
                       source."NormalizedName",
                       source."SectionOrder"
                FROM (
                    SELECT grouped."PresetId",
                           grouped."AnchorId",
                           grouped."Name",
                           grouped."NormalizedName",
                           (ROW_NUMBER() OVER (
                               PARTITION BY grouped."PresetId"
                               ORDER BY grouped."FirstProjectOrder", grouped."NormalizedName") - 1)::integer AS "SectionOrder"
                    FROM (
                        SELECT p."PresetId",
                               MIN(p."Id") AS "AnchorId",
                               MIN(BTRIM(p."CustomSectionName")) AS "Name",
                               UPPER(BTRIM(p."CustomSectionName")) AS "NormalizedName",
                               MIN(p."SortOrder") AS "FirstProjectOrder"
                        FROM "CompendiumPresetProjects" p
                        WHERE p."CustomSectionName" IS NOT NULL
                          AND BTRIM(p."CustomSectionName") <> ''
                        GROUP BY p."PresetId", UPPER(BTRIM(p."CustomSectionName"))
                    ) grouped
                ) source;

                UPDATE "CompendiumPresetProjects" p
                SET "CustomSectionId" = s."Id"
                FROM "CompendiumPresetSections" s
                WHERE p."PresetId" = s."PresetId"
                  AND p."CustomSectionName" IS NOT NULL
                  AND UPPER(BTRIM(p."CustomSectionName")) = s."NormalizedName";

                UPDATE "CompendiumPresets"
                SET "SettingsSchemaVersion" = 5
                WHERE "SettingsSchemaVersion" <= 4;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 5,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 4);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompendiumPresetProjects_CompendiumPresetSections_CustomSectionId",
                table: "CompendiumPresetProjects");

            // Preserve section names in the legacy column before removing the first-class model.
            migrationBuilder.Sql("""
                UPDATE "CompendiumPresetProjects" p
                SET "CustomSectionName" = s."Name"
                FROM "CompendiumPresetSections" s
                WHERE p."CustomSectionId" = s."Id";

                UPDATE "CompendiumPresets"
                SET "SettingsSchemaVersion" = 4
                WHERE "SettingsSchemaVersion" = 5;
                """);

            migrationBuilder.DropIndex(
                name: "IX_CompendiumPresetProjects_CustomSectionId",
                table: "CompendiumPresetProjects");

            migrationBuilder.DropColumn(
                name: "CustomSectionId",
                table: "CompendiumPresetProjects");

            migrationBuilder.DropColumn(
                name: "NarrativeSourceOverride",
                table: "CompendiumPresetProjects");

            migrationBuilder.DropTable(name: "CompendiumPresetSections");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 4,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 5);
        }
    }
}
