-- Demo data for "Returned to you" on the PO briefing desk (LOCAL DEVELOPMENT DATABASES ONLY).
--
-- Adds eight projects named "Demo · Return N · …", each with a timeline plan the HoD returned,
-- shaped the way PlanApprovalService leaves the rows. Six show how a return can stay listed;
-- two are controls that should clear (resubmitted and pending, or corrected and approved).
-- Dates count back from the day the script runs. Does nothing if the projects already exist;
-- run returned-plans-demo-remove.sql first to refresh them.
--
-- Usage (names of existing users; nothing is created for them):
--   psql -v ON_ERROR_STOP=1 -v po_user=<PO user name> -v hod_user=<HoD user name> \
--        -v other_po_user=<another PO user name> -d <development database> \
--        -f tools/dev-data/returned-plans-demo-seed.sql
\encoding UTF8
\if :{?po_user}
\else
    \echo 'Pass -v po_user=… -v hod_user=… -v other_po_user=….'
    \quit
\endif
\if :{?hod_user}
\else
    \echo 'Pass -v po_user=… -v hod_user=… -v other_po_user=….'
    \quit
\endif
\if :{?other_po_user}
\else
    \echo 'Pass -v po_user=… -v hod_user=… -v other_po_user=….'
    \quit
\endif

SELECT set_config('prism_demo.po_user', :'po_user', false) AS po_user,
       set_config('prism_demo.hod_user', :'hod_user', false) AS hod_user,
       set_config('prism_demo.other_po_user', :'other_po_user', false) AS other_po_user
\gset prism_demo_

-- A project with the standard stages: FS to SO completed, DEVP in progress.
CREATE FUNCTION pg_temp.demo_project(name text, po text, hod text, lifecycle text) RETURNS integer
LANGUAGE plpgsql AS $$
DECLARE
    codes text[] := ARRAY['FS','SOW','IPA','AON','BID','TEC','BM','COB','PNC','EAS','SO','DEVP','ATP','PAYMENT','TOT'];
    pid integer;
    i integer;
BEGIN
    INSERT INTO "Projects" ("Name", "Description", "CreatedAt", "CreatedByUserId", "RowVersion", "LifecycleStatus",
                            "IsLegacy", "IsArchived", "IsDeleted", "IsBuild", "WorkflowVersion", "LeadPoUserId", "HodUserId",
                            "CompletedOn", "CompletedYear",
                            "CategoryId", "PncApplicable", "PublishedPlanningInputVersion", "FeaturedVideoVersion",
                            "ActivePlanVersionNo")
    VALUES (name, 'Demo project for testing "Returned to you" on the PO briefing desk.',
            current_date - 400, hod, gen_random_bytes(8), lifecycle, false, false, false, false, 'SDD-2.0', po, hod,
            CASE WHEN lifecycle = 'Completed' THEN current_date - 10 END,
            CASE WHEN lifecycle = 'Completed' THEN EXTRACT(YEAR FROM current_date - 10)::int END,
            (SELECT "CategoryId" FROM "Projects" WHERE "CategoryId" IS NOT NULL LIMIT 1), true, 0, 1, 1)
    RETURNING "Id" INTO pid;

    FOR i IN 1..array_length(codes, 1) LOOP
        INSERT INTO "ProjectStages" ("ProjectId", "StageCode", "SortOrder", "Status", "ActualStart", "CompletedOn",
                                     "PlannedStart", "PlannedDue", "IsAutoCompleted", "RequiresBackfill")
        VALUES (pid, codes[i], i - 1,
            CASE WHEN lifecycle = 'Completed' OR i <= 11 THEN 'Completed' WHEN i = 12 THEN 'InProgress' ELSE 'NotStarted' END,
            CASE WHEN lifecycle = 'Completed' OR i <= 12 THEN current_date - 380 + (i - 1) * 25 END,
            CASE WHEN lifecycle = 'Completed' OR i <= 11 THEN current_date - 380 + (i - 1) * 25 + 20 END,
            current_date - 380 + (i - 1) * 25,
            current_date - 380 + (i - 1) * 25 + 20 + CASE WHEN i >= 12 THEN 60 ELSE 0 END,
            false, false);
    END LOOP;
    RETURN pid;
