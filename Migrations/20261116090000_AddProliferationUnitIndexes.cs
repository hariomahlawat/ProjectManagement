using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261116090000_AddProliferationUnitIndexes")]
    public partial class AddProliferationUnitIndexes : Migration
    {
        // SECTION: Add proliferation granular unit indexes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProlifGranular_UnitName",
                table: "ProliferationGranular",
                column: "UnitName");

            migrationBuilder.CreateIndex(
                name: "IX_ProlifGranular_UnitName_ProliferationDate",
                table: "ProliferationGranular",
                columns: new[] { "UnitName", "ProliferationDate" });
        }

        // SECTION: Remove proliferation granular unit indexes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProlifGranular_UnitName",
                table: "ProliferationGranular");

            migrationBuilder.DropIndex(
                name: "IX_ProlifGranular_UnitName_ProliferationDate",
                table: "ProliferationGranular");
        }
    }
}
