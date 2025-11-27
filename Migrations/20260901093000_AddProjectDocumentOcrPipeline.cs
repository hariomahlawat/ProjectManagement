using System;
using Microsoft.EntityFrameworkCore.Migrations;

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

            migrationBuilder.AlterColumn<string>(
                name: "OcrFailureReason",
                table: "ProjectDocuments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "OcrStatus",
                table: "ProjectDocuments",
                newName: "OcrStatusLegacy");

            migrationBuilder.AddColumn<string>(
                name: "OcrStatus",
                table: "ProjectDocuments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""OcrStatus"" = CASE ""OcrStatusLegacy""
    WHEN 1 THEN 'Pending'
    WHEN 2 THEN 'Succeeded'
    WHEN 3 THEN 'Failed'
    ELSE 'None'
END;
");

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""OcrStatus"" = CASE WHEN ""Status"" = 'Published' THEN 'Pending' ELSE 'None' END,
    ""OcrFailureReason"" = NULL
WHERE ""OcrStatus"" IN ('None', 'Pending');
");

            migrationBuilder.DropColumn(
                name: "OcrStatusLegacy",
                table: "ProjectDocuments");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments' AND column_name = 'SearchVector'
    ) THEN
        ALTER TABLE ""ProjectDocuments"" ADD COLUMN ""SearchVector"" tsvector;
    END IF;
END
$$;
");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""ProjectDocumentTexts"" (
    ""ProjectDocumentId"" integer PRIMARY KEY,
    ""OcrText"" text NULL,
    ""UpdatedAtUtc"" timestamp with time zone NOT NULL DEFAULT timezone('utc', now()),
    CONSTRAINT ""FK_ProjectDocumentTexts_ProjectDocuments_ProjectDocumentId""
        FOREIGN KEY (""ProjectDocumentId"")
        REFERENCES ""ProjectDocuments""(""Id"")
        ON DELETE CASCADE
);
");

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
    v_stage_code text;
    v_ocr text;
BEGIN
    IF p_stage_id IS NOT NULL THEN
        SELECT ""StageCode"" INTO v_stage_code FROM ""ProjectStages"" WHERE ""Id"" = p_stage_id;
    END IF;

    SELECT ""OcrText"" INTO v_ocr FROM ""ProjectDocumentTexts"" WHERE ""ProjectDocumentId"" = p_document_id;

    RETURN
        setweight(to_tsvector('english', coalesce(p_title, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(v_stage_code, '')), 'B') ||
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

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_project_documents_search
    ON ""ProjectDocuments""
    USING GIN (""SearchVector"");
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
DROP INDEX IF EXISTS idx_project_documents_search;
");

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
                name: "SearchVector",
                table: "ProjectDocuments");

            migrationBuilder.AddColumn<int>(
                name: "OcrStatusLegacy",
                table: "ProjectDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""OcrStatusLegacy"" = CASE ""OcrStatus""
    WHEN 'Pending' THEN 1
    WHEN 'Succeeded' THEN 2
    WHEN 'Failed' THEN 3
    ELSE 0
END;
");

            migrationBuilder.DropColumn(
                name: "OcrStatus",
                table: "ProjectDocuments");

            migrationBuilder.RenameColumn(
                name: "OcrStatusLegacy",
                table: "ProjectDocuments",
                newName: "OcrStatus");

            migrationBuilder.AlterColumn<string>(
                name: "OcrFailureReason",
                table: "ProjectDocuments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }
    }
}