END $$;

-- A plan version with the project's stage dates (shifted by shift_days for the stages still ahead,
-- so a correction differs from the approved plan when wanted).
CREATE FUNCTION pg_temp.demo_plan(pid integer, version_no integer, status text, owner text,
                                  submitted_by text, submitted_on timestamptz,
                                  approved_by text, approved_on timestamptz,
                                  rejected_by text, rejected_on timestamptz, note text,
                                  created_on timestamptz, shift_days integer DEFAULT 0) RETURNS integer
LANGUAGE plpgsql AS $$
DECLARE
    vid integer;
BEGIN
    INSERT INTO "PlanVersions" ("ProjectId", "OwnerUserId", "VersionNo", "Title", "Status", "CreatedByUserId", "CreatedOn",
                                "SubmittedByUserId", "SubmittedOn", "ApprovedByUserId", "ApprovedOn",
                                "RejectedByUserId", "RejectedOn", "RejectionNote",
                                "SkipWeekends", "TransitionRule", "PncApplicable", "SkipHolidays", "PlanningInputVersion")
    VALUES (pid, owner, version_no, 'Project Timeline', status, owner, created_on,
            submitted_by, submitted_on, approved_by, approved_on, rejected_by, rejected_on, note,
            true, 'NextWorkingDay', true, true, 0)
    RETURNING "Id" INTO vid;

    INSERT INTO "StagePlans" ("PlanVersionId", "StageCode", "PlannedStart", "PlannedDue", "DurationDays", "SortOrder")
    SELECT vid, s."StageCode",
           s."PlannedStart" + CASE WHEN s."SortOrder" >= 11 THEN shift_days ELSE 0 END,
           s."PlannedDue" + CASE WHEN s."SortOrder" >= 11 THEN shift_days ELSE 0 END,
           s."PlannedDue" - s."PlannedStart" + 1, s."SortOrder"
    FROM "ProjectStages" s WHERE s."ProjectId" = pid;
    RETURN vid;
END $$;

CREATE FUNCTION pg_temp.demo_log(vid integer, action text, note text, by_user text, at timestamptz) RETURNS void
LANGUAGE sql AS $$
    INSERT INTO "PlanApprovalLogs" ("PlanVersionId", "Action", "Note", "PerformedByUserId", "PerformedOn")
    VALUES (vid, action, note, by_user, at)
$$;

DO $$
DECLARE
    po    text := (SELECT "Id" FROM "AspNetUsers" WHERE "UserName" = current_setting('prism_demo.po_user'));
    hod   text := (SELECT "Id" FROM "AspNetUsers" WHERE "UserName" = current_setting('prism_demo.hod_user'));
    other text := (SELECT "Id" FROM "AspNetUsers" WHERE "UserName" = current_setting('prism_demo.other_po_user'));
    pid   integer;
    v     integer;
    now_  timestamptz := date_trunc('minute', now());
    -- Every project starts from an approved v1, submitted and approved 200 days ago.
    approved_at timestamptz := date_trunc('minute', now()) - interval '200 days';
