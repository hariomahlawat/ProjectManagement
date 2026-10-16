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
        Assert.IsType<ForbidResult>(await f.Page.OnPostAsync(1, default));
        Assert.Empty(await f.Db.ProjectPlanDurations.ToListAsync());
        await f.AssertLiveDatesUnchangedAsync();
    }

    [Fact]
    public async Task LegacyAjaxSaveDraft_DoesNotDirectApplyAndReturnsJson()
    {
        await using var f = await Fixture.CreateAsync("HoD", "SaveDraft");
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

        private Fixture(ApplicationDbContext db, EditPlanModel page, UserContext user) => (Db, Page, User) = (db, page, user);

        public static async Task<Fixture> CreateAsync(string role, string action)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
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
            var audit = new NullAudit();
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
            return new Fixture(db, page, user);
        }

        public async Task AssertLiveDatesUnchangedAsync()
        {
            Db.ChangeTracker.Clear();
            var stage = await Db.ProjectStages.SingleAsync();
            Assert.Equal(new DateOnly(2024, 1, 1), stage.PlannedStart);
            Assert.Equal(new DateOnly(2024, 1, 3), stage.PlannedDue);
            Assert.Equal(StageStatus.NotStarted, stage.Status);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
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
