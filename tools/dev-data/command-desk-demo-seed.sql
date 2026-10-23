-- Demo data for the Command desk's "What moved forward" (LOCAL DEVELOPMENT DATABASES ONLY).
--
-- Adds two projects, their stages, a ToT, proliferation, three closed tasks, two IPRs, an FFC
-- record, two visits, a training and three activities. Every record is labelled "Demo · " (IPR
-- numbers "DEMO-"), and every date counts back from the day the script runs, so the records fall
-- inside the desk's 30-day window. Does nothing if the demo projects already exist; to refresh
-- the dates, run command-desk-demo-remove.sql first.
--
-- Usage (names of existing users; nothing is created for them):
--   psql -v ON_ERROR_STOP=1 -v po_user=<Project Officer user name> -v hod_user=<HoD user name> \
--        -d <development database> -f tools/dev-data/command-desk-demo-seed.sql
-- See docs/build-and-test.md, "Local demo data".
\encoding UTF8
\if :{?po_user}
\else
    \echo 'Pass -v po_user=<Project Officer user name> -v hod_user=<HoD user name>.'
    \quit
\endif
\if :{?hod_user}
\else
    \echo 'Pass -v po_user=<Project Officer user name> -v hod_user=<HoD user name>.'
    \quit
\endif

SELECT set_config('prism_demo.po_user', :'po_user', false) AS po_user,
       set_config('prism_demo.hod_user', :'hod_user', false) AS hod_user,
       -- The dates below are written as they were on 23 Oct 2026; they move with today's date.
       set_config('prism_demo.shift_days', (current_date - DATE '2026-10-23')::text, false) AS shift_days
\gset prism_demo_

CREATE FUNCTION pg_temp.demo_date(value date) RETURNS date LANGUAGE sql STABLE
    AS $$ SELECT value + current_setting('prism_demo.shift_days')::int $$;
CREATE FUNCTION pg_temp.demo_time(value timestamptz) RETURNS timestamptz LANGUAGE sql STABLE
    AS $$ SELECT value + make_interval(days => current_setting('prism_demo.shift_days')::int) $$;

DO $$
DECLARE
    po    text := (SELECT "Id" FROM "AspNetUsers" WHERE "UserName" = current_setting('prism_demo.po_user'));
    hod   text := (SELECT "Id" FROM "AspNetUsers" WHERE "UserName" = current_setting('prism_demo.hod_user'));
    a_id  integer;
    b_id  integer;
    codes text[] := ARRAY['FS','SOW','IPA','AON','BID','TEC','BM','COB','PNC','EAS','SO','DEVP','ATP','PAYMENT','TOT'];
    i     integer;
    ffc_year    integer;
    ffc_country integer;
