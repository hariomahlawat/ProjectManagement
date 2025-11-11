using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddProjectDocumentDocRepoLink : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocRepoDocumentId",
                table: "ProjectDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_DocRepoDocumentId",
                table: "ProjectDocuments",
                column: "DocRepoDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectDocuments_Documents_DocRepoDocumentId",
                table: "ProjectDocuments",
                column: "DocRepoDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectDocuments_Documents_DocRepoDocumentId",
                table: "ProjectDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ProjectDocuments_DocRepoDocumentId",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "DocRepoDocumentId",
                table: "ProjectDocuments");
        }
    }
}
