using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207090000_AddArppFoundation")]
public partial class AddArppFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ArppIssues",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                IssueSequence = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppIssues", x => x.Id);
                table.CheckConstraint(
                    "CK_ArppIssues_FinancialYearStart",
                    "\"FinancialYearStart\" BETWEEN 2000 AND 9998");
                table.CheckConstraint(
                    "CK_ArppIssues_IssueSequence",
                    "\"IssueSequence\" >= 0");
                table.CheckConstraint(
                    "CK_ArppIssues_KindSequence",
                    "(\"Kind\" = 1 AND \"IssueSequence\" = 0) OR (\"Kind\" = 2 AND \"IssueSequence\" > 0)");
            });

        migrationBuilder.CreateTable(
            name: "ArppEntries",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ArppIssueId = table.Column<long>(type: "bigint", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProjectReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                Category = table.Column<int>(type: "integer", nullable: false),
                IpaCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Cfa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Fund = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                DfpdsSchedule = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppEntries", x => x.Id);
                table.CheckConstraint(
                    "CK_ArppEntries_Category",
                    "\"Category\" IN (1, 2, 3, 4)");
                table.CheckConstraint(
                    "CK_ArppEntries_IpaCost",
                    "\"IpaCost\" >= 0");
                table.CheckConstraint(
                    "CK_ArppEntries_RequiredText",
                    "length(btrim(\"SerialNumber\")) > 0 AND " +
                    "length(btrim(\"ProjectReference\")) > 0 AND " +
                    "length(btrim(\"Cfa\")) > 0 AND " +
                    "length(btrim(\"Fund\")) > 0 AND " +
                    "length(btrim(\"DfpdsSchedule\")) > 0");
                table.CheckConstraint(
                    "CK_ArppEntries_SortOrder",
                    "\"SortOrder\" >= 0");
                table.ForeignKey(
                    name: "FK_ArppEntries_ArppIssues_ArppIssueId",
                    column: x => x.ArppIssueId,
                    principalTable: "ArppIssues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ArppEntries_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ArppEntries_ArppIssueId_SortOrder",
            table: "ArppEntries",
            columns: new[] { "ArppIssueId", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_ArppEntries_ProjectId",
            table: "ArppEntries",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "UX_ArppEntries_Issue_Project",
            table: "ArppEntries",
            columns: new[] { "ArppIssueId", "ProjectId" },
            unique: true,
            filter: "\"ProjectId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_ArppIssues_FinancialYearStart_IssueDate",
            table: "ArppIssues",
            columns: new[] { "FinancialYearStart", "IssueDate" });

        migrationBuilder.CreateIndex(
            name: "UX_ArppIssues_FinancialYear_Sequence",
            table: "ArppIssues",
            columns: new[] { "FinancialYearStart", "IssueSequence" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ArppEntries");
        migrationBuilder.DropTable(name: "ArppIssues");
    }
}
