using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanApprovalValidationSkippedStagesTests
{
    [Fact]
    public async Task SubmitForApproval_AllowsMissingPlanForSkippedStage()
    {
        // SECTION: Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.FS,
                Name = "Feasibility Study",
                Sequence = 10
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.IPA,
                Name = "In-Principle Approval",
                Sequence = 20
            });

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Skipped Stage Project",
            LeadPoUserId = "owner",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.FS,
                Status = StageStatus.Skipped
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                Status = StageStatus.NotStarted
            });

        var plan = new PlanVersion
        {
            ProjectId = 1,
            VersionNo = 1,
            Title = "Draft",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "owner",
            OwnerUserId = "owner",
            CreatedOn = DateTimeOffset.UtcNow
        };

        plan.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.IPA,
            PlannedStart = new DateOnly(2024, 1, 1),
            PlannedDue = new DateOnly(2024, 1, 5)
        });

        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();

        var approval = new PlanApprovalService(
            db,
            new TestClock(),
            NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(db),
            new NullPlanNotificationService());

        // SECTION: Act
        await approval.SubmitForApprovalAsync(1, "owner");

        // SECTION: Assert
        var updatedPlan = await db.PlanVersions.SingleAsync();
        Assert.Equal(PlanVersionStatus.PendingApproval, updatedPlan.Status);
    }

    [Fact]
    public async Task SubmitForApproval_SkipsDependencyValidationWhenDependencyStageIsSkipped()
    {
        // SECTION: Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.StageTemplates.AddRange(
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.FS,
                Name = "Feasibility Study",
                Sequence = 10
            },
            new StageTemplate
            {
                Version = PlanConstants.StageTemplateVersionV1,
                Code = StageCodes.SOW,
                Name = "SOW Vetting",
                Sequence = 20
            });

        db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.SOW,
            DependsOnStageCode = StageCodes.FS
        });

        db.Projects.Add(new Project
        {
            Id = 2,
            Name = "Skipped Dependency Project",
            LeadPoUserId = "owner",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 2,
                StageCode = StageCodes.FS,
                Status = StageStatus.Skipped
            },
            new ProjectStage
            {
                ProjectId = 2,
                StageCode = StageCodes.SOW,
                Status = StageStatus.NotStarted
            });

        var plan = new PlanVersion
        {
            ProjectId = 2,
            VersionNo = 1,
            Title = "Draft",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "owner",
            OwnerUserId = "owner",
            CreatedOn = DateTimeOffset.UtcNow
        };

        plan.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.SOW,
            PlannedStart = new DateOnly(2024, 2, 1),
            PlannedDue = new DateOnly(2024, 2, 10)
        });

        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();

        var approval = new PlanApprovalService(
            db,
            new TestClock(),
            NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(db),
            new NullPlanNotificationService());

        // SECTION: Act
        await approval.SubmitForApprovalAsync(2, "owner");

        // SECTION: Assert
        var updatedPlan = await db.PlanVersions.SingleAsync(p => p.ProjectId == 2);
        Assert.Equal(PlanVersionStatus.PendingApproval, updatedPlan.Status);
    }

    // SECTION: Test Doubles
    private sealed class NullPlanNotificationService : IPlanNotificationService
    {
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string approverUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string approverUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string submitterUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
