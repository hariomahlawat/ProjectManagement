using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207130000_AddArppPublishedSnapshots")]
public partial class AddArppPublishedSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ArppPublishedIssues",
            columns: table => new
            {
                ArppIssueId = table.Column<long>(type: "bigint", nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                IssueSequence = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PublishedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                AttachmentStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                AttachmentOriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                AttachmentContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AttachmentSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                AttachmentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppPublishedIssues", x => x.ArppIssueId);
                table.CheckConstraint("CK_ArppPublishedIssues_AttachmentSha256", "length(\"AttachmentSha256\") = 64");
                table.CheckConstraint("CK_ArppPublishedIssues_AttachmentSize", "\"AttachmentSizeBytes\" > 0");
                table.CheckConstraint("CK_ArppPublishedIssues_FinancialYearStart", "\"FinancialYearStart\" BETWEEN 2000 AND 9998");
                table.CheckConstraint("CK_ArppPublishedIssues_IssueSequence", "\"IssueSequence\" >= 0");
                table.CheckConstraint(
                    "CK_ArppPublishedIssues_KindSequence",
                    "(\"Kind\" = 1 AND \"IssueSequence\" = 0) OR (\"Kind\" = 2 AND \"IssueSequence\" > 0)");
                table.CheckConstraint("CK_ArppPublishedIssues_PdfContentType", "\"AttachmentContentType\" = 'application/pdf'");
                table.CheckConstraint("CK_ArppPublishedIssues_RevisionNumber", "\"RevisionNumber\" > 0");
                table.ForeignKey(
                    name: "FK_ArppPublishedIssues_ArppIssues_ArppIssueId",
                    column: x => x.ArppIssueId,
                    principalTable: "ArppIssues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ArppPublishedEntries",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ArppIssueId = table.Column<long>(type: "bigint", nullable: false),
                SourceEntryId = table.Column<long>(type: "bigint", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProjectReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                Category = table.Column<int>(type: "integer", nullable: false),
                IpaCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Cfa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Fund = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                DfpdsSchedule = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppPublishedEntries", x => x.Id);
                table.CheckConstraint("CK_ArppPublishedEntries_Category", "\"Category\" IN (1, 2, 3, 4)");
                table.CheckConstraint("CK_ArppPublishedEntries_IpaCost", "\"IpaCost\" >= 0");
                table.CheckConstraint(
                    "CK_ArppPublishedEntries_RequiredText",
                    "length(btrim(\"SerialNumber\")) > 0 AND length(btrim(\"ProjectReference\")) > 0 AND length(btrim(\"Cfa\")) > 0 AND length(btrim(\"Fund\")) > 0 AND length(btrim(\"DfpdsSchedule\")) > 0");
                table.CheckConstraint("CK_ArppPublishedEntries_SortOrder", "\"SortOrder\" >= 0");
                table.CheckConstraint("CK_ArppPublishedEntries_SourceEntryId", "\"SourceEntryId\" > 0");
                table.ForeignKey(
                    name: "FK_ArppPublishedEntries_ArppPublishedIssues_ArppIssueId",
                    column: x => x.ArppIssueId,
                    principalTable: "ArppPublishedIssues",
                    principalColumn: "ArppIssueId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ArppPublishedEntries_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ArppPublishedIssues_FinancialYearStart_IssueSequence",
            table: "ArppPublishedIssues",
            columns: new[] { "FinancialYearStart", "IssueSequence" });

        migrationBuilder.CreateIndex(
            name: "IX_ArppPublishedIssues_PublishedAtUtc",
            table: "ArppPublishedIssues",
            column: "PublishedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_ArppPublishedIssues_AttachmentStorageKey",
            table: "ArppPublishedIssues",
            column: "AttachmentStorageKey");

        migrationBuilder.CreateIndex(
            name: "IX_ArppPublishedEntries_ArppIssueId_SortOrder",
            table: "ArppPublishedEntries",
            columns: new[] { "ArppIssueId", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_ArppPublishedEntries_ProjectId",
            table: "ArppPublishedEntries",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ArppPublishedEntries_SourceEntryId",
            table: "ArppPublishedEntries",
            column: "SourceEntryId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_ArppPublishedEntries_Issue_Project",
            table: "ArppPublishedEntries",
            columns: new[] { "ArppIssueId", "ProjectId" },
            unique: true,
            filter: "\"ProjectId\" IS NOT NULL");

        // Existing verified issues become immediately available in the organisation-wide
        // read-only library. The stored PDF key is retained as the immutable published source.
        migrationBuilder.Sql("""
            INSERT INTO "ArppPublishedIssues"
                ("ArppIssueId", "RevisionNumber", "FinancialYearStart", "Kind", "IssueSequence",
                 "Name", "IssueDate", "PublishedAtUtc", "PublishedByUserId",
                 "AttachmentStorageKey", "AttachmentOriginalFileName", "AttachmentContentType",
                 "AttachmentSizeBytes", "AttachmentSha256")
            SELECT i."Id", 1, i."FinancialYearStart", i."Kind", i."IssueSequence",
                   i."Name", i."IssueDate", i."VerifiedAtUtc", i."VerifiedByUserId",
                   a."StorageKey", a."OriginalFileName", a."ContentType", a."SizeBytes", a."Sha256"
            FROM "ArppIssues" AS i
            INNER JOIN "ArppAttachments" AS a ON a."ArppIssueId" = i."Id"
            WHERE i."IsVerified" = TRUE
              AND i."VerifiedAtUtc" IS NOT NULL
              AND length(btrim(i."VerifiedByUserId")) > 0;

            INSERT INTO "ArppPublishedEntries"
                ("ArppIssueId", "SourceEntryId", "SortOrder", "SerialNumber", "ProjectReference", "ProjectId",
                 "Category", "IpaCost", "Cfa", "Fund", "DfpdsSchedule")
            SELECT e."ArppIssueId", e."Id", e."SortOrder", e."SerialNumber", e."ProjectReference", e."ProjectId",
                   e."Category", e."IpaCost", e."Cfa", e."Fund", e."DfpdsSchedule"
            FROM "ArppEntries" AS e
            INNER JOIN "ArppPublishedIssues" AS p ON p."ArppIssueId" = e."ArppIssueId";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ArppPublishedEntries");
        migrationBuilder.DropTable(name: "ArppPublishedIssues");
    }
}
