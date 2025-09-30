using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CoverPhotoId",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoverPhotoVersion",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("UPDATE \"Projects\" SET \"CoverPhotoVersion\" = 1 WHERE \"CoverPhotoVersion\" IS NULL OR \"CoverPhotoVersion\" = 0;");

            migrationBuilder.CreateTable(
                name: "ProjectPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Caption = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsCover = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPhotos_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("UPDATE \"ProjectPhotos\" SET \"Ordinal\" = 1 WHERE \"Ordinal\" = 0;");
            migrationBuilder.Sql("UPDATE \"ProjectPhotos\" SET \"Version\" = 1 WHERE \"Version\" = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CoverPhotoId",
                table: "Projects",
                column: "CoverPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPhotos_ProjectId_Ordinal",
                table: "ProjectPhotos",
                columns: new[] { "ProjectId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProjectPhotos_Cover",
                table: "ProjectPhotos",
                column: "ProjectId",
                unique: true,
                filter: "\"IsCover\" = TRUE");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_ProjectPhotos_CoverPhotoId",
                table: "Projects",
                column: "CoverPhotoId",
                principalTable: "ProjectPhotos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_ProjectPhotos_CoverPhotoId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "ProjectPhotos");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CoverPhotoId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CoverPhotoId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CoverPhotoVersion",
                table: "Projects");
        }
    }
}
