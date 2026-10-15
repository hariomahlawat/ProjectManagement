using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Plans;
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
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 7, 15, 0, 0, 0, TimeSpan.Zero));
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
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 8, 25, 0, 0, 0, TimeSpan.Zero));
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
    public async Task ApplyAsync_WhenCompletedOnBeforeStart_RejectsWithoutChangingStage()
    {
        var clock = FakeClock.ForIstDate(2024, 11, 1);
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 10, 10));

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var exception = await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 10, 5),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None));

        Assert.Contains(exception.Details, error => error.Contains("before the selected start date", StringComparison.OrdinalIgnoreCase));
        var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Null(stage.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task ApplyAsync_AllowsAnyHodActor_WhenAnotherHodIsAssignedToProject()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 9, 10, 0, 0, 0, TimeSpan.Zero));
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


    [Fact]
    public async Task ApplyAsync_DirectCompletionWithExplicitBlankStart_PersistsCompletionWithoutBackfill()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2026, 6, 23, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var completionDate = new DateOnly(2026, 6, 23);
        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: completionDate,
            startDate: null,
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.Completed.ToString(), result.UpdatedStatus);
        Assert.Null(result.ActualStart);
        Assert.Equal(completionDate, result.CompletedOn);
        Assert.False(result.RequiresBackfill);

        var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Equal(completionDate, stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task ApplyAsync_ArppManagedIpa_RejectsManualLifecycleChange()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.Completed, new DateOnly(2026, 1, 1));
        var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.IPA);
        stage.CompletedOn = new DateOnly(2026, 2, 26);
        AddPublishedPosition(db, projectId: 1, issueDate: new DateOnly(2026, 2, 26));
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(
            db,
            clock,
            validation,
            stageRules,
            workflowPolicy,
            arppIpaStageAuthority: new ArppIpaStageAuthorityService(db));

        var exception = await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.InProgress.ToString(),
            date: new DateOnly(2026, 7, 1),
            note: "Manual change",
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None));

        Assert.Contains(exception.Details, detail => detail.Contains("controlled by published ARPP", StringComparison.OrdinalIgnoreCase));
        var persisted = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, persisted.Status);
        Assert.Equal(new DateOnly(2026, 2, 26), persisted.CompletedOn);
    }


    [Fact]
    public async Task ApplyAsync_PublishedIpaAuthority_DoesNotEmitStalePredecessorWarning()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2026, 3, 1));
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
                StageCode = StageCodes.FS,
                SortOrder = 0,
                Status = StageStatus.Completed,
                CompletedOn = new DateOnly(2026, 1, 10)
            },
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
                Status = StageStatus.NotStarted
            });

        AddPublishedPosition(db, projectId: 1, issueDate: new DateOnly(2026, 2, 26));
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.SOW,
            status: StageStatus.InProgress.ToString(),
            date: new DateOnly(2026, 2, 27),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.InProgress.ToString(), result.UpdatedStatus);
        Assert.DoesNotContain(result.Warnings, warning =>
            warning.Contains(StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
            && warning.Contains("complete", StringComparison.OrdinalIgnoreCase));

        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.NotStarted, ipa.Status);
        Assert.Null(ipa.CompletedOn);
    }

    [Fact]
    public async Task ApplyAsync_BenchmarkingCompletion_AllowsParallelTechnicalEvaluationChronology()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2026, 8, 28, 6, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = "hod-1",
            CreatedAt = DateTime.UtcNow,
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

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(db, clock, validation, stageRules, workflowPolicy);

        var result = await service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.BM,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2026, 8, 21),
            startDate: new DateOnly(2026, 8, 7),
            note: null,
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(StageStatus.Completed.ToString(), result.UpdatedStatus);
        Assert.Equal(new DateOnly(2026, 8, 7), result.ActualStart);
        Assert.Equal(new DateOnly(2026, 8, 21), result.CompletedOn);

        var benchmarking = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BM);
        Assert.Equal(StageStatus.Completed, benchmarking.Status);
        Assert.Equal(new DateOnly(2026, 8, 7), benchmarking.ActualStart);
        Assert.Equal(new DateOnly(2026, 8, 21), benchmarking.CompletedOn);
    }

    [Fact]
    public async Task ApplyAsync_WhenRealignmentFails_RollsBackStageAndAuditChanges()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.AddRange(
            new ApplicationUser { Id = "creator", UserName = "creator" },
            new ApplicationUser { Id = "hod-1", UserName = "hod-1" });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 10, 9, 30, 0, TimeSpan.Zero));
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 5, 1));
        var target = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        target.PlannedDue = new DateOnly(2024, 5, 5);
        await db.SaveChangesAsync();

        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        var validation = new StageValidationService(db, clock, workflowPolicy);
        var stageRules = new StageRulesService(db, workflowPolicy);
        var service = new StageDirectApplyService(
            db,
            clock,
            validation,
            stageRules,
            workflowPolicy,
            new ThrowingPlanRealignment());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            projectId: 1,
            stageCode: StageCodes.IPA,
            status: StageStatus.Completed.ToString(),
            date: new DateOnly(2024, 5, 10),
            note: "Must roll back",
            hodUserId: "hod-1",
            forceBackfillPredecessors: false,
            CancellationToken.None));

        db.ChangeTracker.Clear();
        var persisted = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.InProgress, persisted.Status);
        Assert.Equal(new DateOnly(2024, 5, 1), persisted.ActualStart);
        Assert.Null(persisted.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Theory]
    [InlineData("Reopen")]
    [InlineData("InProgress")]
    [InlineData("Blocked")]
    [InlineData("NotStarted")]
    public async Task DirectTransition_PreservesStartUnlessExplicitReset_AndClearsCompletion(string status)
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 20, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        var originalStart = new DateOnly(2024, 5, 1);
        await SeedStageAsync(db, StageStatus.Completed, originalStart);
        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        stage.CompletedOn = new DateOnly(2024, 5, 10);
        stage.RequiresBackfill = true;
        await db.SaveChangesAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy),
            new StageRulesService(db, policy), policy);
        var result = await service.ApplyAsync(1, StageCodes.IPA, status,
            status is "Reopen" or "InProgress" ? originalStart : null,
            "Lifecycle correction", "hod-1", false, CancellationToken.None);
        Assert.Null(result.CompletedOn);
        Assert.Equal(status == "NotStarted" ? null : (DateOnly?)originalStart, result.ActualStart);
        Assert.False(result.RequiresBackfill);
        var log = await db.StageChangeLogs.SingleAsync(l => l.Action == "DirectApply");
        Assert.Equal(new DateOnly(2024, 5, 10), log.FromCompletedOn);
        Assert.Null(log.ToCompletedOn);
    }

    [Fact]
    public async Task DirectTransition_RejectsSkipWhenWorkStarted()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 20, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.InProgress, new DateOnly(2024, 5, 1));
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy),
            new StageRulesService(db, policy), policy);
        await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(1, StageCodes.IPA,
            "Skipped", null, "Skip", "hod-1", false, CancellationToken.None));
        await db.Entry(await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA)).ReloadAsync();
        Assert.Equal(StageStatus.InProgress, (await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA)).Status);
    }

    [Fact]
    public async Task DirectCompletion_LegacyCallerDoesNotPersistSuggestedStart()
    {
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 20, 9, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy),
            new StageRulesService(db, policy), policy);
        var result = await service.ApplyAsync(1, StageCodes.IPA, "Completed", new DateOnly(2024, 5, 10),
            "Complete with unknown start", "hod-1", false, CancellationToken.None);
        Assert.Null(result.ActualStart);
        Assert.Equal(new DateOnly(2024, 5, 10), result.CompletedOn);
    }

    [Theory]
    [InlineData("Completed", "Reopen")]
    [InlineData("Completed", "InProgress")]
    [InlineData("Blocked", "Reopen")]
    [InlineData("Blocked", "InProgress")]
    public async Task Reopen_RejectsReplacementOfRecordedStart(string prior, string transition)
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2024, 5, 20));
        await using var db = CreateContext();
        await SeedStageAsync(db, Enum.Parse<StageStatus>(prior), new DateOnly(2024, 5, 10));
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy), new StageRulesService(db, policy), policy);
        await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(1, StageCodes.IPA,
            transition, new DateOnly(2024, 5, 12), "Resume", "hod-1", false, CancellationToken.None));
        Assert.Equal(new DateOnly(2024, 5, 10), (await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA)).ActualStart);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("InProgress")]
    public async Task DirectTransition_RejectsPreservedFutureStart(string target)
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2024, 5, 20));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.Completed, new DateOnly(2024, 5, 21));
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy), new StageRulesService(db, policy), policy);
        await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => service.ApplyAsync(1, StageCodes.IPA,
            target, target == "InProgress" ? new DateOnly(2024, 5, 20) : null, "Transition", "hod-1", false, CancellationToken.None));
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task DirectCompletion_UnpublishedArppDoesNotLockIpa()
    {
        var clock = FakeClock.ForIstDate(new DateOnly(2026, 1, 31));
        await using var db = CreateContext();
        await SeedStageAsync(db, StageStatus.NotStarted);
        db.ArppIssues.Add(new ArppIssue { Id = 101, Name = "Draft", FinancialYearStart = 2025, IssueDate = new DateOnly(2026, 1, 20),
            CreatedByUserId = "seed", UpdatedByUserId = "seed", Entries = { new ArppEntry { ProjectId = 1, ProjectReference = "Project", Cfa = "CFA", Fund = "Fund", DfpdsSchedule = "1", CreatedByUserId = "seed", UpdatedByUserId = "seed" } } });
        await db.SaveChangesAsync();
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var service = new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy), new StageRulesService(db, policy), policy);
        var result = await service.ApplyAsync(1, StageCodes.IPA, "Completed", new DateOnly(2026, 1, 15), "Manual completion", "hod-1", false, CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 1, 15), result.CompletedOn);
        Assert.Null(result.ActualStart);
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

    private static void AddPublishedPosition(ApplicationDbContext db, int projectId, DateOnly issueDate)
    {
        var issue = new ArppIssue
        {
            Id = 100,
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "ARPP/I&R&D/ARTRAC",
            IssueDate = issueDate,
            IsVerified = true,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            VerifiedByUserId = "verifier",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

        issue.PublishedSnapshot = new ArppPublishedIssue
        {
            ArppIssueId = issue.Id,
            RevisionNumber = 1,
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = issue.Name,
            IssueDate = issueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = "arpp/100.pdf",
            AttachmentOriginalFileName = "ARPP-100.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64),
            Entries =
            {
                new ArppPublishedEntry
                {
                    ArppIssueId = issue.Id,
                    SourceEntryId = 1,
                    SortOrder = 1,
                    SerialNumber = "33",
                    ProjectReference = "Project",
                    ProjectId = projectId,
                    Category = ArppCategory.New,
                    IpaCost = 40_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3"
                }
            }
        };

        db.ArppIssues.Add(issue);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
    private sealed class ThrowingPlanRealignment : IPlanRealignment
    {
        public Task CreateRealignmentDraftAsync(
            int projectId,
            string sourceStageCode,
            int delayDays,
            string triggeredByUserId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated realignment failure.");
    }

}
