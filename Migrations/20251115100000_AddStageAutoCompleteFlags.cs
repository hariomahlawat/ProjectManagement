using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddStageAutoCompleteFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoCompletedFromCode",
                table: "ProjectStages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoCompleted",
                table: "ProjectStages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresBackfill",
                table: "ProjectStages",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoCompletedFromCode",
                table: "ProjectStages");

            migrationBuilder.DropColumn(
                name: "IsAutoCompleted",
                table: "ProjectStages");

            migrationBuilder.DropColumn(
                name: "RequiresBackfill",
                table: "ProjectStages");
        }
    }
}
