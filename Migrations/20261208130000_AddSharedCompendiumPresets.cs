using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261208130000_AddSharedCompendiumPresets")]
    public partial class AddSharedCompendiumPresets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompendiumPresets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SettingsSchemaVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Edition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    HandlingMarking = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompendiumPresets", x => x.Id);
                    table.ForeignKey(name: "FK_CompendiumPresets_AspNetUsers_CreatedByUserId", column: x => x.CreatedByUserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_CompendiumPresets_AspNetUsers_LastModifiedByUserId", column: x => x.LastModifiedByUserId, principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompendiumPresetProjects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PresetId = table.Column<long>(type: "bigint", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    ProjectNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompendiumPresetProjects", x => x.Id);
                    table.ForeignKey(name: "FK_CompendiumPresetProjects_CompendiumPresets_PresetId", column: x => x.PresetId, principalTable: "CompendiumPresets", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_CompendiumPresetProjects_Projects_ProjectId", column: x => x.ProjectId, principalTable: "Projects", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(name: "IX_CompendiumPresets_CreatedByUserId", table: "CompendiumPresets", column: "CreatedByUserId");
            migrationBuilder.CreateIndex(name: "IX_CompendiumPresets_LastModifiedByUserId", table: "CompendiumPresets", column: "LastModifiedByUserId");
            migrationBuilder.CreateIndex(name: "IX_CompendiumPresets_IsActive", table: "CompendiumPresets", column: "IsActive");
            migrationBuilder.CreateIndex(name: "IX_CompendiumPresets_UpdatedAtUtc", table: "CompendiumPresets", column: "UpdatedAtUtc");
            migrationBuilder.CreateIndex(name: "UX_CompendiumPresets_NormalizedName", table: "CompendiumPresets", column: "NormalizedName", unique: true);
            migrationBuilder.CreateIndex(name: "IX_CompendiumPresetProjects_ProjectId", table: "CompendiumPresetProjects", column: "ProjectId");
            migrationBuilder.CreateIndex(name: "UX_CompendiumPresetProjects_Preset_Project", table: "CompendiumPresetProjects", columns: new[] { "PresetId", "ProjectId" }, unique: true);
            migrationBuilder.CreateIndex(name: "UX_CompendiumPresetProjects_Preset_SortOrder", table: "CompendiumPresetProjects", columns: new[] { "PresetId", "SortOrder" }, unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CompendiumPresetProjects");
            migrationBuilder.DropTable(name: "CompendiumPresets");
        }
    }
}
