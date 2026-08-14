using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261208180000_AddCompendiumCoverComposer")]
    public partial class AddCompendiumCoverComposer : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "BackCoverEdition", table: "CompendiumPresets", type: "character varying(80)", maxLength: 80, nullable: true);
            migrationBuilder.AddColumn<string>(name: "BackCoverEyebrow", table: "CompendiumPresets", type: "character varying(80)", maxLength: 80, nullable: true);
            migrationBuilder.AddColumn<string>(name: "BackCoverSubtitle", table: "CompendiumPresets", type: "character varying(160)", maxLength: 160, nullable: true);
            migrationBuilder.AddColumn<string>(name: "BackCoverTemplate", table: "CompendiumPresets", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "MinimalInstitutional");
            migrationBuilder.AddColumn<string>(name: "BackCoverTitle", table: "CompendiumPresets", type: "character varying(120)", maxLength: 120, nullable: true);
            migrationBuilder.AddColumn<string>(name: "BackLogoPlacement", table: "CompendiumPresets", type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "TopCorners");
            migrationBuilder.AddColumn<string>(name: "FrontCoverEdition", table: "CompendiumPresets", type: "character varying(80)", maxLength: 80, nullable: true);
            migrationBuilder.AddColumn<string>(name: "FrontCoverEyebrow", table: "CompendiumPresets", type: "character varying(80)", maxLength: 80, nullable: true);
            migrationBuilder.AddColumn<string>(name: "FrontCoverSubtitle", table: "CompendiumPresets", type: "character varying(160)", maxLength: 160, nullable: true);
            migrationBuilder.AddColumn<string>(name: "FrontCoverTemplate", table: "CompendiumPresets", type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "InstitutionalHero");
            migrationBuilder.AddColumn<string>(name: "FrontCoverTitle", table: "CompendiumPresets", type: "character varying(120)", maxLength: 120, nullable: true);
            migrationBuilder.AddColumn<string>(name: "FrontLogoPlacement", table: "CompendiumPresets", type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "TopCorners");
            migrationBuilder.AddColumn<bool>(name: "ShowBackEdition", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowBackLeftLogo", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowBackRightLogo", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowBackSubtitle", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowBackTitle", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowFrontEdition", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowFrontLeftLogo", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowFrontRightLogo", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowFrontSubtitle", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "ShowFrontTitle", table: "CompendiumPresets", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<string>(name: "ImageFitMode", table: "CompendiumPresetProjects", type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Fill");

            migrationBuilder.CreateTable(
                name: "CompendiumPresetCoverImages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PresetId = table.Column<long>(type: "bigint", nullable: false),
                    Surface = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SlotKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ImageMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Automatic"),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    PhotoId = table.Column<int>(type: "integer", nullable: true),
                    FocalX = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.5),
                    FocalY = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.5),
                    FitMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Fill"),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompendiumPresetCoverImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompendiumPresetCoverImages_CompendiumPresets_PresetId",
                        column: x => x.PresetId,
                        principalTable: "CompendiumPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompendiumPresetPhotoPreferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PresetId = table.Column<long>(type: "bigint", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    PhotoId = table.Column<int>(type: "integer", nullable: false),
                    PreferredForPublication = table.Column<bool>(type: "boolean", nullable: false),
                    SuitableForCoverHero = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompendiumPresetPhotoPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompendiumPresetPhotoPreferences_CompendiumPresets_PresetId",
                        column: x => x.PresetId,
                        principalTable: "CompendiumPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_CompendiumPresetCoverImages_PhotoId", table: "CompendiumPresetCoverImages", column: "PhotoId");
            migrationBuilder.CreateIndex(name: "UX_CompendiumPresetCoverImages_Preset_Surface_Slot", table: "CompendiumPresetCoverImages", columns: new[] { "PresetId", "Surface", "SlotKey" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_CompendiumPresetPhotoPreferences_PhotoId", table: "CompendiumPresetPhotoPreferences", column: "PhotoId");
            migrationBuilder.CreateIndex(name: "UX_CompendiumPresetPhotoPreferences_Preset_Project_Photo", table: "CompendiumPresetPhotoPreferences", columns: new[] { "PresetId", "ProjectId", "PhotoId" }, unique: true);

            // Preserve the legacy single-cover hero as the first front-cover slot so existing
            // saved Compendiums render identically after upgrading to the cover composer.
            migrationBuilder.Sql("""
                INSERT INTO "CompendiumPresetCoverImages"
                    ("PresetId", "Surface", "SlotKey", "ImageMode", "ProjectId", "PhotoId", "FocalX", "FocalY", "FitMode", "SortOrder")
                SELECT p."Id", 'Front', 'Hero', p."CoverImageMode", p."CoverHeroProjectId", p."CoverHeroPhotoId",
                       p."CoverFocalX", p."CoverFocalY", 'Fill', 0
                FROM "CompendiumPresets" p;

                UPDATE "CompendiumPresets"
                SET "SettingsSchemaVersion" = 6
                WHERE "SettingsSchemaVersion" <= 5;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 6,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 5);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "CompendiumPresets" p
                SET "CoverImageMode" = COALESCE(i."ImageMode", p."CoverImageMode"),
                    "CoverHeroProjectId" = i."ProjectId",
                    "CoverHeroPhotoId" = i."PhotoId",
                    "CoverFocalX" = COALESCE(i."FocalX", p."CoverFocalX"),
                    "CoverFocalY" = COALESCE(i."FocalY", p."CoverFocalY"),
                    "SettingsSchemaVersion" = 5
                FROM "CompendiumPresetCoverImages" i
                WHERE i."PresetId" = p."Id" AND i."Surface" = 'Front' AND i."SlotKey" = 'Hero';
                """);

            migrationBuilder.DropTable(name: "CompendiumPresetPhotoPreferences");
            migrationBuilder.DropTable(name: "CompendiumPresetCoverImages");
            migrationBuilder.DropColumn(name: "ImageFitMode", table: "CompendiumPresetProjects");

            foreach (var column in new[]
            {
                "BackCoverEdition", "BackCoverEyebrow", "BackCoverSubtitle", "BackCoverTemplate", "BackCoverTitle", "BackLogoPlacement",
                "FrontCoverEdition", "FrontCoverEyebrow", "FrontCoverSubtitle", "FrontCoverTemplate", "FrontCoverTitle", "FrontLogoPlacement",
                "ShowBackEdition", "ShowBackLeftLogo", "ShowBackRightLogo", "ShowBackSubtitle", "ShowBackTitle",
                "ShowFrontEdition", "ShowFrontLeftLogo", "ShowFrontRightLogo", "ShowFrontSubtitle", "ShowFrontTitle"
            })
            {
                migrationBuilder.DropColumn(name: column, table: "CompendiumPresets");
            }

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 5,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 6);
        }
    }
}
