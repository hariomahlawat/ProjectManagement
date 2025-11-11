using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace ProjectManagement.Migrations
{
    public partial class AddProjectDocumentOcrAndSearch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) ADD OCR/SEARCH COLUMNS TO PROJECT DOCUMENTS
            migrationBuilder.AddColumn<int>(
                name: "OcrStatus",
                table: "ProjectDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0); // 0 = Pending (or whatever your enum base is)

            migrationBuilder.AddColumn<string>(
                name: "OcrFailureReason",
                table: "ProjectDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "ProjectDocuments",
                type: "tsvector",
                nullable: true);

            // 2) CREATE PROJECT DOCUMENT TEXTS (1:1)
            migrationBuilder.CreateTable(
                name: "ProjectDocumentTexts",
                columns: table => new
                {
                    ProjectDocumentId = table.Column<int>(type: "integer", nullable: false),
                    OcrText = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocumentTexts", x => x.ProjectDocumentId);
                    table.ForeignKey(
                        name: "FK_ProjectDocumentTexts_ProjectDocuments_ProjectDocumentId",
                        column: x => x.ProjectDocumentId,
                        principalTable: "ProjectDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 3) INDEX FOR FTS
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_ProjectDocuments_SearchVector""
                ON ""ProjectDocuments""
                USING GIN (""SearchVector"");
            ");

            // 4) OPTIONAL TRIGGER/FUNCTION TO KEEP SEARCHVECTOR IN SYNC
            migrationBuilder.Sql(@"
                CREATE FUNCTION project_documents_search_vector_update() RETURNS trigger AS $$
                BEGIN
                    NEW.""SearchVector"" :=
                        setweight(to_tsvector('english', coalesce(NEW.""Title"", '')), 'A')
                        ||
                        setweight(to_tsvector('english', coalesce(NEW.""OriginalFileName"", '')), 'B');
                    RETURN NEW;
                END
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";

                CREATE TRIGGER project_documents_search_vector_trigger
                BEFORE INSERT OR UPDATE ON ""ProjectDocuments""
                FOR EACH ROW EXECUTE PROCEDURE project_documents_search_vector_update();
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // drop trigger + function
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS project_documents_search_vector_update();");

            migrationBuilder.DropTable(
                name: "ProjectDocumentTexts");

            migrationBuilder.DropColumn(
                name: "OcrStatus",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "OcrFailureReason",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "ProjectDocuments");
        }
    }
}
