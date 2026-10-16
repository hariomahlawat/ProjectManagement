using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Tests.Stages;

public sealed class ProjectStageWorkflowPolicyTests
{
    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1, StageCodes.IPA, StageCodes.FS)]
    [InlineData(ProcurementWorkflow.VersionV1, StageCodes.SOW, StageCodes.IPA)]
    [InlineData(ProcurementWorkflow.VersionV2, StageCodes.SOW, StageCodes.FS)]
    [InlineData(ProcurementWorkflow.VersionV2, StageCodes.IPA, StageCodes.SOW)]
    public async Task GetAsync_UsesWorkflowSpecificPredecessors(
        string workflowVersion,
        string stageCode,
        string expectedPredecessor)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            WorkflowVersion = workflowVersion
        });
        await db.SaveChangesAsync();

        var policy = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
        var snapshot = await policy.GetAsync(1);

        Assert.Contains(expectedPredecessor, snapshot.RequiredPredecessors(stageCode));
    }


    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1)]
    [InlineData(ProcurementWorkflow.VersionV2)]
    public async Task GetAsync_FallbackParallelTopologyMatchesSeededWorkflow(string workflowVersion)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            WorkflowVersion = workflowVersion
        });
        await db.SaveChangesAsync();

        var policy = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
        var snapshot = await policy.GetAsync(1);

        Assert.Equal(new[] { StageCodes.BID }, snapshot.RequiredPredecessors(StageCodes.BM));
        Assert.Equal(
            new[] { StageCodes.TEC, StageCodes.BM },
            snapshot.RequiredPredecessors(StageCodes.COB));
        Assert.Equal(new[] { StageCodes.COB, StageCodes.PNC }, snapshot.RequiredPredecessors(StageCodes.EAS));
    }

    [Fact]
    public async Task GetAsync_ReturnsTransitivePredecessorsInWorkflowOrder()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "V2",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });
        await db.SaveChangesAsync();

        var policy = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
        var snapshot = await policy.GetAsync(1);
        var closure = snapshot.RequiredPredecessorClosure(StageCodes.AON);

        Assert.Equal(new[] { StageCodes.FS, StageCodes.SOW, StageCodes.IPA }, closure);
    }

    [Fact]
    public async Task GetAsync_UsesWorkflowSpecificStageOrder()
    {
        await using var db = CreateContext();
        db.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "V1",
                CreatedByUserId = "seed",
                WorkflowVersion = ProcurementWorkflow.VersionV1
            },
            new Project
            {
                Id = 2,
                Name = "V2",
                CreatedByUserId = "seed",
                WorkflowVersion = ProcurementWorkflow.VersionV2
            });
        await db.SaveChangesAsync();

        var policy = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
        var v1 = await policy.GetAsync(1);
        var v2 = await policy.GetAsync(2);

        Assert.True(v1.OrderOf(StageCodes.IPA) < v1.OrderOf(StageCodes.SOW));
        Assert.True(v2.OrderOf(StageCodes.SOW) < v2.OrderOf(StageCodes.IPA));
    }

    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1)]
    [InlineData(ProcurementWorkflow.VersionV2)]
    public async Task CurrentPublishedState_WinsOverApprovedHistoryAndIgnoresStaleRows(string version)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Published state", WorkflowVersion = version, ActivePlanVersionNo = 1, PncApplicable = false });
        db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "earlier-po", Status = PlanVersionStatus.Approved, PncApplicable = true });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.COB, Status = StageStatus.Completed },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.NotStarted, PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.EAS, Status = StageStatus.NotStarted });
        // Even a historical/configured unconditional edge is filtered by current applicability.
        db.StageDependencyTemplates.AddRange(
            new StageDependencyTemplate { Version = version, FromStageCode = StageCodes.EAS, DependsOnStageCode = StageCodes.COB },
            new StageDependencyTemplate { Version = version, FromStageCode = StageCodes.EAS, DependsOnStageCode = StageCodes.PNC });
        await db.SaveChangesAsync();
        var policy = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
        var rules = new StageRulesService(db, policy);
        var first = await policy.GetAsync(1);
        Assert.False(first.PncApplicable);
        Assert.True(rules.CanStart(await rules.GetContextAsync(1, default), StageCodes.EAS).Allowed);
        var project = await db.Projects.SingleAsync();
        project.PncApplicable = true;
        Assert.Same(first, await policy.GetAsync(1)); // Unsaved tracked state is not publication.
        await db.SaveChangesAsync();
        Assert.True((await policy.GetAsync(1)).PncApplicable);
        Assert.False(rules.CanStart(await rules.GetContextAsync(1, default), StageCodes.EAS).Allowed);
        project.PncApplicable = false;
        await db.SaveChangesAsync();
        Assert.False((await policy.GetAsync(1)).PncApplicable);
        var stale = await db.ProjectStages.AsNoTracking().SingleAsync(p => p.StageCode == StageCodes.PNC);
        Assert.Equal(StageStatus.NotStarted, stale.Status);
        Assert.Equal(new DateOnly(2024, 1, 1), stale.PlannedStart);
        Assert.Equal(new DateOnly(2024, 1, 2), stale.PlannedDue);
        Assert.True((await db.PlanVersions.AsNoTracking().SingleAsync()).PncApplicable);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
