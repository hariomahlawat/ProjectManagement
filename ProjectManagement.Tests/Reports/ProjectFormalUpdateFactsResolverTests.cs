using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class ProjectFormalUpdateFactsResolverTests
{
    [Fact]
    public async Task Aon_date_is_returned_only_when_Aon_stage_is_completed()
    {
        await using var db = CreateContext();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 101,
            StageCode = StageCodes.AON,
            SortOrder = 120,
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2026, 4, 29)
        });
        await db.SaveChangesAsync();

        var resolver = new ProjectFormalUpdateFactsResolver(db);
        var result = await resolver.ResolveAsync(new[] { 101 });

        Assert.Equal(new DateOnly(2026, 4, 29), result[101].AonDate);
    }

    [Theory]
    [InlineData(StageStatus.NotStarted)]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Blocked)]
    [InlineData(StageStatus.Skipped)]
    public async Task Aon_date_ignores_stale_completed_on_when_stage_is_not_completed(
        StageStatus status)
    {
        await using var db = CreateContext();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 102,
            StageCode = StageCodes.AON,
            SortOrder = 120,
            Status = status,

            // Deliberately inconsistent historical data. The report must not
            // expose this as an AoN completion date unless Status == Completed.
            CompletedOn = new DateOnly(2026, 4, 29)
        });
        await db.SaveChangesAsync();

        var resolver = new ProjectFormalUpdateFactsResolver(db);
        var result = await resolver.ResolveAsync(new[] { 102 });

        Assert.Null(result[102].AonDate);
    }

    [Fact]
    public async Task Completed_Aon_without_completion_date_remains_blank()
    {
        await using var db = CreateContext();

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 103,
            StageCode = StageCodes.AON,
            SortOrder = 120,
            Status = StageStatus.Completed,
            CompletedOn = null,
            RequiresBackfill = true
        });
        await db.SaveChangesAsync();

        var resolver = new ProjectFormalUpdateFactsResolver(db);
        var result = await resolver.ResolveAsync(new[] { 103 });

        Assert.Null(result[103].AonDate);
    }

    [Fact]
    public async Task Resolver_prefers_a_valid_completed_Aon_over_a_newer_non_completed_row()
    {
        await using var db = CreateContext();

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 104,
                StageCode = StageCodes.AON,
                SortOrder = 120,
                Status = StageStatus.Completed,
                CompletedOn = new DateOnly(2026, 3, 15)
            },
            new ProjectStage
            {
                ProjectId = 104,
                StageCode = StageCodes.AON,
                SortOrder = 121,
                Status = StageStatus.InProgress,
                CompletedOn = new DateOnly(2026, 5, 1)
            });
        await db.SaveChangesAsync();

        var resolver = new ProjectFormalUpdateFactsResolver(db);
        var result = await resolver.ResolveAsync(new[] { 104 });

        Assert.Equal(new DateOnly(2026, 3, 15), result[104].AonDate);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ProjectFormalUpdateFacts-{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }
}
