using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace ProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddDocRepoFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("DocRepo full-text search requires PostgreSQL.");
            }

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Documents",
                type: "tsvector",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocRepoDocumentTexts",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OcrText = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocRepoDocumentTexts", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocRepoDocumentTexts_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
INSERT INTO \"DocRepoDocumentTexts\" (\"DocumentId\", \"OcrText\", \"UpdatedAtUtc\")
SELECT \"Id\", \"ExtractedText\", COALESCE(\"UpdatedAtUtc\", \"CreatedAtUtc\", now() AT TIME ZONE 'utc')
FROM \"Documents\"
WHERE \"ExtractedText\" IS NOT NULL;
");

            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "Documents");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION docrepo_documents_build_search_vector(
    p_document_id uuid,
    p_subject text,
    p_received_from text,
    p_office_category_id integer,
    p_document_category_id integer)
RETURNS tsvector
LANGUAGE plpgsql
AS $$
DECLARE
    v_office_name text;
    v_category_name text;
    v_tag_names text;
    v_ocr text;
BEGIN
    SELECT \"Name\" INTO v_office_name FROM \"OfficeCategories\" WHERE \"Id\" = p_office_category_id;
    SELECT \"Name\" INTO v_category_name FROM \"DocumentCategories\" WHERE \"Id\" = p_document_category_id;

    SELECT string_agg(t.\"Name\", ' ')
    INTO v_tag_names
    FROM \"DocumentTags\" dt
    JOIN \"Tags\" t ON t.\"Id\" = dt.\"TagId\"
    WHERE dt.\"DocumentId\" = p_document_id;

    SELECT \"OcrText\"
    INTO v_ocr
    FROM \"DocRepoDocumentTexts\"
    WHERE \"DocumentId\" = p_document_id;

    RETURN
        setweight(to_tsvector('english', coalesce(p_subject, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(p_received_from, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(v_tag_names, '')), 'B') ||
        setweight(to_tsvector('english', concat_ws(' ', v_office_name, v_category_name)), 'C') ||
        setweight(to_tsvector('english', coalesce(v_ocr, '')), 'D');
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION docrepo_documents_search_vector_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.\"SearchVector\" = docrepo_documents_build_search_vector(
        NEW.\"Id\",
        NEW.\"Subject\",
        NEW.\"ReceivedFrom\",
        NEW.\"OfficeCategoryId\",
        NEW.\"DocumentCategoryId\");
    RETURN NEW;
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER docrepo_documents_search_vector_before
BEFORE INSERT OR UPDATE ON \"Documents\"
FOR EACH ROW EXECUTE FUNCTION docrepo_documents_search_vector_trigger();
");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION docrepo_document_tags_search_vector_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_document_id uuid;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_document_id := OLD.\"DocumentId\";
    ELSE
        v_document_id := NEW.\"DocumentId\";
    END IF;

    UPDATE \"Documents\" d
    SET \"SearchVector\" = docrepo_documents_build_search_vector(
        d.\"Id\",
        d.\"Subject\",
        d.\"ReceivedFrom\",
        d.\"OfficeCategoryId\",
        d.\"DocumentCategoryId\")
    WHERE d.\"Id\" = v_document_id;

    RETURN NULL;
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER docrepo_document_tags_search_vector_after
AFTER INSERT OR UPDATE OR DELETE ON \"DocumentTags\"
FOR EACH ROW EXECUTE FUNCTION docrepo_document_tags_search_vector_trigger();
");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION docrepo_document_texts_search_vector_trigger()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_document_id uuid;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_document_id := OLD.\"DocumentId\";
    ELSE
        v_document_id := NEW.\"DocumentId\";
    END IF;

    UPDATE \"Documents\" d
    SET \"SearchVector\" = docrepo_documents_build_search_vector(
        d.\"Id\",
        d.\"Subject\",
        d.\"ReceivedFrom\",
        d.\"OfficeCategoryId\",
        d.\"DocumentCategoryId\")
    WHERE d.\"Id\" = v_document_id;

    RETURN NULL;
END;
$$;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER docrepo_document_texts_search_vector_after
AFTER INSERT OR UPDATE OR DELETE ON \"DocRepoDocumentTexts\"
FOR EACH ROW EXECUTE FUNCTION docrepo_document_texts_search_vector_trigger();
");

            migrationBuilder.Sql(@"
UPDATE \"Documents\" d
SET \"SearchVector\" = docrepo_documents_build_search_vector(
    d.\"Id\",
    d.\"Subject\",
    d.\"ReceivedFrom\",
    d.\"OfficeCategoryId\",
    d.\"DocumentCategoryId\");
");

            migrationBuilder.Sql(@"
CREATE INDEX idx_docrepo_documents_search
    ON \"Documents\"
    USING GIN (\"SearchVector\");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                throw new NotSupportedException("DocRepo full-text search requires PostgreSQL.");
            }

            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS idx_docrepo_documents_search;
");

            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS docrepo_document_texts_search_vector_after ON \"DocRepoDocumentTexts\";
DROP TRIGGER IF EXISTS docrepo_document_tags_search_vector_after ON \"DocumentTags\";
DROP TRIGGER IF EXISTS docrepo_documents_search_vector_before ON \"Documents\";
");

            migrationBuilder.Sql(@"
DROP FUNCTION IF EXISTS docrepo_document_texts_search_vector_trigger();
DROP FUNCTION IF EXISTS docrepo_document_tags_search_vector_trigger();
DROP FUNCTION IF EXISTS docrepo_documents_search_vector_trigger();
DROP FUNCTION IF EXISTS docrepo_documents_build_search_vector(uuid, text, text, integer, integer);
");

            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE \"Documents\" d
SET \"ExtractedText\" = t.\"OcrText\"
FROM \"DocRepoDocumentTexts\" t
WHERE d.\"Id\" = t.\"DocumentId\";
");

            migrationBuilder.DropTable(
                name: "DocRepoDocumentTexts");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Documents");
        }
    }
}
