using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261001090000_FixProjectDocumentSearchVector")]
    public partial class FixProjectDocumentSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("FixProjectDocumentSearchVector migration requires PostgreSQL.");
            }

            // SECTION: Ensure schema prerequisites
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
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_name = 'ProjectDocumentTexts'
    ) THEN
        CREATE TABLE ""ProjectDocumentTexts"" (
            ""ProjectDocumentId"" integer PRIMARY KEY,
            ""OcrText"" text NULL,
            ""UpdatedAtUtc"" timestamp with time zone NOT NULL DEFAULT now() at time zone 'utc',
            CONSTRAINT ""FK_ProjectDocumentTexts_ProjectDocuments_ProjectDocumentId""
                FOREIGN KEY (""ProjectDocumentId"")
                REFERENCES ""ProjectDocuments""(""Id"")
                ON DELETE CASCADE
        );
    END IF;
END
$$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocumentTexts' AND column_name = 'UpdatedAtUtc'
    ) THEN
        ALTER TABLE ""ProjectDocumentTexts""
        ADD COLUMN ""UpdatedAtUtc"" timestamp with time zone NOT NULL DEFAULT now() at time zone 'utc';
    END IF;
END
$$;
");

            // SECTION: Recreate search helpers
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
");

            migrationBuilder.Sql(@"
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
        NEW.""OriginalFileName""
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
        d.""OriginalFileName""
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

            // SECTION: Backfill and indexes
            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments"" d
SET ""SearchVector"" = project_documents_build_search_vector(
    d.""Id"",
    d.""Title"",
    d.""Description"",
    d.""StageId"",
    d.""OriginalFileName""
);
");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS idx_project_documents_search
    ON ""ProjectDocuments"" USING GIN (""SearchVector"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("FixProjectDocumentSearchVector migration requires PostgreSQL.");
            }

            // SECTION: Remove refreshed helpers
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
");

            // SECTION: Restore previous trigger definitions
            migrationBuilder.Sql(@"
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
        NEW.""OriginalFileName""
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
        d.""OriginalFileName""
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
        }
    }
}
