using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261207100000_AddArppIssueAttachment")]
public partial class AddArppIssueAttachment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ArppAttachments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ArppIssueId = table.Column<long>(type: "bigint", nullable: false),
                StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArppAttachments", x => x.Id);
                table.CheckConstraint(
                    "CK_ArppAttachments_PdfContentType",
                    "\"ContentType\" = 'application/pdf'");
                table.CheckConstraint(
                    "CK_ArppAttachments_Sha256",
                    "length(\"Sha256\") = 64");
                table.CheckConstraint(
                    "CK_ArppAttachments_SizeBytes",
                    "\"SizeBytes\" > 0");
                table.ForeignKey(
                    name: "FK_ArppAttachments_ArppIssues_ArppIssueId",
                    column: x => x.ArppIssueId,
                    principalTable: "ArppIssues",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ArppAttachments_Sha256",
            table: "ArppAttachments",
            column: "Sha256");

        migrationBuilder.CreateIndex(
            name: "UX_ArppAttachments_Issue",
            table: "ArppAttachments",
            column: "ArppIssueId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ArppAttachments");
    }
}
