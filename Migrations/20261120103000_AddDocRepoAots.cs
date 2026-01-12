using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261120103000_AddDocRepoAots")]
    public partial class AddDocRepoAots : Migration
    {
        // SECTION: Add AOTS flag + view tracking
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAots",
                table: "Documents",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DocRepoAotsViews",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentId = table.Column<Guid>(nullable: false),
                    UserId = table.Column<string>(maxLength: 450, nullable: false),
                    FirstViewedAtUtc = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocRepoAotsViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocRepoAotsViews_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocRepoAotsViews_DocumentId",
                table: "DocRepoAotsViews",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocRepoAotsViews_UserId",
                table: "DocRepoAotsViews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocRepoAotsViews_DocumentId_UserId",
                table: "DocRepoAotsViews",
                columns: new[] { "DocumentId", "UserId" },
                unique: true);
        }

        // SECTION: Remove AOTS flag + view tracking
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocRepoAotsViews");

            migrationBuilder.DropColumn(
                name: "IsAots",
                table: "Documents");
        }
    }
}
