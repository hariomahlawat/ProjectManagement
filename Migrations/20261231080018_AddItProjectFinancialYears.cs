using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// IT projects redesign PR 2 (docs/ux/it-projects-redesign.md, "Year type"): one row per later
    /// financial year a project is carried into, holding that year's IT PPP reference number and
    /// an optional New / CL / CF type set by hand. Additive: a new table nothing else depends on,
    /// so old code ignores it during an overlapping deployment and a rollback leaves it unused.
    /// Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261231080018_AddItProjectFinancialYears")]
    public partial class AddItProjectFinancialYears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItProjectFinancialYears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItProjectId = table.Column<int>(type: "integer", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    PppReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TypeOverride = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItProjectFinancialYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItProjectFinancialYears_ItProjects_ItProjectId",
                        column: x => x.ItProjectId,
                        principalTable: "ItProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjectFinancialYears_ItProjectId_FinancialYearStart",
                table: "ItProjectFinancialYears",
                columns: new[] { "ItProjectId", "FinancialYearStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItProjectFinancialYears");
        }
    }
}
