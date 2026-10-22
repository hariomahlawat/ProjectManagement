using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProcurementPncStepTests
{
    [Fact]
    public async Task SkippedPncStage_WithNoPncCost_LeavesTheLadder()
    {
        await using var db = CreateContext();
        AddProject(db, 1, StageStatus.Skipped);
        await db.SaveChangesAsync();

        var procurement = await new ProjectProcurementReadService(db).GetAsync(1);

        Assert.True(procurement.IsPncStageSkipped);
        Assert.False(procurement.ShowsPncStep);
    }

    [Fact]
    public async Task SkippedPncStage_WithARecordedPncCost_StillShowsIt()
    {
        await using var db = CreateContext();
        AddProject(db, 1, StageStatus.Skipped);
        db.ProjectPncFacts.Add(new ProjectPncFact
        {
            ProjectId = 1,
            PncCost = 2_500_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var procurement = await new ProjectProcurementReadService(db).GetAsync(1);

        Assert.True(procurement.ShowsPncStep);
    }

    [Theory]
    [InlineData(StageStatus.NotStarted)]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Completed)]
    public async Task PncStageNotSkipped_KeepsTheStep(StageStatus status)
    {
        await using var db = CreateContext();
        AddProject(db, 1, status);
        // Another project's skipped PNC must not leak into this one.
        AddProject(db, 2, StageStatus.Skipped);
        await db.SaveChangesAsync();

        var procurement = await new ProjectProcurementReadService(db).GetAsync(1);

        Assert.False(procurement.IsPncStageSkipped);
        Assert.True(procurement.ShowsPncStep);
    }

    private static void AddProject(ApplicationDbContext db, int id, StageStatus pncStatus)
    {
        db.Projects.Add(new Project { Id = id, Name = $"Project {id}", CreatedByUserId = "seed" });
        db.ProjectStages.Add(new ProjectStage { ProjectId = id, StageCode = StageCodes.PNC, Status = pncStatus });
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
