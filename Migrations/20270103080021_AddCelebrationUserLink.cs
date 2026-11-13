using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Birthday and anniversary wishes, PR 1: the account a celebration belongs to. Additive: the
    /// column is nullable and every existing row stays not linked, so a process of the previous
    /// build that is still running keeps working during an overlapping deployment. The unique
    /// index only covers linked, live rows, so it cannot fail on existing data. The previous build
    /// does not start against the migrated database (startup refuses a migration it does not
    /// know): going back follows the rollback SOP, a database restore or this migration's Down.
    /// Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270103080021_AddCelebrationUserLink")]
    public partial class AddCelebrationUserLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Celebrations",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Celebrations_UserId_EventType",
                table: "Celebrations",
                columns: new[] { "UserId", "EventType" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL AND \"DeletedUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the column only forgets who each celebration is for; the calendar entries stay.
            migrationBuilder.DropIndex(
                name: "IX_Celebrations_UserId_EventType",
                table: "Celebrations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Celebrations");
        }
    }
}
