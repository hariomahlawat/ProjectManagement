using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddRemarkMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemarkMentions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RemarkId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemarkMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemarkMentions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RemarkMentions_Remarks_RemarkId",
                        column: x => x.RemarkId,
                        principalTable: "Remarks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemarkMentions_RemarkId_UserId",
                table: "RemarkMentions",
                columns: new[] { "RemarkId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemarkMentions_UserId",
                table: "RemarkMentions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemarkMentions");
        }
    }
}
