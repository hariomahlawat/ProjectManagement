using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// IT projects (docs/ux/it-projects.md, PR 1): the project row and its nine fixed stage rows.
    /// Additive: two new tables nothing else depends on, no change to Projects. A server still
    /// running the previous release ignores them, so the migration is safe during an overlapping
    /// deployment, and rolling the application back leaves them unused. Hand-written because the
    /// model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261227080014_AddItProjects")]
    public partial class AddItProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItProjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PppReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DroppedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DroppedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DroppedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItProjectStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItProjectId = table.Column<int>(type: "integer", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsSkipped = table.Column<bool>(type: "boolean", nullable: false),
                    CostLakhs = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItProjectStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItProjectStages_ItProjects_ItProjectId",
                        column: x => x.ItProjectId,
                        principalTable: "ItProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjects_FinancialYearStart",
                table: "ItProjects",
                column: "FinancialYearStart");

            migrationBuilder.CreateIndex(
                name: "IX_ItProjects_IsDeleted",
                table: "ItProjects",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ItProjects_UpdatedAtUtc",
                table: "ItProjects",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ItProjectStages_ItProjectId_SortOrder",
                table: "ItProjectStages",
                columns: new[] { "ItProjectId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjectStages_ItProjectId_StageCode",
                table: "ItProjectStages",
                columns: new[] { "ItProjectId", "StageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItProjectStages");

            migrationBuilder.DropTable(
                name: "ItProjects");
        }
    }
}
