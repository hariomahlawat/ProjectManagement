using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261208150000_AddCompendiumCoverHeroControls")]
    public partial class AddCompendiumCoverHeroControls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CoverFocalX",
                table: "CompendiumPresets",
                type: "double precision",
                nullable: false,
                defaultValue: 0.5d);

            migrationBuilder.AddColumn<double>(
                name: "CoverFocalY",
                table: "CompendiumPresets",
                type: "double precision",
                nullable: false,
                defaultValue: 0.5d);

            migrationBuilder.AddColumn<int>(
                name: "CoverHeroPhotoId",
                table: "CompendiumPresets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoverHeroProjectId",
                table: "CompendiumPresets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageMode",
                table: "CompendiumPresets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Automatic");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 2);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CoverFocalX", table: "CompendiumPresets");
            migrationBuilder.DropColumn(name: "CoverFocalY", table: "CompendiumPresets");
            migrationBuilder.DropColumn(name: "CoverHeroPhotoId", table: "CompendiumPresets");
            migrationBuilder.DropColumn(name: "CoverHeroProjectId", table: "CompendiumPresets");
            migrationBuilder.DropColumn(name: "CoverImageMode", table: "CompendiumPresets");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "CompendiumPresets",
                type: "integer",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 3);
        }
    }
}
