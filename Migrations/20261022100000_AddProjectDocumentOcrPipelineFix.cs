using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261022100000_AddProjectDocumentOcrPipelineFix")]
    public partial class AddProjectDocumentOcrPipelineFix : Migration
    {
        // SECTION: Apply schema fixes
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("Project document OCR pipeline requires PostgreSQL.");
            }

            // SECTION: Align OCR failure reason length
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments' AND column_name = 'OcrFailureReason'
    ) THEN
        ALTER TABLE ""ProjectDocuments""
        ALTER COLUMN ""OcrFailureReason""
        TYPE character varying(1024);
    ELSE
        ALTER TABLE ""ProjectDocuments""
        ADD COLUMN ""OcrFailureReason"" character varying(1024);
    END IF;
END
$$;
");

            // SECTION: Rebuild OCR status column as string enum
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments'
          AND column_name = 'OcrStatus'
          AND data_type = 'integer'
    ) THEN
        ALTER TABLE ""ProjectDocuments"" RENAME COLUMN ""OcrStatus"" TO ""OcrStatusLegacy"";
    END IF;
END
$$;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments'
          AND column_name = 'OcrStatus'
    ) THEN
        ALTER TABLE ""ProjectDocuments""
        ADD COLUMN ""OcrStatus"" character varying(32) NOT NULL DEFAULT 'None';
    END IF;
END
$$;
");

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""OcrStatus"" = CASE
    WHEN EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments'
          AND column_name = 'OcrStatusLegacy'
    ) THEN CASE ""OcrStatusLegacy""
        WHEN 1 THEN 'Pending'
        WHEN 2 THEN 'Succeeded'
        WHEN 3 THEN 'Failed'
        ELSE 'None'
    END
    ELSE ""OcrStatus""
END;
");

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""OcrStatus"" = CASE WHEN ""Status"" = 'Published' THEN 'Pending' ELSE 'None' END,
    ""OcrFailureReason"" = NULL;
");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments'
          AND column_name = 'OcrStatusLegacy'
    ) THEN
        ALTER TABLE ""ProjectDocuments"" DROP COLUMN ""OcrStatusLegacy"";
    END IF;
END
$$;
");

            // SECTION: Ensure search schema exists
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
            ""UpdatedAtUtc"" timestamp with time zone NOT NULL DEFAULT timezone('utc', now()),
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
        ADD COLUMN ""UpdatedAtUtc"" timestamp with time zone NOT NULL DEFAULT timezone('utc', now());
    END IF;
END
$$;
");

            // SECTION: Refresh search helpers
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
BEGIN
    UPDATE ""ProjectDocuments"" d
    SET ""SearchVector"" = project_documents_build_search_vector(
        d.""Id"",
        d.""Title"",
        d.""Description"",
        d.""StageId"",
        d.""OriginalFileName""
    )
    WHERE d.""Id"" = NEW.""ProjectDocumentId"";

    RETURN NULL;
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER project_document_texts_search_vector_after
AFTER INSERT OR UPDATE ON ""ProjectDocumentTexts""
FOR EACH ROW
EXECUTE FUNCTION project_document_texts_search_vector_trigger();
");

            // SECTION: Backfill and index search data
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

        // SECTION: Revert schema fixes
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("Project document OCR pipeline requires PostgreSQL.");
            }

            // SECTION: Remove search helpers
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON ""ProjectDocumentTexts"";
DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);
");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_project_documents_search;");

            // SECTION: Drop search schema
            migrationBuilder.Sql(@"ALTER TABLE ""ProjectDocuments"" DROP COLUMN IF EXISTS ""SearchVector"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ProjectDocumentTexts"";");

            // SECTION: Revert OCR status to integer enum
            migrationBuilder.Sql(@"
ALTER TABLE ""ProjectDocuments"" ADD COLUMN IF NOT EXISTS ""OcrStatusLegacy"" integer NOT NULL DEFAULT 0;
");

            migrationBuilder.Sql(@"
UPDATE ""ProjectDocuments""
SET ""OcrStatusLegacy"" = CASE ""OcrStatus""
    WHEN 'Pending' THEN 1
    WHEN 'Succeeded' THEN 2
    WHEN 'Failed' THEN 3
    ELSE 0
END;
");

            migrationBuilder.Sql(@"ALTER TABLE ""ProjectDocuments"" DROP COLUMN IF EXISTS ""OcrStatus"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ProjectDocuments"" RENAME COLUMN ""OcrStatusLegacy"" TO ""OcrStatus"";");

            // SECTION: Shrink OCR failure reason length
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments' AND column_name = 'OcrFailureReason'
    ) THEN
        ALTER TABLE ""ProjectDocuments""
        ALTER COLUMN ""OcrFailureReason""
        TYPE character varying(512);
    END IF;
END
$$;
");
        }
    }
}
