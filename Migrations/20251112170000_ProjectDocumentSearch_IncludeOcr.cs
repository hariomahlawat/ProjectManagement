using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations;

// SECTION: Migration to include OCR text in project document search vector
public partial class ProjectDocumentSearch_IncludeOcr : Migration
{
    // SECTION: Apply migration
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        // SECTION: Remove legacy search helpers
        migrationBuilder.Sql("""
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
""");

        // SECTION: Create unified search vector builder
        migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION project_documents_build_search_vector(
    p_document_id integer,
    p_title text,
    p_description text,
    p_stage_id integer,
    p_original_file_name text
)
RETURNS tsvector
LANGUAGE plpgsql
AS $$
DECLARE
    v_stage_code text;
    v_ocr text;
BEGIN
    IF p_stage_id IS NOT NULL THEN
        SELECT ""StageCode"" INTO v_stage_code
        FROM ""ProjectStages""
        WHERE ""Id"" = p_stage_id;
    END IF;

    SELECT ""OcrText"" INTO v_ocr
    FROM ""ProjectDocumentTexts""
    WHERE ""ProjectDocumentId"" = p_document_id;

    RETURN
        setweight(to_tsvector('english', coalesce(p_title, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(p_description, '')), 'B') ||
        setweight(to_tsvector('english', coalesce(p_original_file_name, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(v_stage_code, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(v_ocr, '')), 'D');
END;
$$;
""");

        // SECTION: ProjectDocuments trigger for maintaining search vector
        migrationBuilder.Sql("""
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
        NEW.""OriginalFileName""
    );
    RETURN NEW;
END;
$$;
""");

        migrationBuilder.Sql("""
CREATE TRIGGER project_documents_search_vector_trigger
BEFORE INSERT OR UPDATE ON ""ProjectDocuments""
FOR EACH ROW
EXECUTE FUNCTION project_documents_search_vector_trigger();
""");

        // SECTION: Trigger to refresh parent search vector when OCR text changes
        migrationBuilder.Sql("""
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
        d.""OriginalFileName""
    )
    WHERE d.""Id"" = v_document_id;

    RETURN NULL;
END;
$$;
""");

        migrationBuilder.Sql("""
CREATE TRIGGER project_document_texts_search_vector_after
AFTER INSERT OR UPDATE OR DELETE ON ""ProjectDocumentTexts""
FOR EACH ROW
EXECUTE FUNCTION project_document_texts_search_vector_trigger();
""");

        // SECTION: Backfill existing document search vectors
        migrationBuilder.Sql("""
UPDATE ""ProjectDocuments"" d
SET ""SearchVector"" = project_documents_build_search_vector(
    d.""Id"",
    d.""Title"",
    d.""Description"",
    d.""StageId"",
    d.""OriginalFileName""
);
""");
    }

    // SECTION: Revert migration
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        migrationBuilder.Sql("""
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
""");
    }
}
