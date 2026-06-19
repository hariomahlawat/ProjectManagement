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
            // SECTION: Idempotent Notebook optimistic concurrency column
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                ALTER TABLE "NotebookItems"
                ADD COLUMN IF NOT EXISTS "Version" uuid;

                UPDATE "NotebookItems"
                SET "Version" = gen_random_uuid()
                WHERE "Version" IS NULL
                   OR "Version" = '00000000-0000-0000-0000-000000000000';

                ALTER TABLE "NotebookItems"
                ALTER COLUMN "Version" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SECTION: Notebook optimistic concurrency rollback
            migrationBuilder.Sql("""
                ALTER TABLE "NotebookItems"
                DROP COLUMN IF EXISTS "Version";
                """);
        }
    }
}
