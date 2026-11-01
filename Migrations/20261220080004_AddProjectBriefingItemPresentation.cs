using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Adds how each project appears in its briefing deck (slides, table row only, left out).
    /// Additive: existing rows and rows written by earlier builds take the "Slides" default, so
    /// decks keep generating exactly as before.
    /// </remarks>
    public partial class AddProjectBriefingItemPresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Presentation",
                table: "ProjectBriefingDeckItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Slides");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Presentation",
                table: "ProjectBriefingDeckItems");
        }
    }
}