BEGIN
    IF EXISTS (SELECT 1 FROM "Projects" WHERE "Name" LIKE 'Demo · Return %') THEN
        RAISE NOTICE 'Returned-plan demo data already present; run returned-plans-demo-remove.sql first to refresh it.';
        RETURN;
    END IF;
    IF po IS NULL OR hod IS NULL OR other IS NULL THEN
        RAISE EXCEPTION 'One of the users was not found.';
    END IF;

    -- 1. Returned with a note that asks for nothing (the production "dfgdfsh" case), 106 days ago.
    --    Current shape: the reviewed v2 is kept as Rejected and a correction draft v3 is opened.
    pid := pg_temp.demo_project('Demo · Return 1 · Returned with nothing to fix', po, hod, 'Active');
    v := pg_temp.demo_plan(pid, 1, 'Approved', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, approved_at - interval '1 day');
    v := pg_temp.demo_plan(pid, 2, 'Rejected', po, po, now_ - interval '107 days', NULL, NULL, hod, now_ - interval '106 days', 'dfgdfsh', now_ - interval '108 days', 30);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '107 days');
    PERFORM pg_temp.demo_log(v, 'Rejected', 'dfgdfsh', hod, now_ - interval '106 days');
    PERFORM pg_temp.demo_plan(pid, 3, 'Draft', po, NULL, NULL, NULL, NULL, NULL, NULL, NULL, now_ - interval '106 days', 30);

    -- 2. Returned 20 days ago; the PO discarded the correction draft and kept the approved plan.
    pid := pg_temp.demo_project('Demo · Return 2 · Correction draft discarded', po, hod, 'Active');
    v := pg_temp.demo_plan(pid, 1, 'Approved', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, approved_at - interval '1 day');
    v := pg_temp.demo_plan(pid, 2, 'Rejected', po, po, now_ - interval '22 days', NULL, NULL, hod, now_ - interval '20 days', 'Hold the DEVP extension until the trial report is in.', now_ - interval '23 days', 45);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '22 days');
    PERFORM pg_temp.demo_log(v, 'Rejected', 'Hold the DEVP extension until the trial report is in.', hod, now_ - interval '20 days');

    -- 3. Returned 25 days ago, resubmitted 20 days ago, then withdrawn by the PO 18 days ago.
    pid := pg_temp.demo_project('Demo · Return 3 · Resubmitted, then withdrawn', po, hod, 'Active');
    v := pg_temp.demo_plan(pid, 1, 'Approved', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, approved_at - interval '1 day');
    v := pg_temp.demo_plan(pid, 2, 'Rejected', po, po, now_ - interval '27 days', NULL, NULL, hod, now_ - interval '25 days', 'ATP date is too tight.', now_ - interval '28 days', 20);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '27 days');
    PERFORM pg_temp.demo_log(v, 'Rejected', 'ATP date is too tight.', hod, now_ - interval '25 days');
    v := pg_temp.demo_plan(pid, 3, 'Withdrawn', po, po, now_ - interval '20 days', NULL, NULL, NULL, NULL, NULL, now_ - interval '25 days', 35);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '20 days');
    PERFORM pg_temp.demo_log(v, 'Withdrawn', 'Submission withdrawn by its owner.', po, now_ - interval '18 days');
    PERFORM pg_temp.demo_plan(pid, 4, 'Draft', po, NULL, NULL, NULL, NULL, NULL, NULL, NULL, now_ - interval '18 days', 35);

    -- 4. Returned 39 days ago (like ICV BMP-II, 15 Sep); the project has since gone to another PO.
    pid := pg_temp.demo_project('Demo · Return 4 · Project handed to another PO', other, hod, 'Active');
    v := pg_temp.demo_plan(pid, 1, 'Approved', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, approved_at - interval '1 day');
    v := pg_temp.demo_plan(pid, 2, 'Rejected', po, po, now_ - interval '41 days', NULL, NULL, hod, now_ - interval '39 days', 'Align the PDC with the FFC delivery schedule.', now_ - interval '42 days', 25);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '41 days');
    PERFORM pg_temp.demo_log(v, 'Rejected', 'Align the PDC with the FFC delivery schedule.', hod, now_ - interval '39 days');
    PERFORM pg_temp.demo_plan(pid, 3, 'Draft', po, NULL, NULL, NULL, NULL, NULL, NULL, NULL, now_ - interval '39 days', 25);

    -- 5. Returned 50 days ago; the project was completed 10 days ago.
    pid := pg_temp.demo_project('Demo · Return 5 · Project since completed', po, hod, 'Completed');
    v := pg_temp.demo_plan(pid, 1, 'Approved', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, approved_at - interval '1 day');
    v := pg_temp.demo_plan(pid, 2, 'Rejected', po, po, now_ - interval '52 days', NULL, NULL, hod, now_ - interval '50 days', 'Payment stage dates need the CFA reference.', now_ - interval '53 days', 15);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '52 days');
    PERFORM pg_temp.demo_log(v, 'Rejected', 'Payment stage dates need the CFA reference.', hod, now_ - interval '50 days');
    PERFORM pg_temp.demo_plan(pid, 3, 'Draft', po, NULL, NULL, NULL, NULL, NULL, NULL, NULL, now_ - interval '50 days', 15);

    -- 6. Older shape, returned 60 days ago: the returned v2 went back to Draft itself (no "Submitted"
    --    log), so it cannot be discarded and only a resubmission clears it.
    pid := pg_temp.demo_project('Demo · Return 6 · Returned under the older rules', po, hod, 'Active');
    v := pg_temp.demo_plan(pid, 1, 'Approved', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    v := pg_temp.demo_plan(pid, 2, 'Draft', po, NULL, NULL, NULL, NULL, hod, now_ - interval '60 days', 'Revise EAS and SO durations.', now_ - interval '63 days', 10);
    PERFORM pg_temp.demo_log(v, 'Rejected', 'Revise EAS and SO durations.', hod, now_ - interval '60 days');

    -- 7. Control: returned 15 days ago, corrected and resubmitted 10 days ago, waiting for the HoD.
    --    Should be under "With others", not "Returned to you".
    pid := pg_temp.demo_project('Demo · Return 7 · Resubmitted, waiting for HoD', po, hod, 'Active');
    v := pg_temp.demo_plan(pid, 1, 'Approved', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, approved_at - interval '1 day');
    v := pg_temp.demo_plan(pid, 2, 'Rejected', po, po, now_ - interval '17 days', NULL, NULL, hod, now_ - interval '15 days', 'Show the slip against the approved PDC.', now_ - interval '18 days', 40);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '17 days');
    PERFORM pg_temp.demo_log(v, 'Rejected', 'Show the slip against the approved PDC.', hod, now_ - interval '15 days');
    v := pg_temp.demo_plan(pid, 3, 'PendingApproval', po, po, now_ - interval '10 days', NULL, NULL, NULL, NULL, NULL, now_ - interval '15 days', 30);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '10 days');

    -- 8. Control: returned 30 days ago, corrected, resubmitted and approved. Should not be listed.
    pid := pg_temp.demo_project('Demo · Return 8 · Corrected and approved', po, hod, 'Active');
    v := pg_temp.demo_plan(pid, 1, 'Superseded', po, po, approved_at - interval '1 day', hod, approved_at, NULL, NULL, NULL, approved_at - interval '2 days');
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, approved_at - interval '1 day');
    v := pg_temp.demo_plan(pid, 2, 'Rejected', po, po, now_ - interval '32 days', NULL, NULL, hod, now_ - interval '30 days', 'Add the PNC stage.', now_ - interval '33 days', 20);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '32 days');
    PERFORM pg_temp.demo_log(v, 'Rejected', 'Add the PNC stage.', hod, now_ - interval '30 days');
    v := pg_temp.demo_plan(pid, 3, 'Approved', po, po, now_ - interval '25 days', hod, now_ - interval '22 days', NULL, NULL, NULL, now_ - interval '30 days', 20);
    PERFORM pg_temp.demo_log(v, 'Submitted', NULL, po, now_ - interval '25 days');
    UPDATE "Projects" SET "ActivePlanVersionNo" = 3 WHERE "Id" = pid;
END $$;
