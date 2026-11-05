using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Records how many slides the last generated PowerPoint had, for the deck library.
    /// Additive and nullable: existing decks show no count until they are next generated.
    /// </remarks>
    public partial class AddProjectBriefingDeckGeneratedSlideCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastGeneratedSlideCount",
                table: "ProjectBriefingDecks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastGeneratedSlideCount",
                table: "ProjectBriefingDecks");
        }
    }
}
