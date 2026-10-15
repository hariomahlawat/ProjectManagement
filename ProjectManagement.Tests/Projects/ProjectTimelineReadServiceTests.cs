using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectTimelineReadServiceTests
{
    [Theory]
    [InlineData("dated", "2026-08-08")]
    [InlineData("undated", null)]
    [InlineData("first", null)]
    [InlineData("skipped", "2026-08-08")]
    [InlineData("converging", "2026-08-12")]
    [InlineData("converging-undated", null)]
    [InlineData("working-holiday", "2026-08-11")]
    public async Task GetAsync_ReopenSuggestionUsesOnlyDeterminedRecordedGraphBoundary(string scenario, string? expected)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Reopen", CreatedByUserId = "seed", WorkflowVersion = ProcurementWorkflow.VersionV2 });
        var target = scenario == "first" ? StageCodes.FS : StageCodes.BM;
        db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = target, Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2026, 8, 20), PlannedStart = new DateOnly(2026, 8, 1) });
        if (scenario != "first")
        {
            db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.BID,
                Status = scenario == "skipped" ? StageStatus.Skipped : StageStatus.Completed,
                CompletedOn = scenario is "undated" or "skipped" ? null : new DateOnly(2026, 8, 7), RequiresBackfill = scenario == "undated" });
            db.StageDependencyTemplates.Add(new StageDependencyTemplate { Version = ProcurementWorkflow.VersionV2, FromStageCode = target, DependsOnStageCode = StageCodes.BID });
        }
        if (scenario == "skipped")
        {
            db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.AON, Status = StageStatus.Completed, CompletedOn = new DateOnly(2026, 8, 7) });
            db.StageDependencyTemplates.Add(new StageDependencyTemplate { Version = ProcurementWorkflow.VersionV2, FromStageCode = StageCodes.BID, DependsOnStageCode = StageCodes.AON });
        }
        if (scenario.StartsWith("converging", StringComparison.Ordinal))
        {
            db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.TEC, Status = StageStatus.Completed,
                CompletedOn = scenario == "converging-undated" ? null : new DateOnly(2026, 8, 11), RequiresBackfill = scenario == "converging-undated" });
            db.StageDependencyTemplates.Add(new StageDependencyTemplate { Version = ProcurementWorkflow.VersionV2, FromStageCode = target, DependsOnStageCode = StageCodes.TEC });
        }
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings { ProjectId = 1, IncludeWeekends = false, SkipHolidays = true,
            NextStageStartPolicy = scenario == "working-holiday" ? NextStageStartPolicies.NextWorkingDay : NextStageStartPolicies.SameDay });
        if (scenario == "working-holiday")
            db.Holidays.Add(new Holiday { Date = new DateOnly(2026, 8, 10), Name = "Office holiday", Type = HolidayType.Gazetted });
        await db.SaveChangesAsync();
        var metadata = new WorkflowStageMetadataProvider();
        var service = new ProjectTimelineReadService(db, new FixedClock(new DateTimeOffset(2026, 8, 28, 6, 30, 0, TimeSpan.Zero)), metadata, new ProjectStageWorkflowPolicy(db, metadata));
        var timeline = await service.GetAsync(1);
        var item = timeline.Items.Single(stage => stage.Code == target);
        Assert.Equal(expected is null ? (DateOnly?)null : DateOnly.Parse(expected), item.ReopenSuggestedStartDate);
        Assert.Null((await db.ProjectStages.SingleAsync(stage => stage.StageCode == target)).ActualStart);
        // Reopen policy must not change the established non-reopen calendar suggestion.
        if (scenario == "working-holiday") Assert.Equal(new DateOnly(2026, 8, 8), item.SuggestedStartDate);
    }

    [Fact]
    public async Task GetAsync_BenchmarkingSuggestionUsesBid_NotTechnicalEvaluation()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Parallel Timeline Project",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });
        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.BID,
                SortOrder = 5,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2026, 8, 1),
                CompletedOn = new DateOnly(2026, 8, 6)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.TEC,
                SortOrder = 6,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2026, 8, 7),
                CompletedOn = new DateOnly(2026, 8, 21)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.BM,
                SortOrder = 7,
                Status = StageStatus.NotStarted
            });
        db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = ProcurementWorkflow.VersionV2,
            FromStageCode = StageCodes.BM,
            DependsOnStageCode = StageCodes.BID
        });
        await db.SaveChangesAsync();

        var metadata = new WorkflowStageMetadataProvider();
        var policy = new ProjectStageWorkflowPolicy(db, metadata);
        var service = new ProjectTimelineReadService(
            db,
            new FixedClock(new DateTimeOffset(2026, 8, 28, 6, 30, 0, TimeSpan.Zero)),
            metadata,
            policy);

        var timeline = await service.GetAsync(1);
        var benchmarking = timeline.Items.Single(item => item.Code == StageCodes.BM);

        Assert.Equal(new DateOnly(2026, 8, 6), benchmarking.EarliestAllowedStartDate);
        Assert.Equal(new DateOnly(2026, 8, 7), benchmarking.SuggestedStartDate);
        Assert.Equal(StageCodes.BID, benchmarking.SuggestedStartSourceCode);
        Assert.Contains("Bidding", benchmarking.SuggestedStartSourceName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(28, 28, false, "2026-08-28")]
    [InlineData(28, 28, true, "2026-08-28")]
    [InlineData(7, 7, false, "2026-08-07")]
    [InlineData(7, 7, true, "2026-08-07")]
    [InlineData(10, 7, false, null)]
    [InlineData(29, 31, false, null)]
    [InlineData(28, 0, false, "2026-08-28")]
    public async Task GetAsync_ReopenSuggestionIsBoundedForPresentationOnly(
        int predecessorDay, int completedDay, bool workingDays, string? expected)
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project { Id = 1, Name = "Bounded suggestion", CreatedByUserId = "seed", WorkflowVersion = ProcurementWorkflow.VersionV2 });
        var predecessorCompletion = new DateOnly(2026, 8, predecessorDay);
        var historicalCompletion = completedDay == 0 ? null : (DateOnly?)new DateOnly(2026, 8, completedDay);
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.BID, Status = StageStatus.Completed, CompletedOn = predecessorCompletion },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.BM, Status = StageStatus.Completed, CompletedOn = historicalCompletion });
        db.StageDependencyTemplates.Add(new StageDependencyTemplate { Version = ProcurementWorkflow.VersionV2, FromStageCode = StageCodes.BM, DependsOnStageCode = StageCodes.BID });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings { ProjectId = 1, IncludeWeekends = false,
            NextStageStartPolicy = workingDays ? NextStageStartPolicies.NextWorkingDay : NextStageStartPolicies.SameDay });
        await db.SaveChangesAsync();
        var metadata = new WorkflowStageMetadataProvider();
        // UTC is still the previous date: the cap must use operational IST today.
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero));
        var policy = new ProjectStageWorkflowPolicy(db, metadata);
        var item = (await new ProjectTimelineReadService(db, clock, metadata, policy).GetAsync(1))
            .Items.Single(row => row.Code == StageCodes.BM);
        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), item.ReopenSuggestedStartDate);
        Assert.Equal(predecessorCompletion, item.EarliestAllowedStartDate);
        Assert.Equal(predecessorCompletion.AddDays(1), item.SuggestedStartDate);
        Assert.False(db.ChangeTracker.HasChanges());
        var persisted = await db.ProjectStages.AsNoTracking().SingleAsync(row => row.StageCode == StageCodes.BM);
        Assert.Null(persisted.ActualStart); Assert.Equal(historicalCompletion, persisted.CompletedOn);
        if (expected is not null)
        {
            var validation = await new StageValidationService(db, clock, policy).ValidateAsync(1, StageCodes.BM,
                "InProgress", predecessorCompletion, isHoD: false);
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
