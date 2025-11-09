using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class CompletedProjectsCostAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectLppRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    LppAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    LppDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupplyOrderNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProjectDocumentId = table.Column<int>(type: "integer", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectLppRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectLppRecords_ProjectDocuments_ProjectDocumentId",
                        column: x => x.ProjectDocumentId,
                        principalTable: "ProjectDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectLppRecords_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectProductionCostFacts",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ApproxProductionCost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectProductionCostFacts", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_ProjectProductionCostFacts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTechStatuses",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    TechStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Current"),
                    AvailableForProliferation = table.Column<bool>(type: "boolean", nullable: false),
                    NotAvailableReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MarkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MarkedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTechStatuses", x => x.ProjectId);
                    table.CheckConstraint("ck_projecttechstatus_code", "\"TechStatus\" IN ('Current', 'Outdated', 'Obsolete')");
                    table.ForeignKey(
                        name: "FK_ProjectTechStatuses_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectLppRecords_ProjectDocumentId",
                table: "ProjectLppRecords",
                column: "ProjectDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectLppRecords_ProjectId_LppDate_CreatedAtUtc",
                table: "ProjectLppRecords",
                columns: new[] { "ProjectId", "LppDate", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectLppRecords");

            migrationBuilder.DropTable(
                name: "ProjectProductionCostFacts");

            migrationBuilder.DropTable(
                name: "ProjectTechStatuses");
        }
    }
}
