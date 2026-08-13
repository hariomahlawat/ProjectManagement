using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261208140000_AddCompendiumPublicationImagery")]
public partial class AddCompendiumPublicationImagery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PrimaryPhotoId",
            table: "CompendiumPresetProjects",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PrimaryFocalX",
            table: "CompendiumPresetProjects",
            type: "double precision",
            nullable: false,
            defaultValue: 0.5d);

        migrationBuilder.AddColumn<double>(
            name: "PrimaryFocalY",
            table: "CompendiumPresetProjects",
            type: "double precision",
            nullable: false,
            defaultValue: 0.5d);

        migrationBuilder.AddColumn<string>(
            name: "ImageSelectionMode",
            table: "CompendiumPresetProjects",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Automatic");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 2,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PrimaryPhotoId", table: "CompendiumPresetProjects");
        migrationBuilder.DropColumn(name: "PrimaryFocalX", table: "CompendiumPresetProjects");
        migrationBuilder.DropColumn(name: "PrimaryFocalY", table: "CompendiumPresetProjects");
        migrationBuilder.DropColumn(name: "ImageSelectionMode", table: "CompendiumPresetProjects");

        migrationBuilder.AlterColumn<int>(
            name: "SettingsSchemaVersion",
            table: "CompendiumPresets",
            type: "integer",
            nullable: false,
            defaultValue: 1,
            oldClrType: typeof(int),
            oldType: "integer",
            oldDefaultValue: 2);
    }
}
