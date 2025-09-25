using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddStageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StageDependencyTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "text", nullable: false),
                    FromStageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DependsOnStageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageDependencyTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Optional = table.Column<bool>(type: "boolean", nullable: false),
                    ParallelGroup = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageDependencyTemplates_Version_FromStageCode_DependsOnSta~",
                table: "StageDependencyTemplates",
                columns: new[] { "Version", "FromStageCode", "DependsOnStageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageTemplates_Version_Code",
                table: "StageTemplates",
                columns: new[] { "Version", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageDependencyTemplates");

            migrationBuilder.DropTable(
                name: "StageTemplates");
        }
    }
}
