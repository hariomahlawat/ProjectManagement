using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20270101000000_AddVisitorNameToVisits")]
    public partial class AddVisitorNameToVisits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisitorName",
                table: "Visits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("UPDATE \"Visits\" SET \"VisitorName\" = 'Unknown visitor' WHERE \"VisitorName\" = '';");
            }
            else
            {
                migrationBuilder.Sql("UPDATE Visits SET VisitorName = 'Unknown visitor' WHERE VisitorName = '';");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisitorName",
                table: "Visits");
        }
    }
}
