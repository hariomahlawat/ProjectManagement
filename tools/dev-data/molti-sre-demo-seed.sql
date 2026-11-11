-- Demo MOLTI projects and SRE projects for a LOCAL DEVELOPMENT database (docs/ux/molti-sre-projects.md).
-- Twelve "Demo · ..." projects at different stages, some assigned to Project Officers, two with
-- no officer, one dropped, two delivered. Never run this against production.
--
-- Run:    psql -h localhost -p 5433 -U postgres -d ProjectManagement -v ON_ERROR_STOP=1 -f tools/dev-data/molti-sre-demo-seed.sql
-- Remove: tools/dev-data/molti-sre-demo-remove.sql
--
-- Safe to run twice: a project whose title already exists in its module is skipped. The officer
-- names are user names of the local database; one that does not exist leaves the project
-- unassigned. The creator is the first Admin or HoD found.
DO $$
DECLARE
    hod text := (
        SELECT u."Id"
        FROM "AspNetUsers" u
        JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
        JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
        WHERE r."Name" IN ('HoD', 'Admin') AND NOT u."IsDisabled"
        ORDER BY (r."Name" = 'HoD') DESC, u."UserName"
        LIMIT 1);
    rec record;
    pid int;
    codes text[] := ARRAY['PPP','AON','BID','TEC','COB','BM','PNC','SO','DLV'];
    i int;
    done_count int;
    step_date date;
BEGIN
    IF hod IS NULL THEN
        RAISE EXCEPTION 'No active HoD or Admin user to create the demo projects as.';
    END IF;

    FOR rec IN
        SELECT * FROM (VALUES
            -- module, title, reference, fy, officer, stages done, days ago the first stage was done, gap, so cost, ipa cost, dropped
            ('MOLTI','Demo · Thermal imaging sights, 2nd batch','MOLTI/03/2026',2026,'ankur',5,150,24,NULL::numeric,62.40,false),
            ('MOLTI','Demo · Field workshop tool kits','MOLTI/05/2026',2026,'ankur',2,60,20,NULL,18.75,false),
            ('MOLTI','Demo · Calibration rig for pressure gauges','MOLTI/08/2026',2026,'arpan',6,210,26,41.20,44.00,false),
            ('MOLTI','Demo · Ruggedised field laptops','MOLTI/09/2026',2026,'chinmay',3,95,28,NULL,27.30,false),
            ('MOLTI','Demo · Spares for mobile generator sets','MOLTI/12/2026',2026,NULL,1,20,0,NULL,9.80,false),
            ('MOLTI','Demo · Vehicle-mounted mast, 12 m','MOLTI/02/2025',2025,'arpan',9,400,35,88.60,90.00,false),
            ('MOLTI','Demo · Night training aids','MOLTI/06/2025',2025,'nishant',4,330,30,NULL,15.00,true),
            ('SRE','Demo · Vibration test fixture','SRE/01/2026',2026,'chinmay',4,120,27,NULL,33.50,false),
            ('SRE','Demo · Environmental chamber refurbishment','SRE/02/2026',2026,'nishant',6,170,25,NULL,120.00,false),
            ('SRE','Demo · Data logger set, 16 channel','SRE/04/2026',2026,'claude_review_po',2,45,18,NULL,12.60,false),
            ('SRE','Demo · High-speed camera lens kit','SRE/05/2026',2026,NULL,0,0,0,NULL,NULL,false),
            ('SRE','Demo · Torque wrench calibration bench','SRE/07/2025',2025,'nishant',9,380,34,21.90,22.50,false)
        ) AS t(module, title, reference, fy, officer, done, first_ago, gap, so_cost, ipa_cost, dropped)
    LOOP
        CONTINUE WHEN EXISTS (SELECT 1 FROM "ItProjects" WHERE "Module" = rec.module AND "Title" = rec.title);

        INSERT INTO "ItProjects" ("Module","Title","PppReferenceNumber","FinancialYearStart","Description","CreatedByUserId","CreatedAtUtc","UpdatedByUserId","UpdatedAtUtc","IsDeleted","RowVersion","ProjectOfficerUserId","ProjectOfficerAssignedAtUtc","DroppedOn","DroppedReason","DroppedByUserId")
        VALUES (rec.module, rec.title, rec.reference, rec.fy,
                'Demo record for checking the ' || rec.module || ' projects module. Scope, quantity and user are placeholders.',
                hod, now() - make_interval(days => rec.first_ago + 10), hod, now() - interval '2 days', false, decode(md5(random()::text), 'hex'),
                (SELECT "Id" FROM "AspNetUsers" WHERE "UserName" = rec.officer),
                CASE WHEN rec.officer IS NULL THEN NULL ELSE now() - make_interval(days => rec.first_ago + 5) END,
                CASE WHEN rec.dropped THEN current_date - 40 ELSE NULL END,
                CASE WHEN rec.dropped THEN 'Requirement merged into next year''s list.' ELSE NULL END,
                CASE WHEN rec.dropped THEN hod ELSE NULL END)
        RETURNING "Id" INTO pid;

        done_count := 0;
        FOR i IN 1..9 LOOP
            -- BM and PNC stay skipped, as on a new project; "done" counts the stages actually run.
            IF codes[i] IN ('BM','PNC') THEN
                INSERT INTO "ItProjectStages" ("ItProjectId","StageCode","SortOrder","IsCompleted","CompletedOn","IsSkipped","CostLakhs")
                VALUES (pid, codes[i], i, false, NULL, true, NULL);
            ELSE
                done_count := done_count + 1;
                step_date := current_date - (rec.first_ago - (done_count - 1) * rec.gap);
                INSERT INTO "ItProjectStages" ("ItProjectId","StageCode","SortOrder","IsCompleted","CompletedOn","IsSkipped","CostLakhs")
                VALUES (pid, codes[i], i,
                        done_count <= LEAST(rec.done, 7),
                        CASE WHEN done_count <= LEAST(rec.done, 7) THEN step_date ELSE NULL END,
                        false,
                        CASE WHEN codes[i] = 'PPP' THEN rec.ipa_cost WHEN codes[i] = 'SO' AND done_count <= LEAST(rec.done, 7) THEN rec.so_cost ELSE NULL END);
            END IF;
        END LOOP;
    END LOOP;
END $$;
SELECT "Module", count(*) AS projects FROM "ItProjects" WHERE NOT "IsDeleted" GROUP BY 1 ORDER BY 1;
