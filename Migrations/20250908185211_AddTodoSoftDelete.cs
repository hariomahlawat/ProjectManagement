using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddTodoSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "Priority",
                table: "TodoItems",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldDefaultValue: (byte)1);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TodoItems_DeletedUtc",
                table: "TodoItems");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "TodoItems");

            migrationBuilder.AlterColumn<byte>(
                name: "Priority",
                table: "TodoItems",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)1,
                oldClrType: typeof(byte),
                oldType: "smallint");
        }
    }
}
