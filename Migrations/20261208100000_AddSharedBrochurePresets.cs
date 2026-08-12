using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261208100000_AddSharedBrochurePresets")]
public partial class AddSharedBrochurePresets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BrochurePresets",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SettingsSchemaVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Subtitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Edition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Strapline = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                CoverStyle = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                InstitutionalCoverArtwork = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                NarrativeSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                PublicationProfile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IntroductionTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                IntroductionText = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                PrintIntroText = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                PrintFutureText = table.Column<string>(type: "character varying(3500)", maxLength: 3500, nullable: true),
                PrintProcurementText = table.Column<string>(type: "character varying(3500)", maxLength: 3500, nullable: true),
                PrintCentreStatement = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                PrintDevelopingAgencyText = table.Column<string>(type: "character varying(1800)", maxLength: 1800, nullable: true),
                PrintManufacturingAgencyText = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                PrintVisionaryText = table.Column<string>(type: "character varying(4500)", maxLength: 4500, nullable: true),
                PrintNewSimulatorsText = table.Column<string>(type: "character varying(1800)", maxLength: 1800, nullable: true),
                HandlingMarking = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                AllowTextOnlyProjects = table.Column<bool>(type: "boolean", nullable: false),
                IncludeBackCover = table.Column<bool>(type: "boolean", nullable: false),
                CoverHeroProjectId = table.Column<int>(type: "integer", nullable: true),
                CoverHeroPhotoId = table.Column<int>(type: "integer", nullable: true),
                CoverHeroFocalX = table.Column<double>(type: "double precision", nullable: false),
                CoverHeroFocalY = table.Column<double>(type: "double precision", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BrochurePresets", x => x.Id);
                table.ForeignKey(
                    name: "FK_BrochurePresets_AspNetUsers_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BrochurePresets_AspNetUsers_LastModifiedByUserId",
                    column: x => x.LastModifiedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "BrochurePresetProjects",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PresetId = table.Column<long>(type: "bigint", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                ProjectNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                PrimaryPhotoId = table.Column<int>(type: "integer", nullable: true),
                SecondaryPhotoId = table.Column<int>(type: "integer", nullable: true),
                PrimaryFocalX = table.Column<double>(type: "double precision", nullable: false),
                PrimaryFocalY = table.Column<double>(type: "double precision", nullable: false),
                SecondaryFocalX = table.Column<double>(type: "double precision", nullable: false),
                SecondaryFocalY = table.Column<double>(type: "double precision", nullable: false),
                ImageMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BrochurePresetProjects", x => x.Id);
                table.ForeignKey(
                    name: "FK_BrochurePresetProjects_BrochurePresets_PresetId",
                    column: x => x.PresetId,
                    principalTable: "BrochurePresets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BrochurePresetProjects_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BrochurePresets_CreatedByUserId",
            table: "BrochurePresets",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_BrochurePresets_LastModifiedByUserId",
            table: "BrochurePresets",
            column: "LastModifiedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_BrochurePresets_IsActive",
            table: "BrochurePresets",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_BrochurePresets_UpdatedAtUtc",
            table: "BrochurePresets",
            column: "UpdatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "UX_BrochurePresets_NormalizedName",
            table: "BrochurePresets",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BrochurePresetProjects_ProjectId",
            table: "BrochurePresetProjects",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "UX_BrochurePresetProjects_Preset_Project",
            table: "BrochurePresetProjects",
            columns: new[] { "PresetId", "ProjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_BrochurePresetProjects_Preset_SortOrder",
            table: "BrochurePresetProjects",
            columns: new[] { "PresetId", "SortOrder" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BrochurePresetProjects");
        migrationBuilder.DropTable(name: "BrochurePresets");
    }
}
