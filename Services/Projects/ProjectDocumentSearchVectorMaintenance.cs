using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Projects;

// SECTION: Project document search vector maintenance helpers
public static class ProjectDocumentSearchVectorMaintenance
{
    public static async Task EnsureUpToDateAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        if (db is null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (!db.Database.IsNpgsql())
        {
            return;
        }

        // SECTION: Drop legacy helpers if they exist so the new ones can be created idempotently
        const string dropSql = """
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON "ProjectDocumentTexts";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON "ProjectDocuments";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_search_vector_update();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
""";
        await db.Database.ExecuteSqlRawAsync(dropSql, cancellationToken);

        // SECTION: Helper function to assemble the composite search vector (title, metadata, OCR)
        const string helperSql = """
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
        SELECT "StageCode" INTO v_stage_code
        FROM "ProjectStages"
        WHERE "Id" = p_stage_id;
    END IF;

    SELECT "OcrText" INTO v_ocr
    FROM "ProjectDocumentTexts"
    WHERE "ProjectDocumentId" = p_document_id;

    RETURN
        setweight(to_tsvector('english', coalesce(p_title, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(p_description, '')), 'B') ||
        setweight(to_tsvector('english', coalesce(p_original_file_name, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(v_stage_code, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(v_ocr, '')), 'D');
END;
$$;
""";
        await db.Database.ExecuteSqlRawAsync(helperSql, cancellationToken);

        // SECTION: Trigger on ProjectDocuments so direct edits keep the search vector in sync
        const string documentTriggerSql = """
CREATE OR REPLACE FUNCTION project_documents_search_vector_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW."SearchVector" = project_documents_build_search_vector(
        NEW."Id",
        NEW."Title",
        NEW."Description",
        NEW."StageId",
        NEW."OriginalFileName"
    );
    RETURN NEW;
END;
$$;

CREATE TRIGGER project_documents_search_vector_trigger
BEFORE INSERT OR UPDATE ON "ProjectDocuments"
FOR EACH ROW
EXECUTE FUNCTION project_documents_search_vector_trigger();
""";
        await db.Database.ExecuteSqlRawAsync(documentTriggerSql, cancellationToken);

        // SECTION: Trigger on ProjectDocumentTexts so OCR updates refresh the parent document
        const string textTriggerSql = """
CREATE OR REPLACE FUNCTION project_document_texts_search_vector_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_document_id integer;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_document_id := OLD."ProjectDocumentId";
    ELSE
        v_document_id := NEW."ProjectDocumentId";
    END IF;

    UPDATE "ProjectDocuments" AS d
    SET "SearchVector" = project_documents_build_search_vector(
        d."Id",
        d."Title",
        d."Description",
        d."StageId",
        d."OriginalFileName"
    )
    WHERE d."Id" = v_document_id;

    RETURN NULL;
END;
$$;

CREATE TRIGGER project_document_texts_search_vector_after
AFTER INSERT OR UPDATE OR DELETE ON "ProjectDocumentTexts"
FOR EACH ROW
EXECUTE FUNCTION project_document_texts_search_vector_trigger();
""";
        await db.Database.ExecuteSqlRawAsync(textTriggerSql, cancellationToken);

        // SECTION: Backfill existing rows so historical documents gain OCR search immediately
        const string backfillSql = """
UPDATE "ProjectDocuments" AS d
SET "SearchVector" = project_documents_build_search_vector(
    d."Id",
    d."Title",
    d."Description",
    d."StageId",
    d."OriginalFileName"
);
""";
        await db.Database.ExecuteSqlRawAsync(backfillSql, cancellationToken);
    }
}
