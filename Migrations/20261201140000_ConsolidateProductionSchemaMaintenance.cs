using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectManagement.Data;

#nullable disable

namespace ProjectManagement.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261201140000_ConsolidateProductionSchemaMaintenance")]
    public partial class ConsolidateProductionSchemaMaintenance : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    -- Consolidate legacy startup repairs into one migration-owned operation.
                    ALTER TABLE "ProjectStages"
                        ADD COLUMN IF NOT EXISTS "AutoCompletedFromCode" character varying(16);

                    ALTER TABLE "ProjectStages"
                        ADD COLUMN IF NOT EXISTS "IsAutoCompleted" boolean;

                    ALTER TABLE "ProjectStages"
                        ADD COLUMN IF NOT EXISTS "RequiresBackfill" boolean;

                    UPDATE "ProjectStages"
                    SET "IsAutoCompleted" = FALSE
                    WHERE "IsAutoCompleted" IS NULL;

                    UPDATE "ProjectStages"
                    SET "RequiresBackfill" = FALSE
                    WHERE "RequiresBackfill" IS NULL;

                    ALTER TABLE "ProjectStages"
                        ALTER COLUMN "IsAutoCompleted" SET DEFAULT FALSE,
                        ALTER COLUMN "IsAutoCompleted" SET NOT NULL,
                        ALTER COLUMN "RequiresBackfill" SET DEFAULT FALSE,
                        ALTER COLUMN "RequiresBackfill" SET NOT NULL;

                    ALTER TABLE "SocialMediaEvents"
                        DROP COLUMN IF EXISTS "Reach";
                    """);

                migrationBuilder.Sql(
                    """
                    -- Project-document full-text-search infrastructure is migration-owned.
                    DROP TRIGGER IF EXISTS project_document_texts_search_vector_after ON "ProjectDocumentTexts";
                    DROP FUNCTION IF EXISTS project_document_texts_search_vector_trigger();
                    DROP TRIGGER IF EXISTS project_documents_search_vector_trigger ON "ProjectDocuments";
                    DROP FUNCTION IF EXISTS project_documents_search_vector_trigger();
                    DROP FUNCTION IF EXISTS project_documents_search_vector_update();
                    DROP FUNCTION IF EXISTS project_documents_build_search_vector(integer, text, text, integer, text);

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

                        UPDATE "ProjectDocuments" AS document
                        SET "SearchVector" = project_documents_build_search_vector(
                            document."Id",
                            document."Title",
                            document."Description",
                            document."StageId",
                            document."OriginalFileName"
                        )
                        WHERE document."Id" = v_document_id;

                        RETURN NULL;
                    END;
                    $$;

                    CREATE TRIGGER project_document_texts_search_vector_after
                    AFTER INSERT OR UPDATE OR DELETE ON "ProjectDocumentTexts"
                    FOR EACH ROW
                    EXECUTE FUNCTION project_document_texts_search_vector_trigger();

                    CREATE INDEX IF NOT EXISTS "IX_ProjectDocuments_SearchVector"
                        ON "ProjectDocuments" USING GIN ("SearchVector");

                    -- Run once as part of this migration, not on every application restart.
                    UPDATE "ProjectDocuments" AS document
                    SET "SearchVector" = project_documents_build_search_vector(
                        document."Id",
                        document."Title",
                        document."Description",
                        document."StageId",
                        document."OriginalFileName"
                    );
                    """);
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF COL_LENGTH(N'dbo.ProjectStages', 'AutoCompletedFromCode') IS NULL
                        ALTER TABLE [ProjectStages] ADD [AutoCompletedFromCode] nvarchar(16) NULL;

                    IF COL_LENGTH(N'dbo.ProjectStages', 'IsAutoCompleted') IS NULL
                        ALTER TABLE [ProjectStages] ADD [IsAutoCompleted] bit NULL;

                    IF COL_LENGTH(N'dbo.ProjectStages', 'RequiresBackfill') IS NULL
                        ALTER TABLE [ProjectStages] ADD [RequiresBackfill] bit NULL;

                    UPDATE [ProjectStages]
                    SET [IsAutoCompleted] = 0
                    WHERE [IsAutoCompleted] IS NULL;

                    UPDATE [ProjectStages]
                    SET [RequiresBackfill] = 0
                    WHERE [RequiresBackfill] IS NULL;

                    IF COL_LENGTH(N'dbo.SocialMediaEvents', 'Reach') IS NOT NULL
                        ALTER TABLE [SocialMediaEvents] DROP COLUMN [Reach];
                    """);
            }
            // SQLite is used only for lightweight tests. Its schema is created from the model.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This is an idempotent production-reconciliation migration. It intentionally does
            // not remove columns or search infrastructure that were already introduced by older
            // migrations. Roll forward with a corrective migration instead of reintroducing drift.
        }
    }
}
