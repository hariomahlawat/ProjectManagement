using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddNotebookItemVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SECTION: Notebook optimistic concurrency column
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "NotebookItems",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.Sql("""
                UPDATE "NotebookItems"
                SET "Version" = gen_random_uuid()
                WHERE "Version" = '00000000-0000-0000-0000-000000000000';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: Notebook optimistic concurrency rollback
            migrationBuilder.DropColumn(
                name: "Version",
                table: "NotebookItems");
        }
    }
}
