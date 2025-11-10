using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddDocRepoExternalLinks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocRepoExternalLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceModule = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceItemId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocRepoExternalLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocRepoExternalLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocRepoExternalLinks_DocumentId",
                table: "DocRepoExternalLinks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocRepoExternalLinks_SourceModule_SourceItemId",
                table: "DocRepoExternalLinks",
                columns: new[] { "SourceModule", "SourceItemId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocRepoExternalLinks");
        }
    }
}
