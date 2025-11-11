using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDefaultRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SECTION: Default role storage
            migrationBuilder.AddColumn<string>(
                name: "DefaultUserRoleId",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: Default role storage
            migrationBuilder.DropColumn(
                name: "DefaultUserRoleId",
                table: "AspNetUsers");
        }
    }
}
