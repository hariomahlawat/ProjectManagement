using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class TimelinePublishedAuthorityRegressionTests
{
    private static readonly DateOnly PublishedDate = new(2026, 1, 20);

    [Theory]
    [InlineData(StageStatus.Completed, 10)]
    [InlineData(StageStatus.Completed, 30)]
    [InlineData(StageStatus.NotStarted, 0)]
    public async Task Read_UsesPublishedStateAndBoundaryWithoutRepair(StageStatus localStatus, int localDay)
    {
        await using var db = await SeedAsync(localStatus, localDay, published: true);
        var metadata = new WorkflowStageMetadataProvider();
        var service = new ProjectTimelineReadService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), metadata, new ProjectStageWorkflowPolicy(db, metadata));
        var timeline = await service.GetAsync(1);
        var ipa = timeline.Items.Single(row => row.Code == StageCodes.IPA);
        var sow = timeline.Items.Single(row => row.Code == StageCodes.SOW);
        Assert.Equal(StageStatus.Completed, ipa.Status);
        Assert.Equal(PublishedDate, ipa.CompletedOn);
        Assert.Equal(PublishedDate, sow.EarliestAllowedStartDate);
        Assert.Equal(PublishedDate.AddDays(1), sow.SuggestedStartDate);
        Assert.Equal(PublishedDate.AddDays(1), sow.ReopenSuggestedStartDate);
        var editor = await service.GetActualsEditorAsync(1);
        Assert.Equal(PublishedDate, editor.Rows.Single(row => row.StageCode == StageCodes.SOW).EarliestAllowedStartDate);
        Assert.Equal(StageStatus.Completed, editor.Rows.Single(row => row.StageCode == StageCodes.IPA).Status);
        Assert.Equal(PublishedDate, editor.Rows.Single(row => row.StageCode == StageCodes.IPA).CompletedOn);
        await AssertUnchangedIpaAsync(db, localStatus, localDay);
    }

    [Fact]
    public async Task Read_PublishedCompletionRemovesStaleIpaBackfillWarningWithoutRepair()
    {
        await using var db = await SeedAsync(StageStatus.Completed, 0, published: true);
        var metadata = new WorkflowStageMetadataProvider();
        var service = new ProjectTimelineReadService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), metadata, new ProjectStageWorkflowPolicy(db, metadata));
        Assert.False(await service.HasBackfillAsync(1));
        Assert.False((await service.GetAsync(1)).Items.Single(row => row.Code == StageCodes.IPA).RequiresBackfill);
        await AssertUnchangedIpaAsync(db, StageStatus.Completed, 0);
    }

    [Theory]
    [InlineData(StageStatus.Completed, 10, 15, false)]
    [InlineData(StageStatus.Completed, 30, 20, true)]
    [InlineData(StageStatus.NotStarted, 0, 20, true)]
    public async Task Validation_UsesPublishedStateAndDate(StageStatus localStatus, int localDay, int startDay, bool valid)
    {
        await using var db = await SeedAsync(localStatus, localDay, published: true);
        var service = new StageValidationService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), StageWorkflowTestFactory.CreatePolicy(db));
        var result = await service.ValidateAsync(1, StageCodes.SOW, "InProgress", new DateOnly(2026, 1, startDay), isHoD: false);
        Assert.Equal(valid, result.IsValid);
        await AssertUnchangedIpaAsync(db, localStatus, localDay);
    }

    [Theory]
    [InlineData(10, 15, false)]
    [InlineData(30, 20, true)]
    public async Task ActualCorrection_UsesPublishedBoundaryWithoutSubmittingIpa(int localDay, int startDay, bool valid)
    {
        await using var db = await SeedAsync(StageStatus.Completed, localDay, published: true);
        var service = new StageActualsUpdateService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), new NoAudit(), NullLogger<StageActualsUpdateService>.Instance);
        var input = new ActualsEditInput { ProjectId = 1, Rows = new List<ActualsEditRowInput>() { new() { StageCode = StageCodes.SOW, ActualStart = new DateOnly(2026, 1, startDay) } } };
        if (valid) Assert.Equal(1, (await service.UpdateAsync(input, "hod", "HoD")).UpdatedCount);
        else await Assert.ThrowsAsync<StageActualsValidationException>(() => service.UpdateAsync(input, "hod", "HoD"));
        await AssertUnchangedIpaAsync(db, StageStatus.Completed, localDay);
    }

    [Theory]
    [InlineData(StageStatus.Completed, true)]
    [InlineData(StageStatus.Blocked, false)]
    [InlineData(StageStatus.Skipped, false)]
    public async Task Read_CompletedReopenDefaultIsNotAppliedToBlockedOrSkipped(StageStatus status, bool suggested)
    {
        await using var db = await SeedAsync(StageStatus.Completed, 10, published: false);
        var target = await db.ProjectStages.SingleAsync(row => row.StageCode == StageCodes.SOW);
        target.Status = status; target.CompletedOn = status == StageStatus.Completed ? new DateOnly(2026, 1, 31) : null;
        await db.SaveChangesAsync();
        var metadata = new WorkflowStageMetadataProvider();
        var service = new ProjectTimelineReadService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), metadata, new ProjectStageWorkflowPolicy(db, metadata));
        Assert.Equal(suggested ? new DateOnly(2026, 1, 11) : (DateOnly?)null,
            (await service.GetAsync(1)).Items.Single(row => row.Code == StageCodes.SOW).ReopenSuggestedStartDate);
    }

    [Fact]
    public async Task NoPublishedAuthority_ManualCompletionRemainsEditableAndUsable()
    {
        await using var db = await SeedAsync(StageStatus.Completed, 10, published: false);
        var service = new StageActualsUpdateService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), new NoAudit(), NullLogger<StageActualsUpdateService>.Instance);
        await service.UpdateAsync(new ActualsEditInput { ProjectId = 1, Rows = new List<ActualsEditRowInput>() { new() { StageCode = StageCodes.IPA, CompletedOn = new DateOnly(2026, 1, 15) } } }, "hod", "HoD");
        var validation = new StageValidationService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), StageWorkflowTestFactory.CreatePolicy(db));
        Assert.True((await validation.ValidateAsync(1, StageCodes.SOW, "InProgress", new DateOnly(2026, 1, 15), isHoD: false)).IsValid);
        Assert.False(await new ArppIpaStageAuthorityService(db).IsManagedAsync(1));
    }

    [Theory]
    [InlineData(StageStatus.Completed, 10, 15, false)]
    [InlineData(StageStatus.Completed, 30, 20, true)]
    [InlineData(StageStatus.NotStarted, 0, 20, true)]
    public async Task ApprovalAssessmentAndDecision_UsePublishedChronology(StageStatus localStatus, int localDay, int startDay, bool valid)
    {
        await using var db = await SeedAsync(localStatus, localDay, published: true);
        var target = await db.ProjectStages.SingleAsync(row => row.StageCode == StageCodes.SOW);
        target.Status = StageStatus.NotStarted; target.CompletedOn = null;
        var request = new StageChangeRequest { ProjectId = 1, StageCode = StageCodes.SOW, RequestedStatus = "InProgress", RequestedDate = new DateOnly(2026, 1, startDay), RequestedByUserId = "po", RequestedOn = DateTimeOffset.UtcNow, DecisionStatus = "Pending" };
        db.StageChangeRequests.Add(request); await db.SaveChangesAsync();
        var workflow = StageWorkflowTestFactory.CreatePolicy(db);
        var facts = new ProjectFactsReadService(db);
        var sequence = new StageApprovalSequenceService(db, workflow, facts);
        Assert.Equal(valid, (await sequence.AssessRequestAsync(request.Id))!.CanApprove);
        var clock = FakeClock.ForIstDate(new DateOnly(2026, 2, 1));
        var progress = new StageProgressService(db, clock, new NoAudit(), facts, new NoNotifications(), workflow);
        var decision = new StageDecisionService(db, clock, progress, NullLogger<StageDecisionService>.Instance, workflow);
        var result = await decision.DecideAsync(new StageDecisionInput(request.Id, StageDecisionAction.Approve, null), "hod", isAdmin: false, isHoD: true);
        Assert.Equal(valid ? StageDecisionOutcome.Success : StageDecisionOutcome.ValidationFailed, result.Outcome);
        await AssertUnchangedIpaAsync(db, localStatus, localDay);
    }

    [Theory]
    [InlineData(StageStatus.Completed)]
    [InlineData(StageStatus.Blocked)]
    public async Task ValidationAndDecision_RejectConflictingRecordedReopenStart(StageStatus status)
    {
        await using var db = await SeedAsync(StageStatus.Completed, 10, published: false);
        var stage = await db.ProjectStages.SingleAsync(row => row.StageCode == StageCodes.FS);
        stage.Status = status; stage.ActualStart = new DateOnly(2026, 1, 10);
        stage.CompletedOn = status == StageStatus.Completed ? new DateOnly(2026, 1, 20) : null;
        var request = new StageChangeRequest { ProjectId = 1, StageCode = StageCodes.FS, RequestedStatus = "InProgress", RequestedDate = new DateOnly(2026, 1, 12), RequestedByUserId = "po", RequestedOn = DateTimeOffset.UtcNow, DecisionStatus = "Pending" };
        db.StageChangeRequests.Add(request); await db.SaveChangesAsync();
        var workflow = StageWorkflowTestFactory.CreatePolicy(db);
        var clock = FakeClock.ForIstDate(new DateOnly(2026, 2, 1));
        var validator = new StageValidationService(db, clock, workflow);
        Assert.False((await validator.ValidateAsync(1, StageCodes.FS, "InProgress", request.RequestedDate, isHoD: false)).IsValid);
        var sequence = new StageApprovalSequenceService(db, workflow, new ProjectFactsReadService(db));
        Assert.False((await sequence.AssessRequestAsync(request.Id))!.CanApprove);
        var progress = new StageProgressService(db, clock, new NoAudit(), new ProjectFactsReadService(db), new NoNotifications(), workflow);
        var result = await new StageDecisionService(db, clock, progress, NullLogger<StageDecisionService>.Instance, workflow)
            .DecideAsync(new StageDecisionInput(request.Id, StageDecisionAction.Approve, null), "hod", isAdmin: false, isHoD: true);
        Assert.Equal(StageDecisionOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("Pending", request.DecisionStatus);
        Assert.Equal(status, stage.Status); Assert.Equal(new DateOnly(2026, 1, 10), stage.ActualStart);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Blocked")]
    public async Task Validation_RejectsPreservedFutureStart(string targetStatus)
    {
        await using var db = await SeedAsync(StageStatus.Completed, 10, published: false);
        var stage = await db.ProjectStages.SingleAsync(row => row.StageCode == StageCodes.FS);
        stage.ActualStart = new DateOnly(2026, 2, 2); await db.SaveChangesAsync();
        var service = new StageValidationService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), StageWorkflowTestFactory.CreatePolicy(db));
        Assert.False((await service.ValidateAsync(1, StageCodes.FS, targetStatus, null, isHoD: true)).IsValid);
        Assert.Equal(new DateOnly(2026, 2, 2), stage.ActualStart);
    }

    private sealed class NoNotifications : IStageNotificationService
    {
        public Task NotifyStageStatusChangedAsync(ProjectStage stage, Project project, StageStatus previousStatus, string actorUserId, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(StageStatus.NotStarted)]
    [InlineData(StageStatus.InProgress)]
    public async Task ActualCorrection_PublishedStaleStatusAllowsIndependentStartWithCoherentHistory(StageStatus status)
    {
        await using var db = await SeedAsync(status, 0, published: true);
        var service = new StageActualsUpdateService(db, FakeClock.ForIstDate(new DateOnly(2026, 2, 1)), new NoAudit(), NullLogger<StageActualsUpdateService>.Instance);
        Assert.Equal(1, (await service.UpdateAsync(new ActualsEditInput { ProjectId = 1,
            Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.IPA, ActualStart = new DateOnly(2026, 1, 10) } } }, "hod", "HoD")).UpdatedCount);
        var ipa = await db.ProjectStages.AsNoTracking().SingleAsync(row => row.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, ipa.Status);
        Assert.Equal(PublishedDate, ipa.CompletedOn);
        Assert.Equal(new DateOnly(2026, 1, 10), ipa.ActualStart);
        var log = await db.StageChangeLogs.SingleAsync();
        Assert.Equal(status.ToString(), log.FromStatus);
        Assert.Equal("Completed", log.ToStatus);
        Assert.Null(log.FromActualStart); Assert.Null(log.FromCompletedOn);
        Assert.Equal(ipa.ActualStart, log.ToActualStart); Assert.Equal(PublishedDate, log.ToCompletedOn);
    }

    [Fact]
    public async Task Read_ReopenSuggestionCapsPublishedIpaBoundaryWithoutChangingSource()
    {
        await using var db = await SeedAsync(StageStatus.NotStarted, 0, published: true);
        var sow = await db.ProjectStages.SingleAsync(row => row.StageCode == StageCodes.SOW);
        sow.CompletedOn = PublishedDate; await db.SaveChangesAsync();
        var metadata = new WorkflowStageMetadataProvider();
        var policy = new ProjectStageWorkflowPolicy(db, metadata);
        var clock = FakeClock.ForIstDate(PublishedDate);
        var timeline = await new ProjectTimelineReadService(db, clock, metadata, policy).GetAsync(1);
        var item = timeline.Items.Single(row => row.Code == StageCodes.SOW);
        Assert.Equal(PublishedDate, item.ReopenSuggestedStartDate);
        Assert.Equal(PublishedDate, item.EarliestAllowedStartDate);
        Assert.Equal(PublishedDate.AddDays(1), item.SuggestedStartDate);
        Assert.True((await new StageValidationService(db, clock, policy).ValidateAsync(1, StageCodes.SOW,
            "InProgress", PublishedDate, isHoD: false)).IsValid);
        Assert.Null(sow.ActualStart); Assert.Equal(PublishedDate, sow.CompletedOn);
        await AssertUnchangedIpaAsync(db, StageStatus.NotStarted, 0);
    }

    private static async Task<ApplicationDbContext> SeedAsync(StageStatus ipaStatus, int ipaDay, bool published)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project { Id = 1, Name = "Authority", CreatedByUserId = "seed", WorkflowVersion = ProcurementWorkflow.VersionV1 });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, Status = StageStatus.Completed, CompletedOn = new DateOnly(2026, 1, 1) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.IPA, Status = ipaStatus, CompletedOn = ipaDay == 0 ? null : new DateOnly(2026, 1, ipaDay) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.SOW, Status = StageStatus.Completed, CompletedOn = new DateOnly(2026, 1, 31) });
        if (published) AddPublishedPosition(db, 1, PublishedDate);
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task AssertUnchangedIpaAsync(ApplicationDbContext db, StageStatus status, int day)
    {
        Assert.False(db.ChangeTracker.HasChanges());
        var ipa = await db.ProjectStages.AsNoTracking().SingleAsync(row => row.StageCode == StageCodes.IPA);
        Assert.Equal(status, ipa.Status);
        Assert.Equal(day == 0 ? null : new DateOnly(2026, 1, day), ipa.CompletedOn);
        Assert.Null(ipa.ActualStart);
        Assert.Empty(await db.StageChangeLogs.Where(row => row.StageCode == StageCodes.IPA).ToListAsync());
    }

    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string,string?>? data = null, Microsoft.AspNetCore.Http.HttpContext? http = null) => Task.CompletedTask;
    }
    private static void AddPublishedPosition(ApplicationDbContext db, int projectId, DateOnly issueDate)
    {
        var issue = new ArppIssue
        {
            Id = 100,
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "ARPP/I&R&D/ARTRAC",
            IssueDate = issueDate,
            IsVerified = true,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            VerifiedByUserId = "verifier",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

        issue.PublishedSnapshot = new ArppPublishedIssue
        {
            ArppIssueId = issue.Id,
            RevisionNumber = 1,
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = issue.Name,
            IssueDate = issueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = "arpp/100.pdf",
            AttachmentOriginalFileName = "ARPP-100.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64),
            Entries =
            {
                new ArppPublishedEntry
                {
                    ArppIssueId = issue.Id,
                    SourceEntryId = 1,
                    SortOrder = 1,
                    SerialNumber = "33",
                    ProjectReference = "Project",
                    ProjectId = projectId,
                    Category = ArppCategory.New,
                    IpaCost = 40_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3"
                }
            }
        };

        db.ArppIssues.Add(issue);
    }

}
