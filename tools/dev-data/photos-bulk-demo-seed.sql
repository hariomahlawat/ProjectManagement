-- A large demo photo library for scroll and performance checks of the Photos page (/Photos).
-- For a THROWAWAY database only: the script stops unless the database name starts with
-- "prism_review_". Never run it against production, a shared database or the normal dev database.
--
-- Run:    psql -h localhost -p 5433 -U postgres -d prism_review_<name> -v ON_ERROR_STOP=1 -f tools/dev-data/photos-bulk-demo-seed.sql
--         add  -v items=20000  for another size (default 6000, 1 to 500000)
-- Remove: tools/dev-data/photos-bulk-demo-remove.sql
--
-- What it does: copies the catalogue rows of project photos and project videos that the Photos
-- page shows today ("MediaAssets"; see MediaAssetVisibilityPolicy) as many times as asked. No
-- image files are added: each copy points at the stored file of the row it was copied from, so
-- thumbnails and the viewer still load.
--
--   * The copies belong to their own catalogue source, key "demo-bulk". The catalogue
--     synchroniser only looks after the "prism" source, where it marks every row it does not
--     find among the real uploads as unavailable; rows in a separate source are left alone.
--   * Each copy's "SourceEntityId" is "demo-bulk-<n>-project-photo:<photo id>". The page takes
--     the text after the first colon and the content providers the text after the last one, so
--     both still find the real photo; "ParentEntityId" stays the project that holds the file.
--   * Titles start with "Demo bulk". Dates are spread over 30 calendar months ending yesterday:
--     about a third of the days have nothing, most have 1 to 5 pictures, some about a dozen and
--     a few 40 or more. Every picture of one day belongs to one project, picked from the
--     existing projects, so the project filter and the day headings have work to do.
--   * About 3 in 100 are videos, if the database has a visible project video to copy.
--   * Width and height are replaced with a mix of landscape, portrait, square and wide shapes so
--     the grid lays out varied tiles. The picture itself keeps its real shape.
--   * The link "open in project" of a copy leads to the project that holds the file, which is
--     usually not the project the copy is shown under.
--
-- Safe to run twice: it removes the earlier "demo-bulk" source and its rows first, then builds
-- the set again (same picture of dates each time for the same size).
\set ON_ERROR_STOP on
\if :{?items}
\else
\set items 6000
\endif
SELECT set_config('demo_bulk.items', :'items', false) \gset demo_bulk_

DO $$
DECLARE
    wanted int := current_setting('demo_bulk.items')::int;
    months int := 30;
    first_day date := (date_trunc('month', current_date) - make_interval(months => months - 1))::date;
    last_day date := current_date - 1;
    demo_source uuid := gen_random_uuid();
    scan_id uuid := gen_random_uuid();
    photo_templates int;
    video_templates int;
    project_count int;
    inserted int;
