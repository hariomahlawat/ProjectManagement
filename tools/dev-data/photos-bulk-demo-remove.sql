-- Removes the large demo photo library added by photos-bulk-demo-seed.sql: the catalogue source
-- with key "demo-bulk" and every row under it (titles "Demo bulk ..."), with their album items,
-- jobs and faces (they cascade). No files are touched: the copies never had files of their own.
-- For a THROWAWAY database only: the script stops unless the database name starts with
-- "prism_review_".
--
-- Run: psql -h localhost -p 5433 -U postgres -d prism_review_<name> -v ON_ERROR_STOP=1 -f tools/dev-data/photos-bulk-demo-remove.sql
\set ON_ERROR_STOP on
DO $$
DECLARE
    removed bigint;
BEGIN
    IF current_database() NOT LIKE 'prism\_review\_%' THEN
        RAISE EXCEPTION 'Refusing to run: database "%" is not a throwaway review database (its name must start with prism_review_).', current_database();
    END IF;

    SELECT count(*) INTO removed
    FROM "MediaAssets" a
    JOIN "MediaLibrarySources" s ON s."Id" = a."SourceId"
    WHERE s."Key" = 'demo-bulk';

    DELETE FROM "MediaLibrarySources" WHERE "Key" = 'demo-bulk';
    RAISE NOTICE 'Removed % demo pictures and videos.', removed;
END $$;

ANALYZE "MediaAssets";

SELECT count(*) AS catalogue_rows_left FROM "MediaAssets";
