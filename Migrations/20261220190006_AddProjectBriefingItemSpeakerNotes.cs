using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Adds speaker notes per project in a briefing deck, written into the PowerPoint's notes.
    /// Additive and nullable: decks without notes generate exactly as before.
    /// </remarks>
    public partial class AddProjectBriefingItemSpeakerNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpeakerNotes",
                table: "ProjectBriefingDeckItems",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeakerNotes",
                table: "ProjectBriefingDeckItems");
        }
    }
}
