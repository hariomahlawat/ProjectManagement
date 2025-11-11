using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDocumentOcrPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("Project document OCR pipeline requires PostgreSQL.");
            }

            migrationBuilder.AddColumn<string>(
                name: "OcrFailureReason",
                table: "ProjectDocuments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OcrLastTriedUtc",
                table: "ProjectDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OcrStatus",
                table: "ProjectDocuments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "ProjectDocuments",
                type: "tsvector",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectDocumentTexts",
                columns: table => new
                {
                    ProjectDocumentId = table.Column<int>(type: "integer", nullable: false),
                    OcrText = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
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

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""OcrStatus"" = CASE WHEN ""Status"" = 'Published' THEN 'Pending' ELSE 'None' END,
    ""OcrFailureReason"" = NULL;
");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION project_documents_build_search_vector(
    p_document_id integer,
    p_title text,
    p_description text,
    p_stage_id integer,
    p_original_file_name text)
RETURNS tsvector
LANGUAGE plpgsql
AS $$
DECLARE
    v_stage_name text;
    v_ocr text;
BEGIN
    IF p_stage_id IS NOT NULL THEN
        SELECT ""StageName"" INTO v_stage_name FROM ""ProjectStages"" WHERE ""Id"" = p_stage_id;
    END IF;

    SELECT ""OcrText"" INTO v_ocr FROM ""ProjectDocumentTexts"" WHERE ""ProjectDocumentId"" = p_document_id;

    RETURN
        setweight(to_tsvector('english', coalesce(p_title, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(v_stage_name, '')), 'B') ||
        setweight(to_tsvector('english', coalesce(p_description, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(p_original_file_name, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(v_ocr, '')), 'D');
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION project_documents_search_vector_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.""SearchVector"" = project_documents_build_search_vector(
        NEW.""Id"",
        NEW.""Title"",
        NEW.""Description"",
        NEW.""StageId"",
        NEW.""OriginalFileName"");
    RETURN NEW;
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER project_documents_search_vector_before
BEFORE INSERT OR UPDATE ON ""ProjectDocuments""
FOR EACH ROW
EXECUTE FUNCTION project_documents_search_vector_trigger();
");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION project_document_texts_search_vector_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_document_id integer;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_document_id := OLD.""ProjectDocumentId"";
    ELSE
        v_document_id := NEW.""ProjectDocumentId"";
    END IF;

    UPDATE ""ProjectDocuments"" d
    SET ""SearchVector"" = project_documents_build_search_vector(
        d.""Id"",
        d.""Title"",
        d.""Description"",
        d.""StageId"",
        d.""OriginalFileName"")
    WHERE d.""Id"" = v_document_id;

    RETURN NULL;
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER project_document_texts_search_vector_after
AFTER INSERT OR UPDATE OR DELETE ON ""ProjectDocumentTexts""
FOR EACH ROW
EXECUTE FUNCTION project_document_texts_search_vector_trigger();
");

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""SearchVector"" = project_documents_build_search_vector(
    ""Id"",
    ""Title"",
    ""Description"",
    ""StageId"",
    ""OriginalFileName"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("Project document OCR pipeline requires PostgreSQL.");
            }

            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_before ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
");

            migrationBuilder.DropTable(
                name: "ProjectDocumentTexts");

            migrationBuilder.DropColumn(
                name: "OcrFailureReason",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "OcrLastTriedUtc",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "OcrStatus",
                table: "ProjectDocuments");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "ProjectDocuments");
        }
    }
}
