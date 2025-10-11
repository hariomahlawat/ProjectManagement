using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    public partial class AddTechnicalCategories : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TechnicalCategoryId",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TechnicalCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicalCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TechnicalCategories_TechnicalCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "TechnicalCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TechnicalCategoryId",
                table: "Projects",
                column: "TechnicalCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TechnicalCategories_ParentId_Name",
                table: "TechnicalCategories",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_TechnicalCategories_TechnicalCategoryId",
                table: "Projects",
                column: "TechnicalCategoryId",
                principalTable: "TechnicalCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_TechnicalCategories_TechnicalCategoryId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "TechnicalCategories");

            migrationBuilder.DropIndex(
                name: "IX_Projects_TechnicalCategoryId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TechnicalCategoryId",
                table: "Projects");
        }
    }
}
