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
                defaultValueSql: "gen_random_uuid()");
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
