using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddDocumentServices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileStamp",
                table: "ProjectDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ProjectDocuments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ProjectDocumentRequests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "ProjectDocumentRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "ProjectDocumentRequests",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestType",
                table: "ProjectDocumentRequests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Upload");

            migrationBuilder.AddColumn<string>(
                name: "TempStorageKey",
                table: "ProjectDocumentRequests",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.DropIndex(
                name: "ux_projectdocumentrequests_pending",
                table: "ProjectDocumentRequests");

            migrationBuilder.CreateIndex(
                name: "ux_projectdocumentrequests_pending_document",
                table: "ProjectDocumentRequests",
                column: "DocumentId",
                unique: true,
                filter: "\"DocumentId\" IS NOT NULL AND \"Status\" IN ('Draft', 'Submitted')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_projectdocumentrequests_pending_document",
                table: "ProjectDocumentRequests");

            migrationBuilder.DropColumn(
                name: "FileStamp",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ProjectDocumentRequests");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "ProjectDocumentRequests");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "ProjectDocumentRequests");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "ProjectDocumentRequests");

            migrationBuilder.DropColumn(
                name: "TempStorageKey",
                table: "ProjectDocumentRequests");

            migrationBuilder.CreateIndex(
                name: "ux_projectdocumentrequests_pending",
                table: "ProjectDocumentRequests",
                columns: new[] { "ProjectId", "StageId" },
                unique: true,
                filter: "\"Status\" IN ('Draft', 'Submitted')");
        }
    }
}
