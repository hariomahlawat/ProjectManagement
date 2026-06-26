using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public class StageDirectApplyServiceTests
{
    [Fact]
    public async Task ApplyAsync_AuthorisedCompletion_AllowsNullDatesCreatesBackfillAndLogsAudit()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 10, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 5, 1));

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: null,
            note: "  Completed through authorised override  ",
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.Completed.ToString(), result.UpdatedStatus);
        Assert.Equal(new DateOnly(2024, 5, 1), result.ActualStart);
        Assert.Null(result.CompletedOn);
        Assert.True(result.RequiresBackfill);
        Assert.Equal(0, result.BackfilledCount);
        Assert.Empty(result.BackfilledStages);
        Assert.Contains(result.Warnings, warning => warning.Contains("authorised override", StringComparison.OrdinalIgnoreCase));

        var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2024, 5, 1), stage.ActualStart);
        Assert.Null(stage.CompletedOn);
        Assert.True(stage.RequiresBackfill);

        var logs = await db.StageChangeLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Contains(logs, l => l.Note != null &&
            l.Note.Contains("Authorised completion without date by HoD; mandatory backfill created.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_WithMissingPredecessorsWithoutForce_ThrowsValidation()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 6, 15), includeCompletedPredecessor: false);

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.FS,
            SortOrder = 0,
            Status = StageStatus.InProgress
        });
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var ex = await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 7, 10),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None));

        Assert.Contains(StageCodes.FS, ex.MissingPredecessors);
        Assert.Contains(ex.Details, d => d.Contains("Complete required predecessor stages first.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_WithForceBackfill_CompletesPredecessorsAndTarget()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 8, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-1",
            CreatedAt = DateTime.UtcNow,
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 1,
                Status = StageStatus.NotStarted
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.SOW,
                SortOrder = 2,
                Status = StageStatus.InProgress,
                ActualStart = new DateOnly(2024, 8, 1)
            });
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.SOW,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 8, 20),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: true,
            CancellationToken.None);

        Assert.Equal(StageStatus.Completed.ToString(), result.UpdatedStatus);
        Assert.False(result.RequiresBackfill);
        Assert.Equal(2, result.BackfilledCount);
        Assert.Contains(StageCodes.FS, result.BackfilledStages);
        Assert.Contains(StageCodes.IPA, result.BackfilledStages);

        var predecessor = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, predecessor.Status);
        Assert.Null(predecessor.ActualStart);
        Assert.Null(predecessor.CompletedOn);
        Assert.True(predecessor.RequiresBackfill);
        Assert.True(predecessor.IsAutoCompleted);
        Assert.Equal(StageCodes.SOW, predecessor.AutoCompletedFromCode);

        var earliestPredecessor = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.Completed, earliestPredecessor.Status);
        Assert.True(earliestPredecessor.RequiresBackfill);
        Assert.True(earliestPredecessor.IsAutoCompleted);
        Assert.Equal(StageCodes.SOW, earliestPredecessor.AutoCompletedFromCode);

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.SOW);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(new DateOnly(2024, 8, 1), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 8, 20), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);

        var logs = await db.StageChangeLogs
            .Where(l => l.StageCode == StageCodes.IPA || l.StageCode == StageCodes.SOW)
            .ToListAsync();

        var autoBackfillLogs = logs.Where(l => l.StageCode == StageCodes.IPA &&
            string.Equals(l.Action, "AutoBackfill", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(autoBackfillLogs);
        Assert.NotNull(autoBackfillLogs[0].Note);
        Assert.Contains("Auto-backfilled (no dates) due to completion of", autoBackfillLogs[0].Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_WhenCompletedOnBeforeStart_ClampsAndWarns()
    {
        var clock = FakeClock.ForIstDate(2024, 11, 1);
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 10, 10));

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 10, 5),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(new DateOnly(2024, 10, 10), result.CompletedOn);
        Assert.False(result.RequiresBackfill);
        Assert.Contains(result.Warnings, w => w.Contains("clamped", StringComparison.OrdinalIgnoreCase));

        var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.IPA);
        Assert.Equal(new DateOnly(2024, 10, 10), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task ApplyAsync_AllowsAnyHodActor_WhenAnotherHodIsAssignedToProject()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var project = await db.Projects.SingleAsync();
        project.HodUserId = "assigned-hod";
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.InProgress.ToString(),
            date: new DateOnly(2024, 9, 5),
            note: null,
            hodUserId: "another-hod",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.InProgress.ToString(), result.UpdatedStatus);
        Assert.False(result.RequiresBackfill);
    }

    [Fact]
    public async Task ApplyAsync_WhenStageRowMissing_CreatesStagesAndUpdates()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-1",
            CreatedAt = DateTime.UtcNow,
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var completionDate = new DateOnly(2024, 11, 20);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.FS,
            status: StageStatus.Completed.ToString(),
            date: completionDate,
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.Completed.ToString(), result.UpdatedStatus);
        Assert.Equal(completionDate, result.CompletedOn);
        Assert.False(result.RequiresBackfill);

        var stages = await db.ProjectStages.OrderBy(s => s.SortOrder).ToListAsync();
        Assert.Equal(StageCodes.All.Length, stages.Count);
        Assert.All(stages.Where(s => !string.Equals(s.StageCode, StageCodes.FS, StringComparison.OrdinalIgnoreCase)),
            s => Assert.Equal(StageStatus.NotStarted, s.Status));

        var stage = stages.Single(s => string.Equals(s.StageCode, StageCodes.FS, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Equal(completionDate, stage.CompletedOn);
    }

    [Fact]
    public async Task ApplyAsync_WhenV2StageRowsAreMissing_MaterialisesV2Order()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "V2 Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-1",
            CreatedAt = DateTime.UtcNow,
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        await service.ApplyAsync(
            1,
            StageCodes.FS,
            StageStatus.Completed.ToString(),
            new DateOnly(2025, 1, 9),
            null,
            "hod-1",
            false,
            CancellationToken.None);

        var sow = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.SOW);
        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.True(sow.SortOrder < ipa.SortOrder);
    }

    private static async Task SeedStageAsync(
        ApplicationDbContext db,
        StageStatus status,
        DateOnly? actualStart = null,
        bool includeCompletedPredecessor = true)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-1",
            CreatedAt = DateTime.UtcNow,
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });

        if (includeCompletedPredecessor)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.FS,
                SortOrder = 0,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2024, 1, 1),
                CompletedOn = new DateOnly(2024, 1, 1)
            });
        }

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = status,
            ActualStart = actualStart
        });

        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
