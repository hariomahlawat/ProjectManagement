using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HodUserId",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeadPoUserId",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_HodUserId",
                table: "Projects",
                column: "HodUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_LeadPoUserId",
                table: "Projects",
                column: "LeadPoUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AspNetUsers_HodUserId",
                table: "Projects",
                column: "HodUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AspNetUsers_LeadPoUserId",
                table: "Projects",
                column: "LeadPoUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_HodUserId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_LeadPoUserId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_HodUserId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_LeadPoUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "HodUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "LeadPoUserId",
                table: "Projects");
        }
    }
}
