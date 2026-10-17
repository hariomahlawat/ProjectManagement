using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.ViewModels;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

// Runs inside the single guarded PostgreSQL test, after its existing down/up cases.
// Every seed statement targets only columns available in the previous application.
internal static class ProposalOwnedPlanningCutoverPostgresFixture
{
    public static async Task RunAsync(ApplicationDbContext db, IMigrator migrator)
    {
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(ProposalOwnedPlanningMigrationTests.PreviousBoundary);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "AccessFailedCount", "EmailConfirmed", "LockoutEnabled", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "MustChangePassword", "FullName", "Rank", "LoginCount", "CreatedUtc", "IsDisabled", "PendingDeletion", "ShowCelebrationsInCalendar")
            SELECT actor, actor, 0, FALSE, FALSE, FALSE, FALSE, FALSE, '', '', 0, TIMESTAMP '2026-10-16 00:00:00', FALSE, FALSE, TRUE
            FROM (VALUES ('cutover-a'), ('cutover-b'), ('cutover-hod')) AS users(actor);
            """);
        var projects = new List<int>();
        for (var scenario = 0; scenario < 4; scenario++)
        {
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                INSERT INTO "Projects" ("Name", "CreatedAt", "CreatedByUserId", "RowVersion", "WorkflowVersion", "PncApplicable", "LeadPoUserId")
                VALUES ('Previous-master planning cutover', TIMESTAMP '2026-10-16 00:00:00', 'system', '\x'::bytea, 'v1', FALSE, 'cutover-a')
                RETURNING "Id";
                """;
            var id = Convert.ToInt32(await command.ExecuteScalarAsync());
            projects.Add(id);
            // Official timeline remains unchanged by the old Duration SaveDraft/Calculate.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "ProjectStages" ("ProjectId", "StageCode", "SortOrder", "Status", "PlannedStart", "PlannedDue", "RequiresBackfill")
                VALUES ({0}, 'FS', 888, 'NotStarted', DATE '2026-11-02', DATE '2026-11-06', FALSE),
                       ({0}, 'PNC', 999, 'NotStarted', DATE '2024-01-01', DATE '2024-01-15', FALSE);
                INSERT INTO "ProjectScheduleSettings" ("ProjectId", "IncludeWeekends", "SkipHolidays", "NextStageStartPolicy", "AnchorStart")
                VALUES ({0}, TRUE, FALSE, 'SameDay', DATE '2035-01-01');
                INSERT INTO "ProjectPlanDurations" ("ProjectId", "StageCode", "DurationDays", "SortOrder")
                VALUES ({0}, 'FS', 99, 777), ({0}, 'PNC', 15, 999), ({0}, 'PRIVATE', 45, 1000);
                INSERT INTO "PlanVersions" ("ProjectId", "VersionNo", "Title", "Status", "CreatedByUserId", "CreatedOn", "SkipWeekends", "TransitionRule", "PncApplicable")
                VALUES ({0}, 1, 'Old publication', 'Approved', 'cutover-a', NOW(), TRUE, 'NextWorkingDay', FALSE);
                INSERT INTO "StagePlans" ("PlanVersionId", "StageCode", "DurationDays", "PlannedStart", "PlannedDue")
                SELECT "Id", 'FS', 5, DATE '2026-11-02', DATE '2026-11-06' FROM "PlanVersions" WHERE "ProjectId" = {0} AND "VersionNo" = 1;
                """, id);
            if (scenario < 2)
            {
                // Previous Duration SaveDraft/Calculate copied posted policies and
                // shared durations into the draft, then generated its planned dates.
                var fsStart = new DateOnly(2035, 1, 1);
                var fsDue = fsStart.AddDays(99 - 1);
                var pncStart = fsDue; // SameDay transition with weekends included.
                var pncDue = pncStart.AddDays(15 - 1);
                var extensionStart = pncDue;
                var extensionDue = extensionStart.AddDays(45 - 1);
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "PlanVersions" ("ProjectId", "VersionNo", "Title", "Status", "OwnerUserId", "CreatedByUserId", "CreatedOn", "AnchorDate", "SkipWeekends", "TransitionRule", "PncApplicable")
                    VALUES ({0}, 2, 'Old Duration working revision', 'Draft', 'cutover-a', 'cutover-a', TIMESTAMPTZ '2026-10-16 00:00:00+00', {1}, FALSE, 'SameDay', TRUE);
                    INSERT INTO "StagePlans" ("PlanVersionId", "StageCode", "DurationDays", "PlannedStart", "PlannedDue")
                    SELECT "Id", 'FS', 99, {1}, {2} FROM "PlanVersions" WHERE "ProjectId" = {0} AND "VersionNo" = 2;
                    INSERT INTO "StagePlans" ("PlanVersionId", "StageCode", "DurationDays", "PlannedStart", "PlannedDue")
                    SELECT "Id", 'PNC', 15, {3}, {4} FROM "PlanVersions" WHERE "ProjectId" = {0} AND "VersionNo" = 2;
                    INSERT INTO "StagePlans" ("PlanVersionId", "StageCode", "DurationDays", "PlannedStart", "PlannedDue")
                    SELECT "Id", 'PRIVATE', 45, {5}, {6} FROM "PlanVersions" WHERE "ProjectId" = {0} AND "VersionNo" = 2;
                    """, id, fsStart, fsDue, pncStart, pncDue, extensionStart, extensionDue);
            }
            else
            {
                // Previous Exact edits retained generic creation-time plan policy.
                // They counted the posted dates using shared IncludeWeekends=true.
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "PlanVersions" ("ProjectId", "VersionNo", "Title", "Status", "OwnerUserId", "CreatedByUserId", "CreatedOn", "AnchorDate", "SkipWeekends", "TransitionRule", "PncApplicable")
                    VALUES ({0}, 2, 'Old Exact working revision', {1}, 'cutover-a', 'cutover-a', TIMESTAMPTZ '2024-04-04 00:00:00+00', DATE '2024-04-04', TRUE, 'NextWorkingDay', FALSE);
                    INSERT INTO "StagePlans" ("PlanVersionId", "StageCode", "DurationDays", "PlannedStart", "PlannedDue")
                    SELECT "Id", 'FS', 2, DATE '2026-12-01', DATE '2026-12-02' FROM "PlanVersions" WHERE "ProjectId" = {0} AND "VersionNo" = 2;
                    """, id, scenario == 3 ? "PendingApproval" : "Draft");
            }
            await AssertPreviousMasterSeedAsync(db, id, durationRevision: scenario < 2);
            if (scenario == 1)
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DELETE FROM "StagePlans" WHERE "PlanVersionId" IN (SELECT "Id" FROM "PlanVersions" WHERE "ProjectId" = {0} AND "VersionNo" = 2);
                    DELETE FROM "PlanVersions" WHERE "ProjectId" = {0} AND "VersionNo" = 2;
                    """, id);
                await using var discarded = db.Database.GetDbConnection().CreateCommand();
                discarded.CommandText = """
                    SELECT NOT EXISTS (SELECT 1 FROM "PlanVersions" WHERE "ProjectId" = @project AND "VersionNo" = 2)
                        AND (SELECT "AnchorStart" = DATE '2035-01-01' FROM "ProjectScheduleSettings" WHERE "ProjectId" = @project)
                        AND (SELECT "DurationDays" = 99 FROM "ProjectPlanDurations" WHERE "ProjectId" = @project AND "StageCode" = 'FS');
                    """;
                var projectParameter = discarded.CreateParameter();
                projectParameter.ParameterName = "project";
                projectParameter.Value = id;
                discarded.Parameters.Add(projectParameter);
                Assert.True(Convert.ToBoolean(await discarded.ExecuteScalarAsync()), "Old discard removes the revision and leaves its private shared inputs behind.");
            }
        }
        // All these revisions, including approved history, predate complete ownership.
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.All(await db.Projects.AsNoTracking().Where(p => projects.Contains(p.Id)).ToListAsync(), p => Assert.Equal(0, p.PublishedPlanningInputVersion));
        Assert.All(await db.PlanVersions.AsNoTracking().Where(p => projects.Contains(p.ProjectId)).ToListAsync(), p => Assert.Equal(0, p.PlanningInputVersion));

        foreach (var id in projects.Take(2))
        {
            var view = await new PlanReadService(db).GetAsync(id, "cutover-b");
            Assert.Null(view.Durations.AnchorStart);
            Assert.False(view.Durations.IncludeWeekends);
            Assert.True(view.Durations.SkipHolidays);
            Assert.Equal(NextStageStartPolicies.NextWorkingDay, view.Durations.NextStageStartPolicy);
            Assert.DoesNotContain(view.Durations.Rows, row => row.Code == "PRIVATE");
            Assert.All(view.Durations.Rows, row => Assert.True(row.DurationDays is null or 0));
            var draft = await DraftService(db, "cutover-b").CreateOrGetDraftAsync(id);
            Assert.Null(draft.AnchorDate);
            Assert.Equal(1, draft.PlanningInputVersion);
            Assert.All(draft.StagePlans, stage => Assert.Equal(0, stage.DurationDays));
            Assert.DoesNotContain(draft.StagePlans, stage => stage.StageCode == "PRIVATE");
            var pnc = draft.StagePlans.Single(stage => stage.StageCode == StageCodes.PNC);
            Assert.False(draft.PncApplicable);
            Assert.Null(pnc.PlannedStart);
            Assert.Null(pnc.PlannedDue);
            Assert.Equal(new DateOnly(2024, 1, 1), (await db.ProjectStages.AsNoTracking().SingleAsync(s => s.ProjectId == id && s.StageCode == StageCodes.PNC)).PlannedStart);
            await new PlanRealignmentService(db, new TestClock()).CreateRealignmentDraftAsync(id, StageCodes.FS, 2, "cutover-hod");
            var realignment = await db.PlanVersions.Include(p => p.StagePlans).OrderByDescending(p => p.VersionNo).FirstAsync(p => p.ProjectId == id);
            Assert.Null(realignment.AnchorDate);
            Assert.True(realignment.SkipWeekends);
            Assert.True(realignment.SkipHolidays);
            Assert.All(realignment.StagePlans, stage => Assert.Equal(0, stage.DurationDays));
            Assert.False(realignment.PncApplicable);
        }

        await ResumeLegacyDurationViaPostAsync(db, projects[0]);

        var legacyDraftProject = projects[2];
        var exactDraftService = DraftService(db, "cutover-a");
        var resumed = await exactDraftService.CreateOrGetDraftAsync(legacyDraftProject);
        Assert.Equal(PlanningInputVersions.Legacy, resumed.PlanningInputVersion);
        await exactDraftService.UpgradeLegacyDraftForExactAsync(resumed);
        Assert.Equal(1, resumed.PlanningInputVersion);
        Assert.Null(resumed.AnchorDate);
        Assert.True(resumed.SkipWeekends);
        Assert.True(resumed.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, resumed.TransitionRule);
        Assert.Equal(new DateOnly(2026, 12, 1), resumed.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedStart);
        Assert.Equal(2, resumed.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
        // This represents the subsequent actor edit, after the upgrade boundary.
        resumed.AnchorDate = new DateOnly(2026, 12, 1);
        resumed.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays = 3;
        await db.SaveChangesAsync();
        await new PlanGenerationService(db).GenerateDraftAsync(legacyDraftProject, resumed.Id);
        Assert.Equal(new DateOnly(2026, 12, 3), resumed.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedDue);
        Assert.False(resumed.PncApplicable);

        var pendingProject = projects[3];
        var pending = await db.PlanVersions.SingleAsync(p => p.ProjectId == pendingProject && p.Status == PlanVersionStatus.PendingApproval);
        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db), new NoNotifications());
        Assert.True(await approval.ApproveVersionAsync(pending.Id, "cutover-hod", false, true));
        db.ChangeTracker.Clear();
        var settings = await db.ProjectScheduleSettings.AsNoTracking().SingleAsync(s => s.ProjectId == pendingProject);
        Assert.Equal(new DateOnly(2035, 1, 1), settings.AnchorStart);
        Assert.True(settings.IncludeWeekends);
        Assert.False(settings.SkipHolidays);
        Assert.Equal(NextStageStartPolicies.SameDay, settings.NextStageStartPolicy);
        Assert.Equal(99, (await db.ProjectPlanDurations.AsNoTracking().SingleAsync(s => s.ProjectId == pendingProject && s.StageCode == StageCodes.FS)).DurationDays);
        Assert.Equal(new DateOnly(2026, 12, 1), (await db.ProjectStages.AsNoTracking().SingleAsync(s => s.ProjectId == pendingProject && s.StageCode == StageCodes.FS)).PlannedStart);
        Assert.False((await db.Projects.AsNoTracking().SingleAsync(p => p.Id == pendingProject)).PncApplicable);
        Assert.Equal(0, (await db.Projects.AsNoTracking().SingleAsync(p => p.Id == pendingProject)).PublishedPlanningInputVersion);
    }

    private static async Task ResumeLegacyDurationViaPostAsync(ApplicationDbContext db, int projectId)
    {
        db.ChangeTracker.Clear();
        var legacy = await db.PlanVersions.AsNoTracking().Include(p => p.StagePlans)
            .SingleAsync(p => p.ProjectId == projectId && p.VersionNo == 2);
        Assert.Equal("cutover-a", legacy.OwnerUserId);
        Assert.Equal(PlanningInputVersions.Legacy, legacy.PlanningInputVersion);
        Assert.Equal(new DateOnly(2035, 1, 1), legacy.AnchorDate);
        Assert.False(legacy.SkipWeekends);
        Assert.Equal(PlanTransitionRule.SameDay, legacy.TransitionRule);
        Assert.Equal(99, legacy.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(15, legacy.StagePlans.Single(s => s.StageCode == StageCodes.PNC).DurationDays);
        Assert.Equal(45, legacy.StagePlans.Single(s => s.StageCode == "PRIVATE").DurationDays);
        var extensionOrder = legacy.StagePlans.Single(s => s.StageCode == "PRIVATE").SortOrder;
        var beforePublished = await PublishedStateAsync(db, projectId);
        var beforeOthers = await OtherProposalsAsync(db, projectId);

        var owner = await new PlanReadService(db).GetAsync(projectId, "cutover-a");
        Assert.Equal(99, owner.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Equal(45, owner.Durations.Rows.Single(r => r.Code == "PRIVATE").DurationDays);
        Assert.True(owner.Durations.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays is null or 0);
        Assert.Null(owner.Exact.Rows.Single(r => r.Code == StageCodes.PNC).PlannedStart);
        Assert.Null(owner.Exact.Rows.Single(r => r.Code == StageCodes.PNC).PlannedDue);
        var untouched = await db.PlanVersions.AsNoTracking().Include(p => p.StagePlans).SingleAsync(p => p.Id == legacy.Id);
        Assert.Equal(PlanningInputVersions.Legacy, untouched.PlanningInputVersion);
        Assert.Equal(15, untouched.StagePlans.Single(s => s.StageCode == StageCodes.PNC).DurationDays);
        Assert.Equal(legacy.AnchorDate, untouched.AnchorDate);

        var other = await new PlanReadService(db).GetAsync(projectId, "cutover-b");
        Assert.Null(other.Durations.AnchorStart);
        Assert.DoesNotContain(other.Durations.Rows, row => row.Code == "PRIVATE" || row.DurationDays == 99);
        Assert.Equal(beforePublished, await PublishedStateAsync(db, projectId));
        Assert.Equal(beforeOthers, await OtherProposalsAsync(db, projectId));

        var user = new UserContext("cutover-a");
        var draftService = DraftService(db, "cutover-a");
        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(db), new NoNotifications());
        var page = new EditPlanModel(db, new NullAudit(), new PlanGenerationService(db), draftService,
            approval, NullLogger<EditPlanModel>.Instance, user)
        {
            // A first explicit Duration edit posts only FS. PRIVATE and PNC are
            // omitted: retained private data survives, while live false PNC wins.
            Input = new PlanEditInput
            {
                ProjectId = projectId, Mode = PlanEditorModes.Durations, Action = "SaveDraft",
                AnchorStart = legacy.AnchorDate, IncludeWeekends = true, SkipHolidays = true,
                NextStageStartPolicy = NextStageStartPolicies.SameDay,
                Rows = new() { new() { Code = StageCodes.FS, Name = "FS", DurationDays = 100 } }
            }
        };
        var http = new DefaultHttpContext { User = user.User };
        page.PageContext = new PageContext(new ActionContext(http, new RouteData(), new PageActionDescriptor()));
        page.TempData = new TempDataDictionary(http, new TempProvider());
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(projectId, default));
        Assert.False(page.TempData.ContainsKey("Error"));
        Assert.Equal("Draft saved.", page.TempData["Flash"]);

        db.ChangeTracker.Clear();
        var resumed = await db.PlanVersions.AsNoTracking().Include(p => p.StagePlans).SingleAsync(p => p.Id == legacy.Id);
        Assert.Equal(PlanningInputVersions.Current, resumed.PlanningInputVersion);
        Assert.Equal(legacy.AnchorDate, resumed.AnchorDate);
        Assert.False(resumed.SkipWeekends);
        Assert.Equal(PlanTransitionRule.SameDay, resumed.TransitionRule);
        // Legacy PlanVersion did not own holiday policy. The untrusted baseline
        // safely skips holidays even though the retained shared setting is false.
        Assert.True(resumed.SkipHolidays);
        Assert.False(resumed.PncApplicable);
        var fs = resumed.StagePlans.Single(s => s.StageCode == StageCodes.FS);
        var pnc = resumed.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        var extension = resumed.StagePlans.Single(s => s.StageCode == "PRIVATE");
        Assert.Equal(100, fs.DurationDays);
        Assert.Equal(45, extension.DurationDays);
        Assert.Equal(extensionOrder, extension.SortOrder);
        Assert.Equal(0, pnc.DurationDays);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        var holidays = await db.Holidays.AsNoTracking()
            .Where(h => h.Type == HolidayType.Gazetted || h.IsObservedAsOfficeHoliday)
            .Select(h => h.Date).Distinct().ToListAsync();
        var calendar = new WorkingCalendar(holidays, includeWeekends: true, skipHolidays: true);
        Assert.Equal(new DateOnly(2035, 1, 1), fs.PlannedStart);
        Assert.Equal(calendar.AddWorkingDays(fs.PlannedStart!.Value, 99), fs.PlannedDue);
        Assert.Equal(fs.PlannedDue, extension.PlannedStart);
        Assert.Equal(calendar.AddWorkingDays(extension.PlannedStart!.Value, 44), extension.PlannedDue);
        Assert.Equal(beforePublished, await PublishedStateAsync(db, projectId));
        Assert.Equal(beforeOthers, await OtherProposalsAsync(db, projectId));

        Assert.Equal(PlanDraftDeleteResult.Success, await draftService.DeleteDraftAsync(projectId));
        db.ChangeTracker.Clear();
        Assert.False(await db.PlanVersions.AnyAsync(p => p.Id == legacy.Id));
        Assert.False(await db.StagePlans.AnyAsync(s => s.PlanVersionId == legacy.Id));
        Assert.Equal(beforePublished, await PublishedStateAsync(db, projectId));
        Assert.Equal(beforeOthers, await OtherProposalsAsync(db, projectId));
        var afterDiscard = await new PlanReadService(db).GetAsync(projectId, "cutover-a");
        Assert.Null(afterDiscard.Durations.AnchorStart);
        Assert.DoesNotContain(afterDiscard.Durations.Rows, row => row.Code == "PRIVATE");
        Assert.All(afterDiscard.Durations.Rows, row => Assert.True(row.DurationDays is null or 0));
        Assert.Equal(PlanningInputVersions.Legacy,
            (await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId)).PublishedPlanningInputVersion);
    }

    private static async Task<string> PublishedStateAsync(ApplicationDbContext db, int projectId)
    {
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId);
        return JsonSerializer.Serialize(new
        {
            project.PncApplicable, project.PublishedPlanningInputVersion, project.RowVersion,
            project.ActivePlanVersionNo, project.PlanApprovedAt, project.PlanApprovedByUserId,
            Settings = await db.ProjectScheduleSettings.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(),
            Durations = await db.ProjectPlanDurations.AsNoTracking().Where(s => s.ProjectId == projectId).OrderBy(s => s.Id).ToListAsync(),
            Stages = await db.ProjectStages.AsNoTracking().Where(s => s.ProjectId == projectId).OrderBy(s => s.Id).ToListAsync(),
            Snapshots = await db.ProjectPlanSnapshots.AsNoTracking().Where(s => s.ProjectId == projectId).OrderBy(s => s.Id).ToListAsync()
        });
    }

    private static async Task<string> OtherProposalsAsync(ApplicationDbContext db, int projectId) =>
        JsonSerializer.Serialize(await db.PlanVersions.AsNoTracking().Include(p => p.StagePlans)
            .Where(p => p.ProjectId == projectId && p.OwnerUserId != "cutover-a")
            .OrderBy(p => p.Id).Select(p => new
            {
                p.Id, p.VersionNo, p.OwnerUserId, p.Status, p.PlanningInputVersion,
                p.AnchorDate, p.SkipWeekends, p.SkipHolidays, p.TransitionRule, p.PncApplicable,
                Stages = p.StagePlans.OrderBy(s => s.Id).Select(s => new
                {
                    s.Id, s.StageCode, s.DurationDays, s.PlannedStart, s.PlannedDue, s.SortOrder
                }).ToList()
            }).ToListAsync());
    private static async Task AssertPreviousMasterSeedAsync(ApplicationDbContext db, int projectId, bool durationRevision)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT plan."AnchorDate", plan."SkipWeekends", plan."TransitionRule", plan."PncApplicable", plan."CreatedOn",
                   project."PncApplicable", settings."AnchorStart", settings."IncludeWeekends", settings."SkipHolidays", settings."NextStageStartPolicy",
                   official."PlannedStart", official."PlannedDue"
            FROM "PlanVersions" AS plan
            JOIN "Projects" AS project ON project."Id" = plan."ProjectId"
            JOIN "ProjectScheduleSettings" AS settings ON settings."ProjectId" = project."Id"
            JOIN "ProjectStages" AS official ON official."ProjectId" = project."Id" AND official."StageCode" = 'FS'
            WHERE plan."ProjectId" = @project AND plan."VersionNo" = 2;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "project";
        parameter.Value = projectId;
        command.Parameters.Add(parameter);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(durationRevision ? new DateOnly(2035, 1, 1) : new DateOnly(2024, 4, 4), reader.GetFieldValue<DateOnly>(0));
            Assert.Equal(!durationRevision, reader.GetBoolean(1));
            Assert.Equal(durationRevision ? "SameDay" : "NextWorkingDay", reader.GetString(2));
            Assert.Equal(durationRevision, reader.GetBoolean(3));
            Assert.Equal(durationRevision ? new DateOnly(2026, 10, 16) : new DateOnly(2024, 4, 4), DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(4).UtcDateTime));
            Assert.False(reader.GetBoolean(5));
            Assert.Equal(new DateOnly(2035, 1, 1), reader.GetFieldValue<DateOnly>(6));
            Assert.True(reader.GetBoolean(7));
            Assert.False(reader.GetBoolean(8));
            Assert.Equal("SameDay", reader.GetString(9));
            Assert.Equal(new DateOnly(2026, 11, 2), reader.GetFieldValue<DateOnly>(10));
            Assert.Equal(new DateOnly(2026, 11, 6), reader.GetFieldValue<DateOnly>(11));
            Assert.False(await reader.ReadAsync());
        }
        command.CommandText = """
            SELECT stage."StageCode", stage."DurationDays", stage."PlannedStart", stage."PlannedDue"
            FROM "StagePlans" AS stage JOIN "PlanVersions" AS plan ON plan."Id" = stage."PlanVersionId"
            WHERE plan."ProjectId" = @project AND plan."VersionNo" = 2;
            """;
        var expected = durationRevision
            ? new Dictionary<string, (int Duration, DateOnly Start, DateOnly Due)>
            {
                ["FS"] = (99, new DateOnly(2035, 1, 1), new DateOnly(2035, 1, 1).AddDays(98)),
                ["PNC"] = (15, new DateOnly(2035, 1, 1).AddDays(98), new DateOnly(2035, 1, 1).AddDays(112)),
                ["PRIVATE"] = (45, new DateOnly(2035, 1, 1).AddDays(112), new DateOnly(2035, 1, 1).AddDays(156))
            }
            : new Dictionary<string, (int Duration, DateOnly Start, DateOnly Due)> { ["FS"] = (2, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 2)) };
        await using var stages = await command.ExecuteReaderAsync();
        while (await stages.ReadAsync())
        {
            Assert.True(expected.Remove(stages.GetString(0), out var row));
            Assert.Equal(row.Duration, stages.GetInt32(1));
            Assert.Equal(row.Start, stages.GetFieldValue<DateOnly>(2));
            Assert.Equal(row.Due, stages.GetFieldValue<DateOnly>(3));
        }
        Assert.Empty(expected);
    }

    private static PlanDraftService DraftService(ApplicationDbContext db, string actor) => new(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new NullAudit(), new UserContext(actor));
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero); }
    private sealed class UserContext(string actor) : IUserContext
    {
        public string? UserId => actor;
        public ClaimsPrincipal User => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, actor), new Claim(ClaimTypes.Role, "Project Officer") }, "Test"));
    }
    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null) => Task.CompletedTask;
    }
    private sealed class NoNotifications : IPlanNotificationService
    {
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
