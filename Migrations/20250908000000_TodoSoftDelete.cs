using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class TodoSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedUtc",
                table: "TodoItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_DeletedUtc",
                table: "TodoItems",
                column: "DeletedUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TodoItems_DeletedUtc",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "TodoItems");
        }
    }
}
