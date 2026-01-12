using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261119120000_AddDocRepoFavourites")]
    public partial class AddDocRepoFavourites : Migration
    {
        // SECTION: Add document repository favourites
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocRepoFavourites",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(maxLength: 64, nullable: false),
                    DocumentId = table.Column<Guid>(nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocRepoFavourites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocRepoFavourites_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocRepoFavourites_DocumentId",
                table: "DocRepoFavourites",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocRepoFavourites_UserId_DocumentId",
                table: "DocRepoFavourites",
                columns: new[] { "UserId", "DocumentId" },
                unique: true);
        }

        // SECTION: Remove document repository favourites
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocRepoFavourites");
        }
    }
}
