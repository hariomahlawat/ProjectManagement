using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FeaturedVideoId",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeaturedVideoVersion",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ProjectVideos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    TotId = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    PosterStorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    PosterContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectVideos_ProjectTots_TotId",
                        column: x => x.TotId,
                        principalTable: "ProjectTots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectVideos_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVideos_ProjectId_Ordinal",
                table: "ProjectVideos",
                columns: new[] { "ProjectId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVideos_ProjectId_TotId",
                table: "ProjectVideos",
                columns: new[] { "ProjectId", "TotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectVideos_TotId",
                table: "ProjectVideos",
                column: "TotId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_FeaturedVideoId",
                table: "Projects",
                column: "FeaturedVideoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_ProjectVideos_FeaturedVideoId",
                table: "Projects",
                column: "FeaturedVideoId",
                principalTable: "ProjectVideos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_ProjectVideos_FeaturedVideoId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "ProjectVideos");

            migrationBuilder.DropIndex(
                name: "IX_Projects_FeaturedVideoId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "FeaturedVideoId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "FeaturedVideoVersion",
                table: "Projects");
        }
    }
}
