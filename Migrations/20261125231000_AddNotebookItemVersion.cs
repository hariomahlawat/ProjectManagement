using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261125231000_AddNotebookItemVersion")]
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
                nullable: true);

            // SECTION: Existing row version backfill
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pgcrypto;
                """);

            migrationBuilder.Sql("""
                UPDATE "NotebookItems"
                SET "Version" = gen_random_uuid()
                WHERE "Version" IS NULL
                   OR "Version" = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "NotebookItems",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
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
