using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Keeps what the last generated PowerPoint said about each project (stage, status, costs),
    /// so the deck library can show when the data has moved since.
    /// Additive and nullable: existing decks show no movement until they are next generated.
    /// </remarks>
    public partial class AddProjectBriefingDeckGeneratedFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastGeneratedFactsJson",
                table: "ProjectBriefingDecks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastGeneratedFactsJson",
                table: "ProjectBriefingDecks");
        }
    }
}
