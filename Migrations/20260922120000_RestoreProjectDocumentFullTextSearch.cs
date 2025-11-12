using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    // SECTION: Migration to restore project document OCR full-text search wiring
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260922120000_RestoreProjectDocumentFullTextSearch")]
    public partial class RestoreProjectDocumentFullTextSearch : Migration
    {
        // SECTION: Apply migration changes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("RestoreProjectDocumentFullTextSearch migration requires PostgreSQL.");
            }

            // SECTION: Remove legacy search vector helpers
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_update();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
");

            // SECTION: Recreate search vector builder that includes OCR text and metadata
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
        setweight(to_tsvector('english', coalesce(p_description, '')), 'B') ||
        setweight(to_tsvector('english', coalesce(p_original_file_name, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(v_stage_code, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(v_ocr, '')), 'D');
END;
$$;
");

            // SECTION: Trigger to refresh search vector before document persistence
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
        NEW.""OriginalFileName"
    );
    RETURN NEW;
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER project_documents_search_vector_trigger
BEFORE INSERT OR UPDATE ON ""ProjectDocuments""
FOR EACH ROW
EXECUTE FUNCTION project_documents_search_vector_trigger();
");

            // SECTION: Trigger to refresh search vector after OCR text changes
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
        d.""OriginalFileName"
    )
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

            // SECTION: Backfill search vector using refreshed logic
            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments"" d
SET ""SearchVector"" = project_documents_build_search_vector(
    d.""Id"",
    d.""Title"",
    d.""Description"",
    d.""StageId"",
    d.""OriginalFileName"
);
");
        }

        // SECTION: Revert migration changes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("RestoreProjectDocumentFullTextSearch migration requires PostgreSQL.");
            }

            // SECTION: Remove enhanced search vector helpers
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
");

            // SECTION: Restore simplified trigger from prior state
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION project_documents_search_vector_update() RETURNS trigger AS $$
BEGIN
    NEW.""SearchVector"" :=
        setweight(to_tsvector('english', coalesce(NEW.""Title"", '')), 'A') ||
        setweight(to_tsvector('english', coalesce(NEW.""OriginalFileName"", '')), 'B');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER project_documents_search_vector_trigger
BEFORE INSERT OR UPDATE ON ""ProjectDocuments""
FOR EACH ROW EXECUTE PROCEDURE project_documents_search_vector_update();
");

            // SECTION: Backfill to simplified search vector
            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""SearchVector"" =
    setweight(to_tsvector('english', coalesce(""Title"", '')), 'A') ||
    setweight(to_tsvector('english', coalesce(""OriginalFileName"", '')), 'B');
");
        }
    }
}
