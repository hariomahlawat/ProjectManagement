using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261216180000_AddBrochureNarrativeAlignment")]
    public partial class AddBrochureNarrativeAlignment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing brochures retain their Phase-16 ragged-right rendering. A preset moves
            // to schema 5 only when it is subsequently saved through the alignment-aware builder.
            migrationBuilder.AddColumn<string>(
                name: "NarrativeAlignment",
                table: "BrochurePresets",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Left");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "BrochurePresets",
                type: "integer",
                nullable: false,
                defaultValue: 5,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 4);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NarrativeAlignment",
                table: "BrochurePresets");

            migrationBuilder.AlterColumn<int>(
                name: "SettingsSchemaVersion",
                table: "BrochurePresets",
                type: "integer",
                nullable: false,
                defaultValue: 4,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 5);
        }
    }
}
