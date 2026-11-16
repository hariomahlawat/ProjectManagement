using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Briefing decks for Project Officers (docs/ux/briefing-decks.md, "Plan: Blocks, Project
    /// Officer decks and focus view", PR 2). Adds "Scope" to "ProjectBriefingDecks": 'Command' for a
    /// deck shared by Comdt and HoD users, 'Personal' for a Project Officer's own deck. Additive:
    /// the column defaults to 'Command', so every existing deck stays a command deck and nothing
    /// changes for Comdt or HoD. Deck names stay unique among live command decks (the existing
    /// index, now limited to command decks, which every existing row is) and become unique per
    /// owner among live personal decks, so no existing row can violate either index. The previous
    /// build does not start against the migrated database (startup refuses a migration it does not
    /// know): going back follows the rollback SOP, a database restore or this migration's Down.
    /// Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270107080025_AddProjectBriefingDeckScope")]
    public partial class AddProjectBriefingDeckScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "ProjectBriefingDecks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Command");

            migrationBuilder.DropIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks",
                column: "NormalizedName",
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL AND \"Scope\" = 'Command'");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectBriefingDecks_Personal_OwnerUserId_NormalizedName",
                table: "ProjectBriefingDecks",
                columns: new[] { "OwnerUserId", "NormalizedName" },
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL AND \"Scope\" = 'Personal'");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Rolling back removes Project Officers' personal decks (live or deleted): before this
        /// migration every deck was shared with command, and a personal deck's name may repeat a
        /// command deck's, which the restored single name index would refuse.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"ProjectBriefingDecks\" WHERE \"Scope\" = 'Personal';");

            migrationBuilder.DropIndex(
                name: "UX_ProjectBriefingDecks_Personal_OwnerUserId_NormalizedName",
                table: "ProjectBriefingDecks");

            migrationBuilder.DropIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectBriefingDecks_NormalizedName",
                table: "ProjectBriefingDecks",
                column: "NormalizedName",
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "ProjectBriefingDecks");
        }
    }
}
