using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125130000_ExpandProjectAndMetaDescriptionsTo5000")]
    public class ExpandProjectAndMetaDescriptionsTo5000 : Migration
    {
        // SECTION: Apply schema changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SECTION: Expand Projects.Description from 1000 to 5000
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Projects",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            // SECTION: Expand ProjectMetaChangeRequests.OriginalDescription from 1000 to 5000
            migrationBuilder.AlterColumn<string>(
                name: "OriginalDescription",
                table: "ProjectMetaChangeRequests",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }

        // SECTION: Revert schema changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: Revert Projects.Description from 5000 to 1000
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Projects",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(5000)",
                oldMaxLength: 5000,
                oldNullable: true);

            // SECTION: Revert ProjectMetaChangeRequests.OriginalDescription from 5000 to 1000
            migrationBuilder.AlterColumn<string>(
                name: "OriginalDescription",
                table: "ProjectMetaChangeRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(5000)",
                oldMaxLength: 5000,
                oldNullable: true);
        }
    }
}
