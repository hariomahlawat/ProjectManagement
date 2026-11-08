using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <summary>
    /// IT projects redesign PR 2 (docs/ux/it-projects-redesign.md, "Year type"): New / CL / CF set
    /// by hand for one project in one financial year. Additive: a new table nothing else depends on,
    /// so old code ignores it during an overlapping deployment and a rollback leaves it unused.
    /// Hand-written because the model snapshot lags the model.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261231080018_AddItProjectYearTypeOverrides")]
    public partial class AddItProjectYearTypeOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItProjectYearTypeOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItProjectId = table.Column<int>(type: "integer", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SetByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItProjectYearTypeOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItProjectYearTypeOverrides_ItProjects_ItProjectId",
                        column: x => x.ItProjectId,
                        principalTable: "ItProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItProjectYearTypeOverrides_ItProjectId_FinancialYearStart",
                table: "ItProjectYearTypeOverrides",
                columns: new[] { "ItProjectId", "FinancialYearStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItProjectYearTypeOverrides");
        }
    }
}
