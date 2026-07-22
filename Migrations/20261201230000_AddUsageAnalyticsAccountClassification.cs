using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261201230000_AddUsageAnalyticsAccountClassification")]
public partial class AddUsageAnalyticsAccountClassification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AccountKind",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.Sql(
            "UPDATE \"AspNetUsers\" SET \"AccountKind\" = 2 WHERE \"Id\" = 'system';");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUsers_AccountKind",
            table: "AspNetUsers",
            column: "AccountKind");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AspNetUsers_AccountKind",
            table: "AspNetUsers",
            sql: "\"AccountKind\" IN (1, 2, 3)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_AspNetUsers_AccountKind",
            table: "AspNetUsers");

        migrationBuilder.DropIndex(
            name: "IX_AspNetUsers_AccountKind",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "AccountKind",
            table: "AspNetUsers");
    }
}
