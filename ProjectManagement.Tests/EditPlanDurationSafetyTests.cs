using Microsoft.Data.Sqlite;
using ProjectManagement.Infrastructure;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Timeline;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public class EditPlanDurationSafetyTests
{
    [Theory]
    [InlineData("HoD", true, PlanEditorModes.Durations)]
    [InlineData("HoD", false, PlanEditorModes.Durations)]
    [InlineData("Admin", true, PlanEditorModes.Durations)]
    [InlineData("Admin", false, PlanEditorModes.Durations)]
    [InlineData("HoD", true, PlanEditorModes.Exact)]
    [InlineData("HoD", false, PlanEditorModes.Exact)]
    [InlineData("Admin", true, PlanEditorModes.Exact)]
    [InlineData("Admin", false, PlanEditorModes.Exact)]
    public async Task DirectPublication_CommitsCurrentApplicabilityAndRefreshesScopedPolicy(string role, bool initiallyApplicable, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true);
        await SeedPublishedExecutionAsync(f, initiallyApplicable);
        f.Page.Input.Mode = mode;
        var pnc = f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC);
        pnc.DurationDays = initiallyApplicable ? null : 2;
        pnc.PlannedStart = initiallyApplicable ? null : new DateOnly(2026, 10, 22);
        pnc.PlannedDue = initiallyApplicable ? null : new DateOnly(2026, 10, 23);
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.Equal(initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        Assert.Equal(!initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        await using var fresh = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(f.Db.Database.GetDbConnection()).Options);
        var freshPolicy = new ProjectStageWorkflowPolicy(fresh, new WorkflowStageMetadataProvider());
        Assert.Equal(!initiallyApplicable, (await freshPolicy.GetAsync(1)).PncApplicable);
        var rules = new StageRulesService(fresh, freshPolicy);
        Assert.Equal(initiallyApplicable, rules.CanStart(await rules.GetContextAsync(1, default), StageCodes.EAS).Allowed);
        Assert.Equal(initiallyApplicable, (await fresh.PlanVersions.SingleAsync(p => p.VersionNo == 40)).PncApplicable);
        Assert.Equal(40, (await fresh.Projects.SingleAsync()).ActivePlanVersionNo);
        var stale = await fresh.ProjectStages.SingleAsync(p => p.StageCode == StageCodes.PNC);
        Assert.Equal(StageStatus.NotStarted, stale.Status);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task ExactPublication_UnchangedDatesStillPublishExplicitApplicability(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true);
        await SeedPublishedExecutionAsync(f, false);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        foreach (var row in f.Page.Input.Rows)
        {
            var stage = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == row.Code);
            row.PlannedStart = stage.PlannedStart;
            row.PlannedDue = stage.PlannedDue;
        }
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        Assert.True((await f.Db.Projects.SingleAsync()).PncApplicable);
        var pnc = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(new DateOnly(2024, 1, 3), pnc.PlannedStart);
        Assert.Equal(StageStatus.NotStarted, pnc.Status);
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations)]
    [InlineData("Admin", PlanEditorModes.Durations)]
    [InlineData("HoD", PlanEditorModes.Exact)]
    [InlineData("Admin", PlanEditorModes.Exact)]
    public async Task Publication_OmittedPncNeverInfersApplicabilityFromWorkingOrStageRows(string role, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true);
        await SeedPublishedExecutionAsync(f, false);
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 2, SortOrder = 80 });
        await f.Db.SaveChangesAsync();
        f.Page.Input.Rows.RemoveAll(r => r.Code == StageCodes.PNC);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.False((await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.False((await policy.GetAsync(1)).PncApplicable);
    }

    [Theory]
    [InlineData(PlanEditorModes.Durations, true)]
    [InlineData(PlanEditorModes.Durations, false)]
    [InlineData(PlanEditorModes.Exact, true)]
    [InlineData(PlanEditorModes.Exact, false)]
    public async Task Publication_DuplicatePncRowsUseTheSameFinalSubmittedDecisionAsPlannerWrites(string mode, bool finalApplicable)
    {
        await using var f = await Fixture.CreateAsync("HoD", "SaveChanges", relational: true);
        await SeedPublishedExecutionAsync(f, !finalApplicable);
        f.Page.Input.Mode = mode;
        var first = f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC);
        first.DurationDays = finalApplicable ? null : 2;
        first.PlannedStart = finalApplicable ? null : new DateOnly(2026, 10, 22);
        first.PlannedDue = finalApplicable ? null : new DateOnly(2026, 10, 23);
        f.Page.Input.Rows.Add(new PlanEditInputRow
        {
            Code = "pnc", Name = "PNC repeated", DurationDays = finalApplicable ? 2 : null,
            PlannedStart = finalApplicable ? new DateOnly(2026, 10, 22) : null,
            PlannedDue = finalApplicable ? new DateOnly(2026, 10, 23) : null
        });
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        Assert.Equal(finalApplicable, (await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        var pnc = await f.Db.ProjectStages.AsNoTracking().SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(finalApplicable, pnc.PlannedStart.HasValue && pnc.PlannedDue.HasValue);
    }

    public static IEnumerable<object[]> NonAuthoritativePublicationCases()
    {
        foreach (var initial in new[] { true, false })
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        foreach (var action in role == "Project Officer" ? new[] { "Autosave", "SaveDraft", "Calculate", "Submit" } : new[] { "Autosave", "SaveDraft", "Calculate" })
            yield return new object[] { role, action, initial };
    }

    [Theory]
    [MemberData(nameof(NonAuthoritativePublicationCases))]
    public async Task WorkingActions_NeverPublishExecutionApplicability(string role, string action, bool initiallyApplicable)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedPublishedExecutionAsync(f, initiallyApplicable);
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = initiallyApplicable ? null : 2;
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.Equal(initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        var result = await f.Page.OnPostAsync(1, default);
        if (action == "Autosave") AssertJson(result, 200, true);
        else Assert.IsType<RedirectToPageResult>(result);
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        Assert.Equal(initiallyApplicable, (await policy.GetAsync(1)).PncApplicable);
        Assert.Equal(initiallyApplicable, (await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        var working = await f.Db.PlanVersions.AsNoTracking().SingleAsync(p => p.VersionNo != 40);
        Assert.Equal(!initiallyApplicable, working.PncApplicable);
        Assert.Equal(action == "Submit" ? PlanVersionStatus.PendingApproval : PlanVersionStatus.Draft, working.Status);
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations)]
    [InlineData("Admin", PlanEditorModes.Durations)]
    [InlineData("HoD", PlanEditorModes.Exact)]
    [InlineData("Admin", PlanEditorModes.Exact)]
    public async Task DirectPublication_FailureRollsBackApplicabilityAndAllPlannerWrites(string role, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges", relational: true, failAudit: true);
        await SeedPublishedExecutionAsync(f, true);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = null;
        var before = await PlannerStateAsync(f.Db);
        var policy = new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider());
        Assert.True((await policy.GetAsync(1)).PncApplicable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Page.OnPostAsync(1, default));
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.True((await policy.GetAsync(1)).PncApplicable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approval_OuterRollbackKeepsPublishedApplicabilityAndPlanState(bool initiallyApplicable)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Submit", relational: true);
        await SeedPublishedExecutionAsync(f, initiallyApplicable);
        var pending = new PlanVersion { ProjectId = 1, VersionNo = 41, CreatedByUserId = "po-user", SubmittedByUserId = "po-user", Status = PlanVersionStatus.PendingApproval, PncApplicable = !initiallyApplicable };
        pending.StagePlans.Add(new StagePlan { StageCode = StageCodes.PNC, PlannedStart = new DateOnly(2026, 10, 22), PlannedDue = new DateOnly(2026, 10, 23) });
        f.Db.PlanVersions.Add(pending);
        await f.Db.SaveChangesAsync();
        var before = await PlannerStateAsync(f.Db);
        var approval = new PlanApprovalService(f.Db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(f.Db), new NullNotifications());
        await using (var outer = await RelationalTransactionScope.CreateAsync(f.Db.Database))
        {
            Assert.True(await approval.ApproveVersionAsync(pending.Id, "hod-reviewer", false, true));
            Assert.Equal(!initiallyApplicable, (await f.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
            await outer.RollbackAsync();
        }
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        Assert.Equal(initiallyApplicable, (await new ProjectStageWorkflowPolicy(f.Db, new WorkflowStageMetadataProvider()).GetAsync(1)).PncApplicable);
    }

    private static async Task SeedPublishedExecutionAsync(Fixture f, bool applicable)
    {
        var project = await f.Db.Projects.SingleAsync();
        project.PncApplicable = applicable;
        project.ActivePlanVersionNo = 40;
        f.Db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 40, CreatedByUserId = "earlier-po", Status = PlanVersionStatus.Approved, PncApplicable = applicable });
        f.Db.StageTemplates.Add(new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.PNC, Name = "PNC", Sequence = 80, Optional = true });
        f.Db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.COB, Status = StageStatus.Completed, ActualStart = new DateOnly(2024, 1, 1), CompletedOn = new DateOnly(2024, 1, 2) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.NotStarted, PlannedStart = new DateOnly(2024, 1, 3), PlannedDue = new DateOnly(2024, 1, 4) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.EAS, Status = StageStatus.NotStarted });
        f.Page.Input.Rows.Add(new PlanEditInputRow { Code = StageCodes.PNC, Name = "PNC", DurationDays = applicable ? 2 : null });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    public static IEnumerable<object?[]> AllowedPlannerActions()
    {
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        {
            foreach (var action in new[] { "Autosave", "SaveDraft", "Calculate", role == "Project Officer" ? "Submit" : "SaveChanges" })
                yield return new object?[] { role, PlanEditorModes.Durations, action };
            foreach (var action in role == "Project Officer" ? new[] { "SaveDraft", "Submit" } : new[] { "SaveChanges" })
                yield return new object?[] { role, PlanEditorModes.Exact, action };
        }
        yield return new object?[] { "Project Officer", PlanEditorModes.Exact, " savedraft " };
        yield return new object?[] { "HoD", PlanEditorModes.Durations, " savechanges " };
        yield return new object?[] { "Admin", PlanEditorModes.Durations, "aUtOsAvE" };
    }

    [Theory]
    [MemberData(nameof(AllowedPlannerActions))]
    public async Task AllowedActionMatrix_PreservesPublicationIntent(string role, string mode, string action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var result = await f.Page.OnPostAsync(1, default);
        var normalizedAction = action.Trim().ToUpperInvariant();
        if (normalizedAction == "AUTOSAVE") AssertJson(result, 200, true);
        else Assert.IsType<RedirectToPageResult>(result);
        Assert.False(f.Page.TempData.ContainsKey("Error"));
        f.Db.ChangeTracker.Clear();
        var live = await f.Db.ProjectStages.SingleAsync();
        var publishes = normalizedAction == "SAVECHANGES";
        Assert.Equal(publishes ? new DateOnly(2026, 10, 19) : new DateOnly(2024, 1, 1), live.PlannedStart);
        Assert.Equal(publishes ? new DateOnly(2026, 10, 21) : new DateOnly(2024, 1, 3), live.PlannedDue);
        Assert.Equal(StageStatus.NotStarted, live.Status);
        var plan = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(normalizedAction == "SUBMIT" ? PlanVersionStatus.PendingApproval : PlanVersionStatus.Draft, plan.Status);
        Assert.Equal(new DateOnly(2026, 10, 19), Assert.Single(plan.StagePlans).PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 21), Assert.Single(plan.StagePlans).PlannedDue);
        if (normalizedAction == "SUBMIT")
        {
            Assert.Equal(f.User.UserId, plan.SubmittedByUserId);
            Assert.NotNull(plan.SubmittedOn);
        }
        else
        {
            Assert.Null(plan.SubmittedByUserId);
            Assert.Null(plan.SubmittedOn);
        }
    }

    public static IEnumerable<object?[]> RejectedPlannerActions()
    {
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        foreach (var mode in new[] { PlanEditorModes.Durations, PlanEditorModes.Exact })
        {
            var allowed = mode == PlanEditorModes.Durations
                ? new[] { "Autosave", "SaveDraft", "Calculate", role == "Project Officer" ? "Submit" : "SaveChanges" }
                : role == "Project Officer" ? new[] { "SaveDraft", "Submit" } : new[] { "SaveChanges" };
            foreach (var action in new string?[] { "Autosave", "SaveDraft", "Calculate", "Submit", "SaveChanges", "Unknown", null, "", " \t ", "1", "Apply" })
                if (!allowed.Contains(action)) yield return new object?[] { role, mode, action };
        }
        foreach (var role in new[] { "Project Officer", "HoD", "Admin" })
        foreach (var mode in new string?[] { "Unknown", "", null })
            yield return new object?[] { role, mode, role == "Project Officer" ? "SaveDraft" : "SaveChanges" };
    }

    [Theory]
    [MemberData(nameof(RejectedPlannerActions))]
    public async Task RejectedActionMatrix_PreservesAllSeededPlannerState(string role, string? mode, string? action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Input.Mode = mode!;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var before = await PlannerStateAsync(f.Db);
        var result = await f.Page.OnPostAsync(1, default);
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        AssertRejectedPlannerRequest(f.Page, result, action);
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations, "Submit")]
    [InlineData("Admin", PlanEditorModes.Durations, "Submit")]
    [InlineData("HoD", PlanEditorModes.Exact, "SaveDraft")]
    [InlineData("Admin", PlanEditorModes.Exact, "Submit")]
    [InlineData("Project Officer", PlanEditorModes.Exact, "Unknown")]
    [InlineData("Project Officer", PlanEditorModes.Durations, null)]
    public async Task RejectedAction_CannotCreateDraftBeforeValidation(string role, string mode, string? action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        await SeedRecognizablePlannerStateAsync(f, ownDraft: false);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var before = await PlannerStateAsync(f.Db);
        var result = await f.Page.OnPostAsync(1, default);
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        AssertRejectedPlannerRequest(f.Page, result, action);
    }

    [Theory]
    [InlineData("HoD", PlanEditorModes.Durations)]
    [InlineData("Admin", PlanEditorModes.Durations)]
    [InlineData("HoD", PlanEditorModes.Exact)]
    [InlineData("Admin", PlanEditorModes.Exact)]
    public async Task DirectRoleAlsoAssignedAsPo_CannotSubmit(string role, string mode)
    {
        await using var f = await Fixture.CreateAsync(role, "Submit");
        ((ClaimsIdentity)f.User.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Project Officer"));
        (await f.Db.Projects.SingleAsync()).LeadPoUserId = f.User.UserId;
        await f.Db.SaveChangesAsync();
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Input.Mode = mode;
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 10, 19);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 10, 21);
        var before = await PlannerStateAsync(f.Db);
        var result = await f.Page.OnPostAsync(1, default);
        Assert.Equal(before, await PlannerStateAsync(f.Db));
        AssertRejectedPlannerRequest(f.Page, result, "Submit");
    }

    private static void AssertRejectedPlannerRequest(EditPlanModel page, IActionResult result, string? action)
    {
        if (string.Equals(action?.Trim(), "Autosave", StringComparison.OrdinalIgnoreCase))
        {
            var json = AssertJson(result, 400, false);
            Assert.NotEmpty(json.GetProperty("errors").EnumerateArray());
        }
        else
        {
            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Projects/Overview", redirect.PageName);
            Assert.Equal(1, redirect.RouteValues!["id"]);
            Assert.False(string.IsNullOrWhiteSpace(page.TempData["Error"] as string));
            Assert.Equal("plan-edit", page.TempData["OpenOffcanvas"]);
        }
    }

    private static async Task SeedRecognizablePlannerStateAsync(Fixture f, bool ownDraft = true)
    {
        f.Db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1, AnchorStart = new DateOnly(2026, 10, 5), IncludeWeekends = true,
            SkipHolidays = true, NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        f.Db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 9, SortOrder = 17 });
        var owner = ownDraft ? f.User.UserId! : "previous-owner";
        var plan = new PlanVersion
        {
            ProjectId = 1, VersionNo = 7, OwnerUserId = owner, CreatedByUserId = owner,
            CreatedOn = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            Status = ownDraft ? PlanVersionStatus.Draft : PlanVersionStatus.Approved,
            AnchorStageCode = StageCodes.FS, AnchorDate = new DateOnly(2026, 10, 5),
            PncApplicable = false, SkipWeekends = false, TransitionRule = PlanTransitionRule.SameDay
        };
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, PlannedStart = new DateOnly(2026, 10, 6), PlannedDue = new DateOnly(2026, 10, 8), DurationDays = 3 });
        f.Db.PlanVersions.Add(plan);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<string> PlannerStateAsync(ApplicationDbContext db)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(new
        {
            Published = await db.Projects.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.PncApplicable, p.ActivePlanVersionNo }).ToListAsync(),
            Settings = await db.ProjectScheduleSettings.AsNoTracking().OrderBy(s => s.ProjectId)
                .Select(s => new { s.ProjectId, s.AnchorStart, s.IncludeWeekends, s.SkipHolidays, s.NextStageStartPolicy }).ToListAsync(),
            Durations = await db.ProjectPlanDurations.AsNoTracking().OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.ProjectId, d.StageCode, d.DurationDays, d.SortOrder }).ToListAsync(),
            StagePlans = await db.StagePlans.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.PlanVersionId, s.StageCode, s.PlannedStart, s.PlannedDue, s.DurationDays }).ToListAsync(),
            Stages = await db.ProjectStages.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.ProjectId, s.StageCode, s.PlannedStart, s.PlannedDue, s.Status, s.ActualStart, s.CompletedOn, s.SortOrder }).ToListAsync(),
            Plans = await db.PlanVersions.AsNoTracking().OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.ProjectId, p.VersionNo, p.Title, p.OwnerUserId, p.CreatedByUserId, p.CreatedOn, p.Status,
                    p.SubmittedByUserId, p.SubmittedOn, p.ApprovedByUserId, p.ApprovedOn, p.RejectedByUserId, p.RejectedOn, p.RejectionNote,
                    p.AnchorStageCode, p.AnchorDate, p.SkipWeekends, p.TransitionRule, p.PncApplicable }).ToListAsync(),
            ApprovalLogs = await db.PlanApprovalLogs.AsNoTracking().OrderBy(l => l.Id).Select(l => l.Id).ToListAsync()
        });
    }

    [Theory]
    [InlineData("HoD", null)]
    [InlineData("HoD", 0)]
    [InlineData("Admin", null)]
    [InlineData("Admin", 0)]
    public async Task LeadingClearedStage_DurationApplyAndExactRoundTripKeepLiveAndDraftAligned(string role, int? leadingDuration)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        var fs = await f.Db.ProjectStages.SingleAsync();
        fs.Status = StageStatus.Skipped;
        f.Page.Input.Rows[0].DurationDays = leadingDuration;
        f.Page.Input.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        f.Db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.IPA, Name = "IPA", Sequence = 20 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.SOW, Name = "SOW", Sequence = 30 });
        f.Page.Input.Rows.AddRange(new[]
        {
            new PlanEditInputRow { Code = StageCodes.IPA, Name = "IPA", DurationDays = 2 },
            new PlanEditInputRow { Code = StageCodes.SOW, Name = "SOW", DurationDays = 2 }
        });
        await f.Db.SaveChangesAsync();
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var draftIpa = await f.Db.StagePlans.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(new DateOnly(2026, 10, 19), draftIpa.PlannedStart);
        // Calculate is still non-authoritative, including for the skipped FS.
        Assert.Equal(new DateOnly(2024, 1, 1), (await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS)).PlannedStart);
        f.Page.Input.Action = "SaveChanges";
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var live = await f.Db.ProjectStages.ToDictionaryAsync(s => s.StageCode);
        Assert.Equal(new DateOnly(2026, 10, 19), live[StageCodes.IPA].PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 20), live[StageCodes.IPA].PlannedDue);
        Assert.Equal(new DateOnly(2026, 10, 21), live[StageCodes.SOW].PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 22), live[StageCodes.SOW].PlannedDue);
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        foreach (var code in new[] { StageCodes.IPA, StageCodes.SOW })
        {
            var preview = view.Durations.Rows.Single(r => r.Code == code);
            var exact = view.Exact.Rows.Single(r => r.Code == code);
            Assert.Equal(live[code].PlannedStart, preview.PreviewStart);
            Assert.Equal(live[code].PlannedDue, preview.PreviewDue);
            Assert.Equal(live[code].PlannedStart, exact.PlannedStart);
            Assert.Equal(live[code].PlannedDue, exact.PlannedDue);
        }
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = view.Exact.Rows.Select(r => new PlanEditInputRow
        {
            Code = r.Code, Name = r.Name, PlannedStart = r.PlannedStart, PlannedDue = r.PlannedDue
        }).ToList();
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var afterExact = await f.Db.ProjectStages.ToDictionaryAsync(s => s.StageCode);
        foreach (var code in new[] { StageCodes.IPA, StageCodes.SOW })
        {
            Assert.Equal(live[code].PlannedStart, afterExact[code].PlannedStart);
            Assert.Equal(live[code].PlannedDue, afterExact[code].PlannedDue);
        }
        Assert.Null(afterExact[StageCodes.FS].PlannedStart);
        Assert.Null(afterExact[StageCodes.FS].PlannedDue);
        Assert.Equal(StageStatus.Skipped, afterExact[StageCodes.FS].Status);
        Assert.Null(afterExact[StageCodes.FS].ActualStart);
        Assert.Null(afterExact[StageCodes.FS].CompletedOn);
    }

    [Theory]
    [InlineData("HoD", null)]
    [InlineData("HoD", 0)]
    [InlineData("HoD", -1)]
    [InlineData("Admin", null)]
    [InlineData("Admin", 0)]
    [InlineData("Admin", -1)]
    public async Task DurationPnc_PositiveThenCleared_DoesNotResurrectOnExactSave(string role, int? removedDuration)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        await AddOptionalPncAsync(f);
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.True(draft.PncApplicable);
        Assert.NotNull(draft.StagePlans.Single(s => s.StageCode == StageCodes.PNC).PlannedDue);
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = removedDuration;
        f.Page.Input.Action = "SaveChanges";
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();

        draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.False(draft.PncApplicable);
        var pncPlan = draft.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pncPlan.PlannedStart);
        Assert.Null(pncPlan.PlannedDue);
        Assert.Equal(0, pncPlan.DurationDays);
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        var pncPreview = view.Durations.Rows.Single(r => r.Code == StageCodes.PNC);
        Assert.Null(pncPreview.PreviewStart);
        Assert.Null(pncPreview.PreviewDue);
        var pncExact = view.Exact.Rows.Single(r => r.Code == StageCodes.PNC);
        Assert.Null(pncExact.PlannedStart);
        Assert.Null(pncExact.PlannedDue);

        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Rows = view.Exact.Rows.Select(r => new PlanEditInputRow
        {
            Code = r.Code, Name = r.Name, PlannedStart = r.PlannedStart, PlannedDue = r.PlannedDue
        }).ToList();
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var pnc = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.Null((await f.Db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.PNC)).DurationDays);
        Assert.Equal(StageStatus.Completed, pnc.Status);
        Assert.Equal(new DateOnly(2025, 1, 1), pnc.ActualStart);
        Assert.Equal(new DateOnly(2025, 1, 2), pnc.CompletedOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task PoDurationPnc_PositiveThenCleared_SubmitAndApproveDoesNotPublishOldDates(int? removedDuration)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Calculate");
        await AddOptionalPncAsync(f);
        await f.Page.OnPostAsync(1, default);
        var draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.NotNull(draft.StagePlans.Single(s => s.StageCode == StageCodes.PNC).PlannedDue);
        f.Page.Input.Rows.Single(r => r.Code == StageCodes.PNC).DurationDays = removedDuration;
        f.Page.Input.Action = "Submit";
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        f.Db.ChangeTracker.Clear();
        var pending = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(PlanVersionStatus.PendingApproval, pending.Status);
        Assert.False(pending.PncApplicable);
        var pncPlan = pending.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pncPlan.PlannedStart);
        Assert.Null(pncPlan.PlannedDue);
        Assert.Equal(0, pncPlan.DurationDays);
        // PO submission cannot erase the already-published plan before approval.
        Assert.Equal(new DateOnly(2024, 1, 2), (await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC)).PlannedDue);
        var approval = new PlanApprovalService(f.Db, new TestClock(), NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(f.Db), new NullNotifications());
        Assert.True(await approval.ApproveVersionAsync(pending.Id, "hod-reviewer", false, true));
        f.Db.ChangeTracker.Clear();
        var pnc = await f.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.Equal(StageStatus.Completed, pnc.Status);
        Assert.Equal(new DateOnly(2025, 1, 2), pnc.CompletedOn);
    }

    private static async Task AddOptionalPncAsync(Fixture f)
    {
        f.Db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.PNC, Name = "PNC", Sequence = 80, Optional = true
        });
        f.Db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.Completed,
            PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2),
            ActualStart = new DateOnly(2025, 1, 1), CompletedOn = new DateOnly(2025, 1, 2)
        });
        f.Page.Input.Rows.Add(new PlanEditInputRow { Code = StageCodes.PNC, Name = "PNC", DurationDays = 2 });
        await f.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Project Officer", "Autosave")]
    [InlineData("HoD", "Autosave")]
    [InlineData("Admin", "Autosave")]
    [InlineData("Project Officer", "Calculate")]
    [InlineData("HoD", "Calculate")]
    [InlineData("Admin", "Calculate")]
    [InlineData("Project Officer", "SaveDraft")]
    [InlineData("HoD", "SaveDraft")]
    [InlineData("Admin", "SaveDraft")]
    public async Task WorkingActions_NeverPublishLiveDates(string role, string action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        var result = await f.Page.OnPostAsync(1, default);
        if (action == "Autosave") AssertJson(result, 200, true);
        else Assert.IsType<RedirectToPageResult>(result);
        await f.AssertLiveDatesUnchangedAsync();
        Assert.Equal(3, (await f.Db.ProjectPlanDurations.SingleAsync()).DurationDays);
        var draft = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(PlanVersionStatus.Draft, draft.Status);
        Assert.Equal(new DateOnly(2026, 10, 19), Assert.Single(draft.StagePlans).PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 21), Assert.Single(draft.StagePlans).PlannedDue);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task ExplicitSaveChanges_PreservesDirectAuthority(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveChanges");
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var live = await f.Db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 19), live.PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 21), live.PlannedDue);
        Assert.Empty(await f.Db.PlanVersions.ToListAsync());
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task CalculateThenExplicitApply_RefreshesPreviewWithoutHidingNewOfficialDates(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        await f.Page.OnPostAsync(1, default);
        await f.AssertLiveDatesUnchangedAsync();
        f.Page.Input.Action = "SaveChanges";
        f.Page.Input.Rows[0].DurationDays = 4;
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        var live = await f.Db.ProjectStages.SingleAsync();
        var preview = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        Assert.Equal(new DateOnly(2026, 10, 22), live.PlannedDue);
        Assert.Equal(new DateOnly(2026, 10, 22), preview.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedDue);
        Assert.Equal(new DateOnly(2026, 10, 22), preview.Durations.Rows.Single(r => r.Code == StageCodes.FS).PreviewDue);
    }

    [Theory]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task DurationPreviewThenExactSave_DoesNotReloadOrRepublishStaleDraftDates(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Calculate");
        await f.Page.OnPostAsync(1, default);
        f.Page.Input.Mode = PlanEditorModes.Exact;
        f.Page.Input.Action = "SaveChanges";
        f.Page.Input.Rows[0].PlannedStart = new DateOnly(2026, 11, 2);
        f.Page.Input.Rows[0].PlannedDue = new DateOnly(2026, 11, 4);
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        var view = await new PlanReadService(f.Db).GetAsync(1, f.User.UserId);
        var row = view.Exact.Rows.Single(r => r.Code == StageCodes.FS);
        Assert.Equal(new DateOnly(2026, 11, 2), row.PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 4), row.PlannedDue);
        f.Page.Input.Rows[0].PlannedStart = row.PlannedStart;
        f.Page.Input.Rows[0].PlannedDue = row.PlannedDue;
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(new DateOnly(2026, 11, 4), (await f.Db.ProjectStages.SingleAsync()).PlannedDue);
    }

    [Theory]
    [InlineData("HoD", "Autosave")]
    [InlineData("Admin", "Autosave")]
    [InlineData("HoD", "Calculate")]
    [InlineData("Admin", "Calculate")]
    public async Task WorkingActions_DoNotMaterialiseOfficialStages(string role, string action)
    {
        await using var f = await Fixture.CreateAsync(role, action);
        f.Db.ProjectStages.RemoveRange(f.Db.ProjectStages);
        await f.Db.SaveChangesAsync();
        await f.Page.OnPostAsync(1, default);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.ProjectStages.ToListAsync());
        Assert.NotEmpty(await f.Db.StagePlans.ToListAsync());
    }

    [Fact]
    public async Task PoSubmit_SubmitsGeneratedDraftWithoutPublishing()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Submit");
        Assert.IsType<RedirectToPageResult>(await f.Page.OnPostAsync(1, default));
        var plan = await f.Db.PlanVersions.Include(p => p.StagePlans).SingleAsync();
        Assert.Equal(PlanVersionStatus.PendingApproval, plan.Status);
        Assert.Equal("po-user", plan.SubmittedByUserId);
        Assert.Equal(new DateOnly(2026, 10, 21), Assert.Single(plan.StagePlans).PlannedDue);
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Theory]
    [InlineData("Project Officer")]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task InvalidAutosave_ReturnsJsonFailureAndDoesNotPersist(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Autosave");
        f.Page.Input.Rows[0].DurationDays = 0;
        var json = AssertJson(await f.Page.OnPostAsync(1, default), 400, false);
        Assert.NotEmpty(json.GetProperty("errors").EnumerateArray());
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await f.Db.ProjectScheduleSettings.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task ModelBindingErrors_AreJsonAutosaveFailures()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Autosave");
        f.Page.ModelState.AddModelError("Input.Rows[0].DurationDays", "The value is invalid.");
        AssertJson(await f.Page.OnPostAsync(1, default), 400, false);
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Unassigned PO")]
    public async Task UnauthorisedAutosave_CannotMutate(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "Autosave");
        AssertJson(await f.Page.OnPostAsync(1, default), 403, false);
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await f.Db.PlanVersions.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task PoCannotForgeDirectSaveIntent()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "SaveChanges");
        AssertRejectedPlannerRequest(f.Page, await f.Page.OnPostAsync(1, default), "SaveChanges");
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Theory]
    [InlineData("Project Officer")]
    [InlineData("HoD")]
    [InlineData("Admin")]
    public async Task LegacyAjaxSaveDraft_DoesNotDirectApplyAndReturnsJson(string role)
    {
        await using var f = await Fixture.CreateAsync(role, "SaveDraft");
        await SeedRecognizablePlannerStateAsync(f);
        f.Page.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        AssertJson(await f.Page.OnPostAsync(1, default), 200, true);
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task PendingDraftLock_ReturnsConflictBeforeWorkingInputsChange()
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Autosave");
        f.Db.PlanVersions.Add(new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, Title = PlanVersion.ProjectTimelineTitle,
            OwnerUserId = "po-user", CreatedByUserId = "po-user",
            Status = PlanVersionStatus.PendingApproval
        });
        await f.Db.SaveChangesAsync();
        AssertJson(await f.Page.OnPostAsync(1, default), 409, false);
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await f.Db.ProjectScheduleSettings.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Theory]
    [InlineData("mismatched-project", 400)]
    [InlineData("missing-project", 404)]
    [InlineData("missing-user", 403)]
    [InlineData("wrong-mode", 400)]
    [InlineData("invalid-policy", 400)]
    public async Task RejectedAutosave_HasMachineReadableFailure(string scenario, int status)
    {
        await using var f = await Fixture.CreateAsync("Project Officer", "Autosave");
        var id = 1;
        if (scenario == "mismatched-project") f.Page.Input.ProjectId = 2;
        if (scenario == "missing-project") id = f.Page.Input.ProjectId = 2;
        if (scenario == "missing-user") f.User.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (scenario == "wrong-mode") f.Page.Input.Mode = PlanEditorModes.Exact;
        if (scenario == "invalid-policy") f.Page.Input.NextStageStartPolicy = "invalid";
        AssertJson(await f.Page.OnPostAsync(id, default), status, false);
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    private static JsonElement AssertJson(IActionResult result, int status, bool ok)
    {
        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(status, json.StatusCode ?? 200);
        var data = JsonSerializer.SerializeToElement(json.Value);
        Assert.Equal(ok, data.GetProperty("ok").GetBoolean());
        if (ok) Assert.True(DateTimeOffset.TryParse(data.GetProperty("savedAt").GetString(), out _));
        else Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("message").GetString()));
        return data;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }
        public EditPlanModel Page { get; }
        public UserContext User { get; }

        private readonly SqliteConnection? _connection;
        private Fixture(ApplicationDbContext db, EditPlanModel page, UserContext user, SqliteConnection? connection) => (Db, Page, User, _connection) = (db, page, user, connection);

        public static async Task<Fixture> CreateAsync(string role, string? action, bool relational = false, bool failAudit = false)
        {
            // FK validation has dedicated PostgreSQL coverage; this disposable
            // SQLite fixture exercises the real transaction/rollback boundaries.
            var connection = relational ? new SqliteConnection("Data Source=:memory:;Foreign Keys=False") : null;
            if (connection is not null) await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>();
            if (connection is not null) options.UseSqlite(connection);
            else options.UseInMemoryDatabase(Guid.NewGuid().ToString());
            var db = new ApplicationDbContext(options.Options);
            if (relational) await db.Database.EnsureCreatedAsync();
            db.Projects.Add(new Project { Id = 1, Name = "Disposable duration test", LeadPoUserId = "po-user", WorkflowVersion = PlanConstants.StageTemplateVersionV1 });
            db.StageTemplates.Add(new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.FS, Name = "FS", Sequence = 10 });
            db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, Status = StageStatus.NotStarted, PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 3) });
            await db.SaveChangesAsync();
            var actor = role == "Project Officer" ? "po-user" : "other-user";
            var user = new UserContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, actor), new Claim(ClaimTypes.Name, actor),
                new Claim(ClaimTypes.Role, role == "Unassigned PO" ? "Project Officer" : role)
            }, "Test")) };
            IAuditService audit = failAudit ? new FailingAudit() : new NullAudit();
            var clock = new TestClock();
            var draft = new PlanDraftService(db, clock, NullLogger<PlanDraftService>.Instance, audit, user);
            var approval = new PlanApprovalService(db, clock, NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db), new NullNotifications());
            var page = new EditPlanModel(db, audit, new PlanGenerationService(db), draft, approval, NullLogger<EditPlanModel>.Instance, user)
            {
                Input = new PlanEditInput
                {
                    ProjectId = 1, Mode = PlanEditorModes.Durations, Action = action,
                    AnchorStart = new DateOnly(2026, 10, 19), SkipHolidays = false,
                    Rows = new() { new() { Code = StageCodes.FS, Name = "FS", DurationDays = 3 } }
                }
            };
            var http = new DefaultHttpContext { User = user.User };
            page.PageContext = new PageContext(new ActionContext(http, new RouteData(), new PageActionDescriptor()));
            page.TempData = new TempDataDictionary(http, new TempProvider());
            return new Fixture(db, page, user, connection);
        }

        public async Task AssertLiveDatesUnchangedAsync()
        {
            Db.ChangeTracker.Clear();
            var stage = await Db.ProjectStages.SingleAsync();
            Assert.Equal(new DateOnly(2024, 1, 1), stage.PlannedStart);
            Assert.Equal(new DateOnly(2024, 1, 3), stage.PlannedDue);
            Assert.Equal(StageStatus.NotStarted, stage.Status);
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
    }

    private sealed class UserContext : IUserContext
    {
        public ClaimsPrincipal User { get; set; } = new();
        public string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero); }
    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
    private sealed class FailingAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
            => throw new InvalidOperationException("Injected publication failure before transaction commit.");
    }
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null) => Task.CompletedTask;
    }
    private sealed class NullNotifications : IPlanNotificationService
    {
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
