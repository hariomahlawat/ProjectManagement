using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261126100000_AddComdtOfficerWorkloadOrder")]
    public partial class AddComdtOfficerWorkloadOrder : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComdtOfficerWorkloadOrderJson",
                table: "AspNetUsers",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComdtOfficerWorkloadOrderJson",
                table: "AspNetUsers");
        }
    }
}
