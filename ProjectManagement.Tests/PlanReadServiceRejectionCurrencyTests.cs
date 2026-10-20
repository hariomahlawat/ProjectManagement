using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Discovery C1.6: the planner's "Returned for updates" banner must describe the
/// user's current outcome, not the most recent rejection of any age.
/// </summary>
public sealed class PlanReadServiceRejectionCurrencyTests
{
    private const int ProjectId = 93;
    private const string Owner = "po-owner";

    [Fact]
    public async Task Rejection_IsShownWhileTheCorrectionDraftIsOpen()
    {
        await using var db = NewDatabase();
        var approval = Approval(db);
        var submitted = await SeedPendingAsync(db);

        Assert.True(await approval.RejectVersionAsync(submitted.Id, "hod-user", false, true,
            "Allow two more weeks", expectedProjectId: ProjectId));

        var state = (await new PlanReadService(db).GetAsync(ProjectId, Owner)).State;
        Assert.True(state.HasMyDraft);
        Assert.NotNull(state.RejectedOn);
        Assert.Equal("Allow two more weeks", state.RejectionNote);
    }

    [Fact]
    public async Task Rejection_IsNotShownOnceTheCorrectionIsResubmitted()
    {
        await using var db = NewDatabase();
        var approval = Approval(db);
        var submitted = await SeedPendingAsync(db);
        Assert.True(await approval.RejectVersionAsync(submitted.Id, "hod-user", false, true,
            "Allow two more weeks", expectedProjectId: ProjectId));

        await approval.SubmitAsync(ProjectId, Owner);

        var state = (await new PlanReadService(db).GetAsync(ProjectId, Owner)).State;
        Assert.True(state.HasPendingSubmission);
        Assert.True(state.PendingOwnedByCurrentUser);
        Assert.Null(state.RejectedOn);
        Assert.Null(state.RejectionNote);
    }

    [Fact]
    public async Task Rejection_IsNotShownAfterALaterApproval()
    {
        await using var db = NewDatabase();
        var approval = Approval(db);
        var submitted = await SeedPendingAsync(db);
        Assert.True(await approval.RejectVersionAsync(submitted.Id, "hod-user", false, true,
            "Allow two more weeks", expectedProjectId: ProjectId));
        await approval.SubmitAsync(ProjectId, Owner);
        var resubmitted = await db.PlanVersions.SingleAsync(p =>
            p.ProjectId == ProjectId && p.Status == PlanVersionStatus.PendingApproval);

        Assert.True(await approval.ApproveVersionAsync(resubmitted.Id, "hod-user", false, true,
            expectedProjectId: ProjectId));

        var state = (await new PlanReadService(db).GetAsync(ProjectId, Owner)).State;
        Assert.False(state.HasMyDraft);
        Assert.False(state.HasPendingSubmission);
        Assert.Null(state.RejectedOn);
        Assert.Null(state.RejectedBy);
        Assert.Null(state.RejectionNote);
        // The rejected revision itself is untouched: only the banner changed.
        Assert.Equal(PlanVersionStatus.Rejected,
            (await db.PlanVersions.AsNoTracking().SingleAsync(p => p.Id == submitted.Id)).Status);
    }

    [Fact]
    public async Task Rejection_StaysWhenAnotherActorSubmitsALaterRevision()
    {
        await using var db = NewDatabase();
        var approval = Approval(db);
        var submitted = await SeedPendingAsync(db);
        Assert.True(await approval.RejectVersionAsync(submitted.Id, "hod-user", false, true,
            "Allow two more weeks", expectedProjectId: ProjectId));
        // A stage-triggered realignment submits a HoD-owned revision (PlanRealignmentService).
        db.PlanVersions.Add(new PlanVersion
        {
            ProjectId = ProjectId, VersionNo = 99, Status = PlanVersionStatus.PendingApproval,
            Title = PlanVersion.ProjectTimelineTitle,
            CreatedByUserId = "hod-user", SubmittedByUserId = "hod-user",
            SubmittedOn = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var state = (await new PlanReadService(db).GetAsync(ProjectId, Owner)).State;

        Assert.NotNull(state.RejectedOn);
        Assert.Equal("Allow two more weeks", state.RejectionNote);
    }

    private static ApplicationDbContext NewDatabase() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PlanApprovalService Approval(ApplicationDbContext db) =>
        new(db, new FixedClock(), NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(db), new SilentPlanNotifications(), new AlwaysValidTimeline());

    private static async Task<PlanVersion> SeedPendingAsync(ApplicationDbContext db)
    {
        db.Projects.Add(new Project
        {
            Id = ProjectId, Name = "C1.6 rejection currency", LeadPoUserId = Owner,
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });
        var submitted = new PlanVersion
        {
            ProjectId = ProjectId, VersionNo = 1, Status = PlanVersionStatus.PendingApproval,
            Title = PlanVersion.ProjectTimelineTitle,
            CreatedByUserId = Owner, OwnerUserId = Owner, SubmittedByUserId = Owner,
            SubmittedOn = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            AnchorDate = new DateOnly(2026, 11, 2), SkipHolidays = false, PncApplicable = false,
            PlanningInputVersion = PlanningInputVersions.Current
        };
        submitted.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS, SortOrder = 10, DurationDays = 4,
            PlannedStart = new DateOnly(2026, 11, 2), PlannedDue = new DateOnly(2026, 11, 5)
        });
        submitted.ApprovalLogs.Add(new PlanApprovalLog
        {
            Action = "Submitted", PerformedByUserId = Owner, PerformedOn = submitted.SubmittedOn.Value
        });
        db.PlanVersions.Add(submitted);
        await db.SaveChangesAsync();
        return submitted;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class SilentPlanNotifications : IPlanNotificationService
    {
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class AlwaysValidTimeline : IPlannedTimelineValidationService
    {
        public Task<PlannedTimelineValidationResult> ValidateAsync(
            PlannedTimelineCandidate candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlannedTimelineValidationResult(Array.Empty<string>()));
    }
}
