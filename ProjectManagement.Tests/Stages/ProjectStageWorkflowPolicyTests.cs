using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
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

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
