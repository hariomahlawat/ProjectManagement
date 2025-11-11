using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class FixProjectDocumentOcrSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("FixProjectDocumentOcrSchema migration requires PostgreSQL.");
            }

            // SECTION: Ensure OCR status column exists
            migrationBuilder.Sql(
                @"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments' AND column_name = 'OcrStatus'
    ) THEN
        ALTER TABLE ""ProjectDocuments""
        ADD COLUMN ""OcrStatus"" character varying(32) NOT NULL DEFAULT 'None';
    END IF;
END
$$;");

            // SECTION: Ensure OCR failure reason column exists
            migrationBuilder.Sql(
                @"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments' AND column_name = 'OcrFailureReason'
    ) THEN
        ALTER TABLE ""ProjectDocuments""
        ADD COLUMN ""OcrFailureReason"" character varying(1024);
    END IF;
END
$$;");

            // SECTION: Ensure search vector column exists
            migrationBuilder.Sql(
                @"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'ProjectDocuments' AND column_name = 'SearchVector'
    ) THEN
        ALTER TABLE ""ProjectDocuments""
        ADD COLUMN ""SearchVector"" tsvector;
    END IF;
END
$$;");

            // SECTION: Ensure OCR text table exists
            migrationBuilder.Sql(
                @"CREATE TABLE IF NOT EXISTS ""ProjectDocumentTexts"" (
    ""ProjectDocumentId"" integer PRIMARY KEY,
    ""OcrText"" text NULL,
    ""UpdatedAtUtc"" timestamp with time zone NOT NULL DEFAULT now() at time zone 'utc',
    CONSTRAINT ""FK_ProjectDocumentTexts_ProjectDocuments_ProjectDocumentId""
        FOREIGN KEY (""ProjectDocumentId"")
        REFERENCES ""ProjectDocuments""(""Id"")
        ON DELETE CASCADE
);");

            // SECTION: Seed search vector content for existing rows
            migrationBuilder.Sql(
                @"UPDATE ""ProjectDocuments""
SET ""SearchVector"" =
    setweight(to_tsvector('english', coalesce(""Title"", '')), 'A')
    ||
    setweight(to_tsvector('english', coalesce(""OriginalFileName"", '')), 'B');");

            // SECTION: Create search index
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_ProjectDocuments_SearchVector""
ON ""ProjectDocuments""
USING GIN (""SearchVector"");");

            // SECTION: Maintain search vector via trigger
            migrationBuilder.Sql(
                @"CREATE OR REPLACE FUNCTION project_documents_search_vector_update() RETURNS trigger AS $$
BEGIN
    NEW.""SearchVector"" :=
        setweight(to_tsvector('english', coalesce(NEW.""Title"", '')), 'A')
        ||
        setweight(to_tsvector('english', coalesce(NEW.""OriginalFileName"", '')), 'B');
    RETURN NEW;
END
$$ LANGUAGE plpgsql;");

            migrationBuilder.Sql(
                @"DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";
CREATE TRIGGER project_documents_search_vector_trigger
BEFORE INSERT OR UPDATE ON ""ProjectDocuments""
FOR EACH ROW EXECUTE PROCEDURE project_documents_search_vector_update();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("FixProjectDocumentOcrSchema migration requires PostgreSQL.");
            }

            // SECTION: Remove trigger and helper function
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON ""ProjectDocuments"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS project_documents_search_vector_update();");

            // SECTION: Drop supporting table
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ProjectDocumentTexts"";");

            // SECTION: Drop OCR/search columns
            migrationBuilder.Sql(@"ALTER TABLE ""ProjectDocuments"" DROP COLUMN IF EXISTS ""SearchVector"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ProjectDocuments"" DROP COLUMN IF EXISTS ""OcrFailureReason"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ProjectDocuments"" DROP COLUMN IF EXISTS ""OcrStatus"";");
        }
    }
}
