using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261126090000_AddNotebookClientRequestId")]
    public partial class AddNotebookClientRequestId : Migration
    {
        // SECTION: Add idempotency key for Notebook creates
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "NotebookItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotebookItems_OwnerId_ClientRequestId",
                table: "NotebookItems",
                columns: new[] { "OwnerId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }

        // SECTION: Remove idempotency key for Notebook creates
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotebookItems_OwnerId_ClientRequestId",
                table: "NotebookItems");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "NotebookItems");
        }
    }
}
