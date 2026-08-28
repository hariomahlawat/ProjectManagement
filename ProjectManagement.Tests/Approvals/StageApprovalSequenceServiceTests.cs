using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Approvals;

public sealed class StageApprovalSequenceServiceTests
{
    [Fact]
    public async Task AssessRequestAsync_BenchmarkingStartDoesNotWaitForTechnicalEvaluation()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db,
            new StageSeed(StageCodes.BID, StageStatus.Completed, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 6)),
            new StageSeed(StageCodes.TEC, StageStatus.InProgress, new DateOnly(2026, 8, 7), null),
            new StageSeed(StageCodes.BM, StageStatus.NotStarted, null, null));
        await SeedDependenciesAsync(db,
            (StageCodes.BM, StageCodes.BID));

        var technicalEvaluationRequest = new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.TEC,
            RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = new DateOnly(2026, 8, 21),
            RequestedStartDate = new DateOnly(2026, 8, 7),
            RequestedByUserId = "po-1",
            RequestedOn = new DateTimeOffset(2026, 8, 22, 6, 0, 0, TimeSpan.Zero),
            DecisionStatus = "Pending"
        };
        var benchmarkingRequest = new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.BM,
            RequestedStatus = StageStatus.InProgress.ToString(),
            RequestedDate = new DateOnly(2026, 8, 10),
            RequestedByUserId = "po-1",
            RequestedOn = new DateTimeOffset(2026, 8, 22, 6, 5, 0, TimeSpan.Zero),
            DecisionStatus = "Pending"
        };
        db.StageChangeRequests.AddRange(technicalEvaluationRequest, benchmarkingRequest);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var assessment = await service.AssessRequestAsync(benchmarkingRequest.Id);

        Assert.NotNull(assessment);
        Assert.Equal(ApprovalReadiness.Ready, assessment!.Readiness);
        Assert.True(assessment.CanApprove);
        Assert.Empty(assessment.WaitingOnRequestIds);
        Assert.DoesNotContain(
            assessment.Checks,
            check => check.Detail?.Contains("Technical Evaluation", StringComparison.OrdinalIgnoreCase) == true
                && check.State == ApprovalCheckState.Blocked);
    }

    [Fact]
    public async Task AssessRequestAsync_CommercialOpeningUsesLaterParallelCompletionBoundary()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db,
            new StageSeed(StageCodes.TEC, StageStatus.Completed, new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 25)),
            new StageSeed(StageCodes.BM, StageStatus.Completed, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 21)),
            new StageSeed(StageCodes.COB, StageStatus.NotStarted, null, null));
        await SeedDependenciesAsync(db,
            (StageCodes.COB, StageCodes.TEC),
            (StageCodes.COB, StageCodes.BM));

        var commercialOpeningRequest = new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.COB,
            RequestedStatus = StageStatus.InProgress.ToString(),
            RequestedDate = new DateOnly(2026, 8, 22),
            RequestedByUserId = "po-1",
            RequestedOn = new DateTimeOffset(2026, 8, 26, 6, 0, 0, TimeSpan.Zero),
            DecisionStatus = "Pending"
        };
        db.StageChangeRequests.Add(commercialOpeningRequest);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var assessment = await service.AssessRequestAsync(commercialOpeningRequest.Id);

        Assert.NotNull(assessment);
        Assert.Equal(ApprovalReadiness.Blocked, assessment!.Readiness);
        Assert.False(assessment.CanApprove);
        Assert.Contains(assessment.Checks, check =>
            check.State == ApprovalCheckState.Blocked
            && string.Equals(check.Label, "Lifecycle chronology", StringComparison.OrdinalIgnoreCase)
            && check.Detail?.Contains("25 Aug 2026", StringComparison.OrdinalIgnoreCase) == true
            && check.Detail?.Contains("Technical Evaluation", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static StageApprovalSequenceService CreateService(ApplicationDbContext db) => new(
        db,
        new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()),
        new ProjectFactsReadService(db));

    private static async Task SeedProjectAsync(ApplicationDbContext db, params StageSeed[] stages)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            LeadPoUserId = "po-1",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });

        var sortOrder = 1;
        foreach (var stage in stages)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1,
                StageCode = stage.Code,
                SortOrder = sortOrder++,
                Status = stage.Status,
                ActualStart = stage.ActualStart,
                CompletedOn = stage.CompletedOn
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedDependenciesAsync(
        ApplicationDbContext db,
        params (string Stage, string Predecessor)[] dependencies)
    {
        foreach (var (stage, predecessor) in dependencies)
        {
            db.StageDependencyTemplates.Add(new StageDependencyTemplate
            {
                Version = ProcurementWorkflow.VersionV2,
                FromStageCode = stage,
                DependsOnStageCode = predecessor
            });
        }

        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed record StageSeed(
        string Code,
        StageStatus Status,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);
}