BEGIN
    IF EXISTS (SELECT 1 FROM "Projects" WHERE "Name" LIKE 'Demo · %') THEN
        RAISE NOTICE 'Demo data already present; nothing to do. Run command-desk-demo-remove.sql first to refresh it.';
        RETURN;
    END IF;
    IF po IS NULL OR hod IS NULL THEN
        RAISE EXCEPTION 'User % or % not found.', current_setting('prism_demo.po_user'), current_setting('prism_demo.hod_user');
    END IF;

    -- A: active, two stages completed in the last fortnight (BM, COB), PNC in progress.
    INSERT INTO "Projects" ("Name", "Description", "CreatedAt", "CreatedByUserId", "RowVersion", "LifecycleStatus",
                            "IsLegacy", "IsArchived", "IsDeleted", "IsBuild", "WorkflowVersion", "LeadPoUserId", "HodUserId",
                            "CategoryId", "PncApplicable", "PublishedPlanningInputVersion", "FeaturedVideoVersion")
    VALUES ('Demo · Tank Driving Simulator Mk II', 'Demo project for testing the Command desk (What moved forward).',
            pg_temp.demo_date('2025-01-10'), hod, '\x01'::bytea, 'Active', false, false, false, false, 'SDD-2.0', po, hod,
            (SELECT "CategoryId" FROM "Projects" WHERE "CategoryId" IS NOT NULL LIMIT 1), true, 0, 1)
    RETURNING "Id" INTO a_id;

    FOR i IN 1..array_length(codes, 1) LOOP
        INSERT INTO "ProjectStages" ("ProjectId", "StageCode", "SortOrder", "Status", "ActualStart", "CompletedOn",
                                     "PlannedStart", "PlannedDue", "IsAutoCompleted", "RequiresBackfill")
        VALUES (a_id, codes[i], i - 1,
            CASE WHEN i <= 8 THEN 'Completed' WHEN i = 9 THEN 'InProgress' ELSE 'NotStarted' END,
            CASE WHEN i <= 6 THEN pg_temp.demo_date('2025-02-01') + (i - 1) * 45
                 WHEN i = 7 THEN pg_temp.demo_date('2026-09-20') WHEN i = 8 THEN pg_temp.demo_date('2026-10-10')
                 WHEN i = 9 THEN pg_temp.demo_date('2026-10-17') END,
            CASE WHEN i <= 6 THEN pg_temp.demo_date('2025-02-01') + (i - 1) * 45 + 40
                 WHEN i = 7 THEN pg_temp.demo_date('2026-10-09') WHEN i = 8 THEN pg_temp.demo_date('2026-10-16') END,
            CASE WHEN i = 9 THEN pg_temp.demo_date('2026-10-17') END,
            CASE WHEN i = 9 THEN pg_temp.demo_date('2026-11-15') END,
            false, false);
    END LOOP;

    -- B: completed 15 days ago (Payment before that), ToT completed 8 days ago, units proliferated.
    INSERT INTO "Projects" ("Name", "Description", "CreatedAt", "CreatedByUserId", "RowVersion", "LifecycleStatus",
                            "IsLegacy", "IsArchived", "IsDeleted", "IsBuild", "WorkflowVersion", "LeadPoUserId", "HodUserId",
                            "CompletedOn", "CompletedYear", "CompletedMonth",
                            "CategoryId", "PncApplicable", "PublishedPlanningInputVersion", "FeaturedVideoVersion")
    VALUES ('Demo · Weapon Training Simulator (Rifle)', 'Demo project for testing the Command desk (What moved forward).',
            pg_temp.demo_date('2024-06-01'), hod, '\x02'::bytea, 'Completed', false, false, false, false, 'SDD-2.0', po, hod,
            pg_temp.demo_date('2026-10-08'), EXTRACT(YEAR FROM pg_temp.demo_date('2026-10-08'))::int,
            NULL, -- an exact completion date carries no separate month
            (SELECT "CategoryId" FROM "Projects" WHERE "CategoryId" IS NOT NULL LIMIT 1), true, 0, 1)
    RETURNING "Id" INTO b_id;

    FOR i IN 1..array_length(codes, 1) LOOP
        INSERT INTO "ProjectStages" ("ProjectId", "StageCode", "SortOrder", "Status", "ActualStart", "CompletedOn",
                                     "IsAutoCompleted", "RequiresBackfill")
        VALUES (b_id, codes[i], i - 1,
            CASE WHEN codes[i] = 'PNC' THEN 'Skipped' ELSE 'Completed' END,
            CASE WHEN codes[i] = 'PNC' THEN NULL
                 WHEN codes[i] = 'PAYMENT' THEN pg_temp.demo_date('2026-09-25')
                 WHEN codes[i] = 'TOT' THEN pg_temp.demo_date('2026-09-01')
                 ELSE pg_temp.demo_date('2024-07-01') + (i - 1) * 50 END,
            CASE WHEN codes[i] = 'PNC' THEN NULL
                 WHEN codes[i] = 'PAYMENT' THEN pg_temp.demo_date('2026-10-05')
                 WHEN codes[i] = 'TOT' THEN pg_temp.demo_date('2026-10-15')
                 ELSE pg_temp.demo_date('2024-07-01') + (i - 1) * 50 + 45 END,
            false, false);
    END LOOP;

    INSERT INTO "ProjectTots" ("ProjectId", "Status", "StartedOn", "CompletedOn", "LastApprovedByUserId", "LastApprovedOnUtc")
    VALUES (b_id, 'Completed', pg_temp.demo_date('2026-09-01'), pg_temp.demo_date('2026-10-15'), hod,
            pg_temp.demo_time('2026-10-15 06:00+00'));

    INSERT INTO "ProliferationGranular" ("Id", "ProjectId", "Source", "UnitName", "ProliferationDate", "Quantity",
                                         "ApprovalStatus", "SubmittedByUserId", "ApprovedByUserId", "ApprovedOnUtc",
                                         "CreatedOnUtc", "LastUpdatedOnUtc", "RowVersion", "Remarks")
    VALUES (gen_random_uuid(), b_id, 1, 'Demo · Infantry School, Mhow', pg_temp.demo_date('2026-10-10'), 12, 1, po, hod,
            pg_temp.demo_time('2026-10-11 05:00+00'), pg_temp.demo_time('2026-10-10 09:00+00'), pg_temp.demo_time('2026-10-11 05:00+00'),
            '\x03'::bytea, 'Demo data'),
           (gen_random_uuid(), b_id, 1, 'Demo · Regimental Centre, Belgaum', pg_temp.demo_date('2026-10-20'), 6, 1, po, hod,
            pg_temp.demo_time('2026-10-21 05:00+00'), pg_temp.demo_time('2026-10-20 09:00+00'), pg_temp.demo_time('2026-10-21 05:00+00'),
            '\x04'::bytea, 'Demo data');

    -- Three closed tasks (assigned to the PO by the HoD). The Command desk no longer counts
    -- closed tasks; they remain useful in the task register.
    INSERT INTO "ActionTasks" ("Title", "Description", "CreatedByUserId", "AssignedToUserId", "CreatedByRole", "AssignedToRole",
                               "AssignedOn", "DueDate", "Priority", "Status", "SubmittedOn", "ClosedOn")
    VALUES ('Demo · Collect user feedback on Mk II cabin', 'Demo task (closed).', hod, po, 'HoD', 'Project Officer',
            pg_temp.demo_time('2026-09-28 04:30+00'), pg_temp.demo_date('2026-10-10'), 'Normal', 'Closed',
            pg_temp.demo_time('2026-10-08 10:00+00'), pg_temp.demo_time('2026-10-09 06:00+00')),
           ('Demo · Draft ToT handover note for rifle simulator', 'Demo task (closed).', hod, po, 'HoD', 'Project Officer',
            pg_temp.demo_time('2026-10-01 04:30+00'), pg_temp.demo_date('2026-10-14'), 'High', 'Closed',
            pg_temp.demo_time('2026-10-13 10:00+00'), pg_temp.demo_time('2026-10-14 06:00+00')),
           ('Demo · Update proliferation register', 'Demo task (closed).', hod, po, 'HoD', 'Project Officer',
            pg_temp.demo_time('2026-10-12 04:30+00'), pg_temp.demo_date('2026-10-21'), 'Normal', 'Closed',
            pg_temp.demo_time('2026-10-20 10:00+00'), pg_temp.demo_time('2026-10-21 06:00+00'));

    -- IPR: one filed 17 days ago, one granted 9 days ago.
    INSERT INTO "IprRecords" ("IprFilingNumber", "Title", "Type", "Status", "FiledAtUtc", "GrantedAtUtc", "RowVersion", "ProjectId")
    VALUES ('DEMO-0001', 'Demo · Haptic recoil mechanism for training rifles', 'Patent', 'Filed',
            pg_temp.demo_time('2026-10-06 05:30+00'), NULL, '\x05'::bytea, b_id),
           ('DEMO-0002', 'Demo · Motion cueing algorithm for driving simulators', 'Patent', 'Granted',
            pg_temp.demo_time('2025-03-12 05:30+00'), pg_temp.demo_time('2026-10-14 05:30+00'), '\x06'::bytea, a_id);

    -- FFC: one unit delivered 14 days ago and installed 4 days ago. Only one active record is
    -- allowed per country and year, so use a country with none that year (Myanmar if free).
    ffc_year := EXTRACT(YEAR FROM pg_temp.demo_date('2026-10-09'))::int;
    SELECT country."Id" INTO ffc_country
    FROM "FfcCountries" country
    WHERE NOT EXISTS (SELECT 1 FROM "FfcRecords" record
                      WHERE record."CountryId" = country."Id" AND record."Year" = ffc_year AND NOT record."IsDeleted")
    ORDER BY (country."Name" = 'Myanmar') DESC, country."Id"
    LIMIT 1;
    IF ffc_country IS NULL THEN
        RAISE NOTICE 'Every FFC country already has a record for %; the demo FFC record was skipped.', ffc_year;
    ELSE
        INSERT INTO "FfcRecords" ("CountryId", "Year", "DeliveryYes", "DeliveryDate", "DeliveryRemarks",
                                  "InstallationYes", "InstallationDate", "InstallationRemarks", "OverallRemarks", "CreatedByUserId", "RowVersion")
        VALUES (ffc_country, ffc_year,
                true, pg_temp.demo_date('2026-10-09'), 'Demo data', true, pg_temp.demo_date('2026-10-19'), 'Demo data',
                'Demo · record for the Command desk', hod, '\x07'::bytea);
    END IF;

    RAISE NOTICE 'Demo projects added: % and %.', a_id, b_id;
