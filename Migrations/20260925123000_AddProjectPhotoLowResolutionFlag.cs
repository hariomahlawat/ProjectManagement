using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260925123000_AddProjectPhotoLowResolutionFlag")]
    public partial class AddProjectPhotoLowResolutionFlag : Migration
    {
        // SECTION: Apply schema changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLowResolution",
                table: "ProjectPhotos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        // SECTION: Revert schema changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLowResolution",
                table: "ProjectPhotos");
        }
    }
}
