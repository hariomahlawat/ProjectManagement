using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// IT project conference remarks (owner, 9 Nov 2026): a remark type, General (0) or Conference (1),
    /// on the remark and on its audit snapshot. Additive with a default of 0, so every existing remark
    /// stays a general remark and old code keeps working during an overlapping deployment; a rollback
    /// leaves two unused columns. Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270101080019_AddItProjectRemarkType")]
    public partial class AddItProjectRemarkType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "ItProjectRemarks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotType",
                table: "ItProjectRemarkAudits",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SnapshotType",
                table: "ItProjectRemarkAudits");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "ItProjectRemarks");
        }
    }
}
