using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// Lets users choose an organisation's primary contact. The column is optional: existing
    /// organisations keep showing their first-added contact as primary until someone chooses.
    /// Removing the chosen contact clears the choice (ON DELETE SET NULL).
    /// </summary>
    public partial class AddIndustryPartnerPrimaryContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrimaryContactId",
                table: "IndustryPartners",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndustryPartners_PrimaryContactId",
                table: "IndustryPartners",
                column: "PrimaryContactId");

            migrationBuilder.AddForeignKey(
                name: "FK_IndustryPartners_IndustryPartnerContacts_PrimaryContactId",
                table: "IndustryPartners",
                column: "PrimaryContactId",
                principalTable: "IndustryPartnerContacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IndustryPartners_IndustryPartnerContacts_PrimaryContactId",
                table: "IndustryPartners");

            migrationBuilder.DropIndex(
                name: "IX_IndustryPartners_PrimaryContactId",
                table: "IndustryPartners");

            migrationBuilder.DropColumn(
                name: "PrimaryContactId",
                table: "IndustryPartners");
        }
    }
}