BEGIN
    IF current_database() NOT LIKE 'prism\_review\_%' THEN
        RAISE EXCEPTION 'Refusing to run: database "%" is not a throwaway review database (its name must start with prism_review_).', current_database();
    END IF;
    IF wanted < 1 OR wanted > 500000 THEN
        RAISE EXCEPTION 'items must be between 1 and 500000 (got %).', wanted;
    END IF;

    -- The rows the Photos page shows today that a copy can stand in for.
    CREATE TEMP TABLE demo_bulk_templates ON COMMIT DROP AS
    SELECT a.*, row_number() OVER (PARTITION BY a."Kind" ORDER BY a."Id") AS pick
    FROM "MediaAssets" a
    JOIN "MediaLibrarySources" s ON s."Id" = a."SourceId"
    WHERE a."IsAvailable" AND a."AvailabilityStatus" = 'Available'
      AND NOT a."IsDeleted" AND NOT a."IsArchived" AND NOT s."IsDeleted"
      AND s."Key" = 'prism'
      AND a."Origin" IN ('ProjectPhoto', 'ProjectVideo')
      AND a."ParentEntityId" IS NOT NULL;

    SELECT count(*) FILTER (WHERE "Kind" = 'Photo'), count(*) FILTER (WHERE "Kind" = 'Video')
    INTO photo_templates, video_templates
    FROM demo_bulk_templates;
    IF photo_templates = 0 THEN
        RAISE EXCEPTION 'No visible project photo to copy. Upload a few project photos and let the catalogue pick them up first.';
    END IF;

    CREATE TEMP TABLE demo_bulk_projects ON COMMIT DROP AS
    SELECT p."Id", p."Name", row_number() OVER (ORDER BY p."Id") AS pick
    FROM "Projects" p
    WHERE NOT p."IsDeleted";
    SELECT count(*) INTO project_count FROM demo_bulk_projects;
    IF project_count = 0 THEN
        RAISE EXCEPTION 'No project to show the demo pictures under.';
    END IF;

    -- Start again from nothing; the rows of the earlier run go with their source.
    DELETE FROM "MediaLibrarySources" WHERE "Key" = 'demo-bulk';

    INSERT INTO "MediaLibrarySources" ("Id","Key","Name","SourceType","IsEnabled","IsReadOnly","IncludeSubfolders","ScanStatus","HealthStatus","IndexedAssetCount","CreatedAtUtc","UpdatedAtUtc","IsVisibleInLibrary","IsConfigurationManaged","IsDeleted")
    VALUES (demo_source, 'demo-bulk', 'Demo bulk photo library', 'Prism', false, true, false, 'Never', 'Internal', wanted, now(), now(), false, false, false);

    PERFORM setseed(0.42);

    -- One line per day: how busy it is and which project it belongs to.
    CREATE TEMP TABLE demo_bulk_days ON COMMIT DROP AS
    WITH days AS (
        SELECT d::date AS day, random() AS busy, 1 + floor(random() * project_count)::int AS project_pick
        FROM generate_series(first_day, last_day, interval '1 day') AS d
    ), weighted AS (
        SELECT day, project_pick,
               CASE WHEN busy < 0.35 THEN 0     -- nothing that day
                    WHEN busy < 0.80 THEN 1     -- 1 to 5 pictures
                    WHEN busy < 0.94 THEN 5     -- about a dozen
                    ELSE 25 END AS weight       -- 40 or more
        FROM days
    )
    SELECT day, project_pick, weight,
           sum(weight) OVER (ORDER BY day) - weight AS from_weight,
           sum(weight) OVER (ORDER BY day) AS to_weight,
           sum(weight) OVER () AS total_weight
    FROM weighted;

    WITH numbers AS (
        SELECT n,
               random() AS day_roll,
               random() AS kind_roll,
               random() AS template_roll,
               random() AS shape_roll,
               random() AS time_roll,
               random() AS length_roll
        FROM generate_series(1, wanted) AS n
    ), placed AS (
        SELECT numbers.*, d.day, d.project_pick
        FROM numbers
        JOIN demo_bulk_days d
          ON d.weight > 0
         AND numbers.day_roll * d.total_weight >= d.from_weight
         AND numbers.day_roll * d.total_weight < d.to_weight
    ), chosen AS (
        SELECT placed.*,
               CASE WHEN video_templates > 0 AND kind_roll < 0.03 THEN 'Video' ELSE 'Photo' END AS kind
        FROM placed
    ), shaped AS (
        SELECT chosen.*,
               (ARRAY[1600,1600,1600,1800,1920,1200,1200,1080,1400,2400])[1 + floor(shape_roll * 10)::int] AS width,
               (ARRAY[1200,1200,1200,1200,1080,1600,1800,1920,1400,1000])[1 + floor(shape_roll * 10)::int] AS height
        FROM chosen
    )
    INSERT INTO "MediaAssets"
    SELECT copy.*
    FROM shaped s
    JOIN demo_bulk_templates t
      ON t."Kind" = s.kind
     AND t.pick = 1 + floor(s.template_roll * CASE WHEN s.kind = 'Video' THEN video_templates ELSE photo_templates END)::int
    JOIN demo_bulk_projects p ON p.pick = s.project_pick
    -- One call per row; written as (...).* the record would be rebuilt for every column.
    CROSS JOIN LATERAL jsonb_populate_record(NULL::"MediaAssets", to_jsonb(t) - 'pick' || jsonb_build_object(
               'Id', nextval(pg_get_serial_sequence('"MediaAssets"', 'Id')),
               'SourceId', demo_source,
               'SourceEntityId', 'demo-bulk-' || lpad(s.n::text, 7, '0') || '-' || t."SourceEntityId",
               'ContextKey', 'project:' || p."Id",
               'CollectionKey', 'project:' || p."Id",
               'ContextTitle', left(p."Name", 300),
               'ProjectId', p."Id",
               'Title', left('Demo bulk ' || s.n || ' · ' || t."Title", 300),
               'QuickFingerprint', NULL,
               'ContentHash', NULL,
               'MediaDateUtc', (s.day::timestamp AT TIME ZONE 'UTC') + interval '3 hours' + s.time_roll * interval '10 hours',
               'IndexedAtUtc', now(),
               'LastSeenAtUtc', now(),
               'LastSeenScanId', scan_id,
               'Width', CASE WHEN s.kind = 'Video' THEN 1920 ELSE s.width END,
               'Height', CASE WHEN s.kind = 'Video' THEN 1080 ELSE s.height END,
               'DurationSeconds', CASE WHEN s.kind = 'Video' THEN 20 + floor(s.length_roll * 220)::int ELSE NULL END,
               'IsCover', false,
               'SortOrder', s.n,
               'EditorialCaption', NULL,
               'EditorialCaptionUpdatedByUserId', NULL,
               'EditorialCaptionUpdatedAtUtc', NULL,
               'EditorialConcurrencyToken', gen_random_uuid(),
               'ClassificationConcurrencyToken', gen_random_uuid(),
               'FaceAnalysisStatus', 'NotRequested',
               'FaceAnalysisVersion', NULL,
               'FaceAnalysedAtUtc', NULL,
               'FaceProcessingFailureReason', NULL)) AS copy;

    GET DIAGNOSTICS inserted = ROW_COUNT;
    UPDATE "MediaLibrarySources" SET "IndexedAssetCount" = inserted WHERE "Id" = demo_source;
    RAISE NOTICE 'Added % demo pictures and videos from % to % (copied from % photos and % videos, shown under % projects).',
        inserted, first_day, last_day, photo_templates, video_templates, project_count;
END $$;

ANALYZE "MediaAssets";

SELECT a."Kind", count(*) AS demo_items, min(a."MediaDateUtc")::date AS first_day, max(a."MediaDateUtc")::date AS last_day
FROM "MediaAssets" a
JOIN "MediaLibrarySources" s ON s."Id" = a."SourceId"
WHERE s."Key" = 'demo-bulk'
GROUP BY a."Kind"
ORDER BY a."Kind";