END $$;

-- Institutional outputs: visits, a training and activities (industry, academia, lecture).
-- Countries and visit, training and activity types are looked up by name where they exist;
-- otherwise the first one is used.
DO $$
DECLARE
    hod  text := (SELECT "Id" FROM "AspNetUsers" WHERE "UserName" = current_setting('prism_demo.hod_user'));
    t_id uuid := gen_random_uuid();
BEGIN
    IF EXISTS (SELECT 1 FROM "Visits" WHERE "VisitorName" LIKE 'Demo · %') THEN
        RAISE NOTICE 'Demo institutional outputs already present; nothing to do.';
        RETURN;
    END IF;
    IF hod IS NULL THEN
        RAISE EXCEPTION 'User % not found.', current_setting('prism_demo.hod_user');
    END IF;

    INSERT INTO "Visits" ("Id", "VisitTypeId", "DateOfVisit", "VisitorName", "Strength", "Remarks", "CreatedByUserId", "CreatedAtUtc", "RowVersion")
    VALUES (gen_random_uuid(), COALESCE((SELECT "Id" FROM "VisitTypes" WHERE "Name" = 'Course Visits'), (SELECT "Id" FROM "VisitTypes" ORDER BY "Name" LIMIT 1)), pg_temp.demo_date('2026-10-07'),
            'Demo · Young Officers'' Course', 30, 'Demo data', hod, pg_temp.demo_time('2026-10-07 10:00+00'), '\x11'::bytea),
           (gen_random_uuid(), COALESCE((SELECT "Id" FROM "VisitTypes" WHERE "Name" = 'Mil Dignitaries'), (SELECT "Id" FROM "VisitTypes" ORDER BY "Name" LIMIT 1)), pg_temp.demo_date('2026-10-17'),
            'Demo · Visit by a senior commander', 6, 'Demo data', hod, pg_temp.demo_time('2026-10-17 10:00+00'), '\x12'::bytea);

    INSERT INTO "Trainings" ("Id", "TrainingTypeId", "StartDate", "EndDate", "LegacyOfficerCount", "LegacyJcoCount", "LegacyOrCount",
                             "Notes", "CreatedByUserId", "CreatedAtUtc", "RowVersion")
    VALUES (t_id, COALESCE((SELECT "Id" FROM "TrainingTypes" WHERE "Name" = 'Simulator'), (SELECT "Id" FROM "TrainingTypes" ORDER BY "Name" LIMIT 1)),
            pg_temp.demo_date('2026-10-12'), pg_temp.demo_date('2026-10-16'), 0, 0, 0,
            'Demo · Simulator operators'' cadre', hod, pg_temp.demo_time('2026-10-16 10:00+00'), '\x13'::bytea);
    INSERT INTO "TrainingCounters" ("TrainingId", "Officers", "JuniorCommissionedOfficers", "OtherRanks", "Total", "Source", "UpdatedAtUtc", "RowVersion")
    VALUES (t_id, 8, 12, 40, 60, 'Roster', pg_temp.demo_time('2026-10-16 10:00+00'), '\x14'::bytea);

    INSERT INTO "Activities" ("Title", "Description", "Location", "ScheduledStartUtc", "ScheduledEndUtc", "ActivityTypeId",
                              "CreatedByUserId", "CreatedAtUtc", "IsDeleted", "RowVersion")
    VALUES ('Demo · Meeting with an industry partner on motion platforms', 'Demo data', 'SDD',
            pg_temp.demo_time('2026-10-08 05:30+00'), pg_temp.demo_time('2026-10-08 07:30+00'),
            COALESCE((SELECT "Id" FROM "ActivityTypes" WHERE "Name" = 'Industry Interaction'), (SELECT min("Id") FROM "ActivityTypes")),
            hod, pg_temp.demo_time('2026-10-08 08:00+00'), false, '\x15'::bytea),
           ('Demo · University workshop on simulation and VR', 'Demo data', 'University campus',
            pg_temp.demo_time('2026-10-14 04:30+00'), pg_temp.demo_time('2026-10-14 10:30+00'),
            COALESCE((SELECT "Id" FROM "ActivityTypes" WHERE "Name" = 'Academia Interaction'), (SELECT min("Id") FROM "ActivityTypes")),
            hod, pg_temp.demo_time('2026-10-14 11:00+00'), false, '\x16'::bytea),
           ('Demo · Lecture on AI in training simulators', 'Demo data', 'SDD',
            pg_temp.demo_time('2026-10-20 06:30+00'), pg_temp.demo_time('2026-10-20 08:00+00'),
            COALESCE((SELECT "Id" FROM "ActivityTypes" WHERE "Name" = 'Seminar/ Lecture'), (SELECT min("Id") FROM "ActivityTypes")),
            hod, pg_temp.demo_time('2026-10-20 08:30+00'), false, '\x17'::bytea);

    RAISE NOTICE 'Demo institutional outputs added: 2 visits, 1 training (60 people), 3 activities.';
END $$;
