using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Runs the real request, validation, decision and progress services together.
/// The malformed legacy fact is retained until an explicit lifecycle action.
/// </summary>
public sealed class LegacySkippedReopenIntegrityTests
{
    private static readonly DateOnly RecordedStart = new(2026, 1, 10);
    private static readonly DateOnly ConflictingStart = new(2026, 1, 12);
    private static readonly IClock Clock = FakeClock.ForIstDate(2026, 1, 31);

    [Fact]
    public async Task ReadAndValidation_DoNotSilentlyCleanLegacySkippedActualStart()
    {
        await using var db = await CreateSeededContextAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var reader = new ProjectTimelineReadService(db, Clock, new WorkflowStageMetadataProvider(), policy);

        var timeline = await reader.GetAsync(1);
        var validation = await new StageValidationService(db, Clock, policy).ValidateAsync(
            1, StageCodes.FS, "InProgress", RecordedStart, isHoD: false);

        var displayed = Assert.Single(timeline.Items.Where(item => item.Code == StageCodes.FS));
        Assert.Equal(StageStatus.Skipped, displayed.Status);
        Assert.Equal(RecordedStart, displayed.ActualStart);
        Assert.True(validation.IsValid, string.Join(" ", validation.Errors));
        await AssertLegacyStateAsync(db);
        Assert.Empty(await db.StageChangeRequests.ToArrayAsync());
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
        Assert.All(db.ChangeTracker.Entries<ProjectStage>(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public async Task AssignedPo_ConflictingReopenIsRejectedWithoutPendingRequestOrMisleadingHistory()
    {
        await using var db = await CreateSeededContextAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageRequestService(db, Clock, new StageValidationService(db, Clock, policy), policy);

        var result = await service.CreateAsync(ReopenInput(ConflictingStart), "assigned-po");

        Assert.Equal(StageRequestOutcome.ValidationFailed, result.Outcome);
        await AssertLegacyStateAsync(db);
        Assert.Empty(await db.StageChangeRequests.ToArrayAsync());
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    [Fact]
    public async Task AssignedPo_PreservedStartAgreesAcrossRequestReviewApprovalAndAppliedHistory()
    {
        await using var db = await CreateSeededContextAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var requestService = new StageRequestService(db, Clock, new StageValidationService(db, Clock, policy), policy);

        var requested = await requestService.CreateAsync(ReopenInput(RecordedStart), "assigned-po");

        Assert.Equal(StageRequestOutcome.Success, requested.Outcome);
        var request = await db.StageChangeRequests.SingleAsync();
        Assert.Equal(RecordedStart, request.RequestedDate);
        Assert.Equal("InProgress", request.RequestedStatus);
        Assert.Equal("Pending", request.DecisionStatus);
        var requestedHistory = await db.StageChangeLogs.SingleAsync();
        Assert.Equal("Requested", requestedHistory.Action);
        Assert.Equal(RecordedStart, requestedHistory.ToActualStart);
        var reader = new ProjectTimelineReadService(db, Clock, new WorkflowStageMetadataProvider(), policy);
        var pendingReview = Assert.Single((await reader.GetAsync(1)).PendingRequests);
        Assert.Equal(RecordedStart, pendingReview.RequestedDate);
        await AssertLegacyStateAsync(db);

        var approved = await CreateDecisionService(db, policy).DecideAsync(
            new StageDecisionInput(request.Id, StageDecisionAction.Approve, "Approve preserved recorded start"),
            "hod-1", isAdmin: false, isHoD: true);

        Assert.Equal(StageDecisionOutcome.Success, approved.Outcome);
        var persisted = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.InProgress, persisted.Status);
        Assert.Equal(RecordedStart, persisted.ActualStart);
        Assert.Null(persisted.CompletedOn);
        Assert.False(persisted.RequiresBackfill);
        Assert.Equal("Approved", request.DecisionStatus);
        Assert.Equal(RecordedStart, request.RequestedDate);
        var history = await db.StageChangeLogs.OrderBy(log => log.Id).ToArrayAsync();
        Assert.Equal(new[] { "Requested", "Approved", "Applied" }, history.Select(log => log.Action));
        Assert.All(history, log =>
        {
            Assert.Equal(RecordedStart, log.FromActualStart);
            Assert.Equal(RecordedStart, log.ToActualStart);
        });
    }

    [Fact]
    public async Task StaleConflictingPendingRequest_CannotBeApprovedOrClaimed()
    {
        await using var db = await CreateSeededContextAsync();
        var request = new StageChangeRequest
        {
            ProjectId = 1, StageCode = StageCodes.FS,
            RequestedStatus = "InProgress", RequestedDate = ConflictingStart,
            RequestedByUserId = "assigned-po", RequestedOn = Clock.UtcNow,
            DecisionStatus = "Pending", Note = "Old client submitted conflicting reopen"
        };
        db.StageChangeRequests.Add(request);
        await db.SaveChangesAsync();

        var result = await CreateDecisionService(db, StageWorkflowTestFactory.CreatePolicy(db)).DecideAsync(
            new StageDecisionInput(request.Id, StageDecisionAction.Approve, "Attempt old proposal"),
            "hod-1", isAdmin: false, isHoD: true);

        Assert.Equal(StageDecisionOutcome.ValidationFailed, result.Outcome);
        await AssertLegacyStateAsync(db);
        await db.Entry(request).ReloadAsync();
        Assert.Equal("Pending", request.DecisionStatus);
        Assert.Equal(ConflictingStart, request.RequestedDate);
        Assert.Null(request.DecidedByUserId);
        Assert.Null(request.DecidedOn);
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    [Fact]
    public async Task DirectHod_RejectedReplacementThenAcceptedPreservedStartNeverMisrepresentsHistory()
    {
        await using var db = await CreateSeededContextAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, Clock, new StageValidationService(db, Clock, policy),
            new StageRulesService(db, policy), policy);

        await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(
            1, StageCodes.FS, "Reopen", ConflictingStart, "Wrong replacement", "hod-1", false, CancellationToken.None));

        await AssertLegacyStateAsync(db);
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());

        await service.ApplyAsync(1, StageCodes.FS, "Reopen", RecordedStart,
            "Resume with recorded start", "hod-1", false, CancellationToken.None);

        var stage = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(RecordedStart, stage.ActualStart);
        Assert.Null(stage.CompletedOn);
        var history = await db.StageChangeLogs.ToArrayAsync();
        Assert.NotEmpty(history);
        Assert.All(history, log =>
        {
            Assert.Equal(RecordedStart, log.FromActualStart);
            Assert.Equal(RecordedStart, log.ToActualStart);
        });
    }

    [Fact]
    public async Task AssignedPo_ConflictingSecondaryStartCannotCreateDifferentPendingRepresentation()
    {
        await using var db = await CreateSeededContextAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var input = ReopenInput(RecordedStart, ConflictingStart);
        var result = await new StageRequestService(db, Clock, new StageValidationService(db, Clock, policy), policy)
            .CreateAsync(input, "assigned-po");
        Assert.Equal(StageRequestOutcome.ValidationFailed, result.Outcome);
        await AssertLegacyStateAsync(db);
        Assert.Empty(await db.StageChangeRequests.ToArrayAsync());
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    [Fact]
    public async Task StaleConflictingSecondaryStart_CannotApproveDifferentReviewValue()
    {
        await using var db = await CreateSeededContextAsync();
        var request = new StageChangeRequest { ProjectId = 1, StageCode = StageCodes.FS,
            RequestedStatus = "InProgress", RequestedDate = RecordedStart, RequestedStartDate = ConflictingStart,
            RequestedByUserId = "assigned-po", RequestedOn = Clock.UtcNow, DecisionStatus = "Pending" };
        db.StageChangeRequests.Add(request); await db.SaveChangesAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var review = Assert.Single((await new ProjectTimelineReadService(db, Clock, new WorkflowStageMetadataProvider(), policy)
            .GetAsync(1)).PendingRequests);
        Assert.Equal(ConflictingStart, review.ProposedStartDate);
        // Preserve evidence on the old request; block it instead of silently changing the reviewed fact.
        var assessment = await new StageApprovalSequenceService(db, policy, new ProjectFactsReadService(db)).AssessRequestAsync(request.Id);
        Assert.False(assessment!.CanApprove);
        var result = await CreateDecisionService(db, policy).DecideAsync(
            new StageDecisionInput(request.Id, StageDecisionAction.Approve, "Old ambiguous proposal"),
            "hod-1", isAdmin: false, isHoD: true);
        Assert.Equal(StageDecisionOutcome.ValidationFailed, result.Outcome);
        await AssertLegacyStateAsync(db);
        Assert.Equal("Pending", request.DecisionStatus);
        Assert.Equal(ConflictingStart, request.RequestedStartDate);
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    private static StageChangeRequestInput ReopenInput(DateOnly date, DateOnly? secondaryStart = null) => new()
    {
        ProjectId = 1, StageCode = StageCodes.FS,
        RequestedStatus = "InProgress", RequestedDate = date, RequestedStartDate = secondaryStart, Note = "Resume existing recorded work"
    };

    private static async Task<ApplicationDbContext> CreateSeededContextAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project
        {
            Id = 1, Name = "Disposable legacy reopen", CreatedByUserId = "seed",
            LeadPoUserId = "assigned-po", HodUserId = "hod-1", WorkflowVersion = ProcurementWorkflow.VersionV1
        });
        // Include the ordinary workflow skeleton so direct apply need not create
        // unrelated missing rows before testing this explicit lifecycle action.
        db.ProjectStages.AddRange(ProcurementWorkflow.StageDefinitionsFor(ProcurementWorkflow.VersionV1)
            .Select((definition, index) => new ProjectStage
            {
                ProjectId = 1, StageCode = definition.Code, SortOrder = index,
                Status = definition.Code == StageCodes.FS ? StageStatus.Skipped : StageStatus.NotStarted,
                ActualStart = definition.Code == StageCodes.FS ? RecordedStart : null
            }));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task AssertLegacyStateAsync(ApplicationDbContext db)
    {
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync(stage => stage.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.Skipped, stage.Status);
        Assert.Equal(RecordedStart, stage.ActualStart);
        Assert.Null(stage.CompletedOn);
    }

    private static StageDecisionService CreateDecisionService(ApplicationDbContext db, IProjectStageWorkflowPolicy policy)
    {
        var progress = new StageProgressService(db, Clock, new NullAudit(), new ProjectFactsReadService(db),
            new NullNotifications(), policy);
        return new StageDecisionService(db, Clock, progress, NullLogger<StageDecisionService>.Instance, policy);
    }

    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null,
            Microsoft.AspNetCore.Http.HttpContext? http = null) => Task.CompletedTask;
    }

    private sealed class NullNotifications : IStageNotificationService
    {
        public Task NotifyStageStatusChangedAsync(ProjectStage stage, Project project, StageStatus previousStatus,
            string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
