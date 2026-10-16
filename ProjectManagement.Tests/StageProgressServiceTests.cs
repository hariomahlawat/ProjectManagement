using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class StageProgressServiceTests
{
    [Fact]
    public async Task UpdateStageStatusAsync_UsesConfirmedActualStartWhenMovingToInProgress()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.IPA, StageStatus.NotStarted));

        var service = CreateService(db, clock);

        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.InProgress, new DateOnly(2024, 1, 5), "tester");

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), stage.ActualStart);
        Assert.False(stage.IsAutoCompleted);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_CompletingStageRecordsCompletionWithoutInventingStart()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 16, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.IPA, StageStatus.NotStarted));

        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 123m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        var completedOn = new DateOnly(2024, 2, 15);
        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.Completed, completedOn, "tester");

        var stage = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(completedOn, stage.CompletedOn);
        Assert.Null(stage.ActualStart);
        Assert.False(stage.IsAutoCompleted);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_CompletingStageDoesNotPersistSuggestedPredecessorStart()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.FS, StageStatus.Completed),
            (StageCodes.IPA, StageStatus.InProgress));

        var fs = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS);
        fs.CompletedOn = new DateOnly(2024, 1, 31);
        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        ipa.ActualStart = null;
        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 123m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);
        await service.UpdateStageStatusAsync(
            1,
            StageCodes.IPA,
            StageStatus.Completed,
            new DateOnly(2024, 2, 15),
            "tester");

        await db.Entry(ipa).ReloadAsync();
        Assert.Null(ipa.ActualStart);
        Assert.Equal(new DateOnly(2024, 2, 15), ipa.CompletedOn);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_CompletingBenchmarkingLeavesUnknownStartUnrecorded()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 28, 6, 30, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.BID, StageStatus.Completed),
            (StageCodes.TEC, StageStatus.Completed),
            (StageCodes.BM, StageStatus.InProgress));

        var project = await db.Projects.SingleAsync();
        project.WorkflowVersion = ProcurementWorkflow.VersionV2;

        var bid = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BID);
        bid.ActualStart = new DateOnly(2026, 8, 1);
        bid.CompletedOn = new DateOnly(2026, 8, 6);

        var technicalEvaluation = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.TEC);
        technicalEvaluation.ActualStart = new DateOnly(2026, 8, 7);
        technicalEvaluation.CompletedOn = new DateOnly(2026, 8, 21);

        var benchmarking = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BM);
        benchmarking.ActualStart = null;

        db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = ProcurementWorkflow.VersionV2,
            FromStageCode = StageCodes.BM,
            DependsOnStageCode = StageCodes.BID
        });
        db.ProjectBenchmarkFacts.Add(new ProjectBenchmarkFact
        {
            ProjectId = 1,
            BenchmarkCost = 100m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);
        await service.UpdateStageStatusAsync(
            1,
            StageCodes.BM,
            StageStatus.Completed,
            new DateOnly(2026, 8, 25),
            "tester");

        await db.Entry(benchmarking).ReloadAsync();
        Assert.Null(benchmarking.ActualStart);
        Assert.Equal(new DateOnly(2026, 8, 25), benchmarking.CompletedOn);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_DirectCompletion_PreservesBlankStartWhenRequested()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted));

        var service = CreateService(db, clock);
        var completedOn = new DateOnly(2024, 2, 15);

        await service.UpdateStageStatusAsync(
            1,
            StageCodes.FS,
            StageStatus.Completed,
            completedOn,
            explicitStartDate: null,
            preserveBlankStartOnCompletion: true,
            userId: "tester");

        var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Equal(completedOn, stage.CompletedOn);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_DirectCompletion_UsesEditableExplicitStart()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted));

        var service = CreateService(db, clock);
        var selectedStart = new DateOnly(2024, 2, 5);
        var completedOn = new DateOnly(2024, 2, 15);

        await service.UpdateStageStatusAsync(
            1,
            StageCodes.FS,
            StageStatus.Completed,
            completedOn,
            explicitStartDate: selectedStart,
            preserveBlankStartOnCompletion: true,
            userId: "tester");

        var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == StageCodes.FS);
        Assert.Equal(selectedStart, stage.ActualStart);
        Assert.Equal(completedOn, stage.CompletedOn);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_DirectCompletion_RejectsCompletionBeforeEditableStart()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 2, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted));

        var service = CreateService(db, clock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStageStatusAsync(
                1,
                StageCodes.FS,
                StageStatus.Completed,
                new DateOnly(2024, 2, 10),
                explicitStartDate: new DateOnly(2024, 2, 11),
                preserveBlankStartOnCompletion: true,
                userId: "tester"));

        Assert.Contains("selected start date", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_CompletingStageCascadesPredecessors()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 3, 6, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted),
            (StageCodes.SOW, StageStatus.NotStarted),
            (StageCodes.AON, StageStatus.NotStarted));

        db.ProjectAonFacts.Add(new ProjectAonFact
        {
            ProjectId = 1,
            AonCost = 500m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        var completedOn = new DateOnly(2024, 3, 5);
        await service.UpdateStageStatusAsync(1, StageCodes.AON, StageStatus.Completed, completedOn, "tester");

        var aon = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.AON);
        var sow = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.SOW);
        var ipa = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        var fs = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);

        Assert.False(aon.IsAutoCompleted);
        Assert.Equal(StageStatus.Completed, sow.Status);
        Assert.True(sow.IsAutoCompleted);
        Assert.Equal(StageCodes.AON, sow.AutoCompletedFromCode);
        Assert.True(sow.RequiresBackfill); // no SOW facts captured
        Assert.Null(sow.ActualStart);
        Assert.Null(sow.CompletedOn);

        Assert.Equal(StageStatus.Completed, ipa.Status);
        Assert.True(ipa.IsAutoCompleted);
        Assert.Equal(StageCodes.AON, ipa.AutoCompletedFromCode);
        Assert.True(ipa.RequiresBackfill);
        Assert.Null(ipa.ActualStart);
        Assert.Null(ipa.CompletedOn);

        Assert.Equal(StageStatus.Completed, fs.Status);
        Assert.True(fs.IsAutoCompleted);
        Assert.Equal(StageCodes.AON, fs.AutoCompletedFromCode);
        Assert.True(fs.RequiresBackfill);
        Assert.Null(fs.ActualStart);
        Assert.Null(fs.CompletedOn);
    }

    [Fact]
    public async Task UpdateStageStatusAsync_ThrowsWhenFactsMissing()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.IPA, StageStatus.NotStarted));

        var service = CreateService(db, clock);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.Completed, null, "tester"));
    }

    [Fact]
    public async Task UpdateStageStatusAsync_ResetClearsDatesAndFlags()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted),
            (StageCodes.SOW, StageStatus.NotStarted),
            (StageCodes.AON, StageStatus.NotStarted));

        db.ProjectAonFacts.Add(new ProjectAonFact
        {
            ProjectId = 1,
            AonCost = 200m,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        await service.UpdateStageStatusAsync(1, StageCodes.AON, StageStatus.Completed, null, "tester");

        await service.UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.NotStarted, null, "tester");

        var ipa = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.NotStarted, ipa.Status);
        Assert.Null(ipa.ActualStart);
        Assert.Null(ipa.CompletedOn);
        Assert.False(ipa.IsAutoCompleted);
        Assert.False(ipa.RequiresBackfill);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateStageStatusAsync_OnlySkipsOptionalPncWhenInEffectiveDependencyClosure(bool applicable)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 16, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(
            db,
            (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted),
            (StageCodes.SOW, StageStatus.NotStarted),
            (StageCodes.AON, StageStatus.NotStarted),
            (StageCodes.BM, StageStatus.NotStarted),
            (StageCodes.COB, StageStatus.NotStarted),
            (StageCodes.PNC, StageStatus.NotStarted),
            (StageCodes.EAS, StageStatus.NotStarted),
            (StageCodes.SO, StageStatus.NotStarted));

        (await db.Projects.SingleAsync()).PncApplicable = applicable;

        db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV2,
            Code = StageCodes.PNC,
            Name = "Price Negotiation Committee",
            Sequence = 80,
            Optional = true
        });

        db.ProjectSupplyOrderFacts.Add(new ProjectSupplyOrderFact
        {
            ProjectId = 1,
            CreatedByUserId = "seed",
            CreatedOnUtc = clock.UtcNow.UtcDateTime
        });

        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        var completedOn = new DateOnly(2024, 6, 15);
        await service.UpdateStageStatusAsync(1, StageCodes.SO, StageStatus.Completed, completedOn, "tester");

        var pnc = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(applicable ? StageStatus.Skipped : StageStatus.NotStarted, pnc.Status);
        Assert.False(pnc.IsAutoCompleted);

        var cob = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.COB);
        Assert.Equal(StageStatus.Completed, cob.Status);
        Assert.True(cob.IsAutoCompleted);
        Assert.Equal(StageCodes.SO, cob.AutoCompletedFromCode);
        Assert.Null(cob.ActualStart);
        Assert.Null(cob.CompletedOn);
        Assert.True(cob.RequiresBackfill);
    }

    [Theory]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Blocked)]
    [InlineData(StageStatus.NotStarted)]
    public async Task LifecycleTransition_ClearsCompletionAndKeepsOnlyValidActuals(StageStatus target)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.Completed));
        var stage = await db.ProjectStages.SingleAsync();
        var originalStart = new DateOnly(2024, 6, 1);
        stage.ActualStart = originalStart;
        stage.CompletedOn = new DateOnly(2024, 6, 10);
        stage.RequiresBackfill = true;
        stage.IsAutoCompleted = true;
        stage.AutoCompletedFromCode = StageCodes.IPA;
        await db.SaveChangesAsync();

        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.FS, target,
            new DateOnly(2024, 6, 15), "tester");

        await db.Entry(stage).ReloadAsync();
        Assert.Null(stage.CompletedOn);
        Assert.Equal(target == StageStatus.NotStarted ? null : (DateOnly?)originalStart, stage.ActualStart);
        Assert.False(stage.RequiresBackfill);
        Assert.False(stage.IsAutoCompleted);
        Assert.Null(stage.AutoCompletedFromCode);
        var log = await db.StageChangeLogs.SingleAsync(log => log.Action == "Applied");
        Assert.Equal(originalStart, log.FromActualStart);
        Assert.Equal(new DateOnly(2024, 6, 10), log.FromCompletedOn);
        Assert.Null(log.ToCompletedOn);
    }

    [Fact]
    public async Task LifecycleTransition_RequiresConfirmedStartInsteadOfTodayOrPlan()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.Completed));
        var stage = await db.ProjectStages.SingleAsync();
        stage.CompletedOn = new DateOnly(2024, 6, 10);
        stage.PlannedStart = new DateOnly(2024, 6, 1);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, clock)
            .UpdateStageStatusAsync(1, StageCodes.FS, StageStatus.InProgress, null, "tester"));
        await db.Entry(stage).ReloadAsync();
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Null(stage.ActualStart);
    }

    [Theory]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Completed)]
    public async Task LifecycleTransition_RejectsSkippingRecordedExecution(StageStatus status)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, status));
        var stage = await db.ProjectStages.SingleAsync();
        stage.ActualStart = new DateOnly(2024, 6, 1);
        stage.CompletedOn = status == StageStatus.Completed ? new DateOnly(2024, 6, 10) : null;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, clock)
            .UpdateStageStatusAsync(1, StageCodes.FS, StageStatus.Skipped, null, "tester"));
        await db.Entry(stage).ReloadAsync();
        Assert.Equal(status, stage.Status);
        Assert.Equal(new DateOnly(2024, 6, 1), stage.ActualStart);
    }

    [Fact]
    public async Task Completion_DoesNotInventStartOrAutomaticallyStartSuccessor()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted), (StageCodes.IPA, StageStatus.NotStarted));
        db.ProjectScheduleSettings.Add(new ProjectManagement.Models.Scheduling.ProjectScheduleSettings
        { ProjectId = 1, AnchorStart = new DateOnly(2024, 6, 1) });
        await db.SaveChangesAsync();
        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.FS, StageStatus.Completed,
            new DateOnly(2024, 6, 20), "tester");
        var fs = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        var ipa = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Null(fs.ActualStart);
        Assert.Equal(StageStatus.NotStarted, ipa.Status);
        Assert.Null(ipa.ActualStart);
        Assert.Null(ipa.CompletedOn);
    }

    [Theory]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Completed)]
    public async Task LifecycleTransition_RejectsFutureDatesInIst(StageStatus target)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 19, 20, 0, 0, TimeSpan.Zero)); // IST 20 Jun
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, clock)
            .UpdateStageStatusAsync(1, StageCodes.FS, target, new DateOnly(2024, 6, 21), "tester"));
        Assert.Equal(StageStatus.NotStarted, (await db.ProjectStages.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cascade_PreservesKnownStartsAndLeavesUndatedPredecessorsInBackfill()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.InProgress), (StageCodes.IPA, StageStatus.NotStarted));
        var fs = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        fs.ActualStart = new DateOnly(2024, 6, 1);
        db.ProjectIpaFacts.Add(new ProjectIpaFact { ProjectId = 1, IpaCost = 10, CreatedByUserId = "seed", CreatedOnUtc = clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.Completed,
            new DateOnly(2024, 6, 20), "tester");
        Assert.Equal(new DateOnly(2024, 6, 1), fs.ActualStart);
        Assert.Null(fs.CompletedOn);
        Assert.True(fs.RequiresBackfill);
        Assert.Equal(StageStatus.Completed, fs.Status);
    }

    [Theory]
    [InlineData(StageStatus.Skipped)]
    [InlineData(StageStatus.Blocked)]
    public async Task LifecycleTransition_NeverStartedStageCanRemainWithoutActualDates(StageStatus target)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted));

        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.FS, target, null, "tester");

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(target, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Null(stage.CompletedOn);
        Assert.False(stage.IsAutoCompleted);
        Assert.Null(stage.AutoCompletedFromCode);
        Assert.False(stage.RequiresBackfill);
        var history = await db.StageChangeLogs.SingleAsync(log => log.Action == "Applied");
        Assert.Equal(StageStatus.NotStarted.ToString(), history.FromStatus);
        Assert.Equal(target.ToString(), history.ToStatus);
        Assert.Null(history.ToActualStart);
        Assert.Null(history.ToCompletedOn);
    }

    [Theory]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Completed)]
    public async Task LifecycleTransition_AllowsCurrentIstDateEvenWhenUtcIsPreviousDay(StageStatus target)
    {
        // UTC is 19 June; PRISM's operational date has already reached 20 June.
        var clock = new TestClock(new DateTimeOffset(2024, 6, 19, 20, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted));
        var operationalDate = new DateOnly(2024, 6, 20);

        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.FS, target, operationalDate, "tester");

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(target, stage.Status);
        Assert.Equal(target == StageStatus.InProgress ? (DateOnly?)operationalDate : null, stage.ActualStart);
        Assert.Equal(target == StageStatus.Completed ? (DateOnly?)operationalDate : null, stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }
    [Fact]
    public async Task Cascade_DoesNotEraseActualEvidenceOnLegacyOptionalNotStartedStage()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted), (StageCodes.IPA, StageStatus.NotStarted));
        var fs = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        fs.ActualStart = new DateOnly(2024, 6, 1);
        db.StageTemplates.Add(new StageTemplate { Version = ProcurementWorkflow.VersionV1, Code = StageCodes.FS, Name = "FS", Optional = true, Sequence = 1 });
        db.ProjectIpaFacts.Add(new ProjectIpaFact { ProjectId = 1, IpaCost = 10, CreatedByUserId = "seed", CreatedOnUtc = clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, clock)
            .UpdateStageStatusAsync(1, StageCodes.IPA, StageStatus.Completed, new DateOnly(2024, 6, 20), "tester"));
        db.ChangeTracker.Clear();
        var original = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.NotStarted, original.Status);
        Assert.Equal(new DateOnly(2024, 6, 1), original.ActualStart);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cascade_TraversesPublishedIpaWithoutChangingItsStoredFacts(bool knownStart)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 20, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.NotStarted),
            (StageCodes.IPA, StageStatus.NotStarted), (StageCodes.SOW, StageStatus.NotStarted));
        var ipa = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        // Deliberately stale: authority must not trigger an incidental repair during cascade.
        ipa.ActualStart = knownStart ? new DateOnly(2024, 6, 1) : null;
        ipa.CompletedOn = new DateOnly(2024, 6, 5);
        ipa.RequiresBackfill = true;
        db.ProjectSowFacts.Add(new ProjectSowFact { ProjectId = 1, CreatedByUserId = "seed" });
        AddPublishedIpa(db, new DateOnly(2024, 6, 10));
        await db.SaveChangesAsync();

        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.SOW, StageStatus.Completed,
            new DateOnly(2024, 6, 20), "tester");

        db.ChangeTracker.Clear();
        var upstream = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.Completed, upstream.Status);
        Assert.True(upstream.IsAutoCompleted);
        Assert.True(upstream.RequiresBackfill);
        Assert.Equal(StageCodes.SOW, upstream.AutoCompletedFromCode);
        Assert.Null(upstream.ActualStart);
        Assert.Null(upstream.CompletedOn);
        var unchanged = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.NotStarted, unchanged.Status);
        Assert.Equal(knownStart ? new DateOnly(2024, 6, 1) : (DateOnly?)null, unchanged.ActualStart);
        Assert.Equal(new DateOnly(2024, 6, 5), unchanged.CompletedOn);
        Assert.True(unchanged.RequiresBackfill);
        Assert.False(unchanged.IsAutoCompleted);
        Assert.Null(unchanged.AutoCompletedFromCode);
        Assert.Empty(await db.StageChangeLogs.Where(log => log.StageCode == StageCodes.IPA).ToListAsync());
        Assert.Equal(new DateOnly(2024, 6, 10), (await db.ArppPublishedIssues.SingleAsync()).IssueDate);
    }

    [Theory]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Blocked)]
    public async Task LifecycleTransition_RejectsPreservedFutureStartWithoutRewritingLegacyState(StageStatus target)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 19, 20, 0, 0, TimeSpan.Zero)); // IST 20 June
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.Completed));
        var stage = await db.ProjectStages.SingleAsync();
        stage.ActualStart = new DateOnly(2024, 6, 21);
        stage.CompletedOn = new DateOnly(2024, 6, 22);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db, clock)
            .UpdateStageStatusAsync(1, StageCodes.FS, target, null, "tester"));

        Assert.Contains("future", exception.Message, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();
        var original = await db.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, original.Status);
        Assert.Equal(new DateOnly(2024, 6, 21), original.ActualStart);
        Assert.Equal(new DateOnly(2024, 6, 22), original.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Theory]
    [InlineData(StageStatus.InProgress)]
    [InlineData(StageStatus.Blocked)]
    public async Task LifecycleTransition_PreservedStartOnCurrentIstDateRemainsValid(StageStatus target)
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 19, 20, 0, 0, TimeSpan.Zero)); // UTC 19 June
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.Completed));
        var stage = await db.ProjectStages.SingleAsync();
        stage.ActualStart = new DateOnly(2024, 6, 20);
        stage.CompletedOn = new DateOnly(2024, 6, 20);
        await db.SaveChangesAsync();

        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.FS, target, null, "tester");

        await db.Entry(stage).ReloadAsync();
        Assert.Equal(target, stage.Status);
        Assert.Equal(new DateOnly(2024, 6, 20), stage.ActualStart);
        Assert.Null(stage.CompletedOn);
    }

    [Fact]
    public async Task ResetToNotStarted_CanExplicitlyClearLegacyFutureDates()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 19, 20, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.FS, StageStatus.Completed));
        var stage = await db.ProjectStages.SingleAsync();
        stage.ActualStart = new DateOnly(2024, 6, 21);
        stage.CompletedOn = new DateOnly(2024, 6, 22);
        await db.SaveChangesAsync();

        await CreateService(db, clock).UpdateStageStatusAsync(1, StageCodes.FS, StageStatus.NotStarted, null, "tester");

        await db.Entry(stage).ReloadAsync();
        Assert.Equal(StageStatus.NotStarted, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Null(stage.CompletedOn);
        var history = await db.StageChangeLogs.SingleAsync();
        Assert.Equal(new DateOnly(2024, 6, 21), history.FromActualStart);
        Assert.Equal(new DateOnly(2024, 6, 22), history.FromCompletedOn);
        Assert.Null(history.ToActualStart);
        Assert.Null(history.ToCompletedOn);
    }

    [Theory]
    [InlineData(StageStatus.Completed, StageStatus.Completed)]
    [InlineData(StageStatus.Completed, StageStatus.Skipped)]
    [InlineData(StageStatus.Skipped, StageStatus.Completed)]
    [InlineData(StageStatus.Skipped, StageStatus.Skipped)]
    public async Task CompletingCob_PreservesSatisfiedSkippedPredecessors(StageStatus tec, StageStatus bm)
    {
        await using var db = CreateContext();
        await SeedStagesAsync(db, (StageCodes.TEC, tec), (StageCodes.BM, bm), (StageCodes.COB, StageStatus.InProgress));
        db.ProjectCommercialFacts.Add(new ProjectCommercialFact { ProjectId = 1, CreatedByUserId = "seed" });
        var cob = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.COB);
        cob.ActualStart = new DateOnly(2024, 6, 10);
        await db.SaveChangesAsync();
        await CreateService(db, new TestClock(new DateTimeOffset(2024, 6, 16, 0, 0, 0, TimeSpan.Zero)))
            .UpdateStageStatusAsync(1, StageCodes.COB, StageStatus.Completed, new DateOnly(2024, 6, 15), "tester");
        db.ChangeTracker.Clear();
        Assert.Equal(tec, (await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.TEC)).Status);
        Assert.Equal(bm, (await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.BM)).Status);
        Assert.Equal(StageStatus.Completed, (await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.COB)).Status);
    }

    private static void AddPublishedIpa(ApplicationDbContext db, DateOnly completion)
    {
        db.ArppIssues.Add(new ArppIssue
        {
            Id = 1, FinancialYearStart = 2024, Kind = ArppIssueKind.Original, IssueSequence = 0,
            Name = "Original ARPP", IssueDate = completion, IsVerified = true,
            CreatedByUserId = "seed", UpdatedByUserId = "seed",
            PublishedSnapshot = new ArppPublishedIssue
            {
                ArppIssueId = 1, RevisionNumber = 1, FinancialYearStart = 2024,
                Kind = ArppIssueKind.Original, IssueSequence = 0, Name = "Original ARPP", IssueDate = completion,
                PublishedByUserId = "seed", AttachmentStorageKey = "test.pdf", AttachmentOriginalFileName = "test.pdf",
                AttachmentContentType = "application/pdf", AttachmentSha256 = new string('a', 64),
                Entries = { new ArppPublishedEntry { ArppIssueId = 1, SourceEntryId = 1, ProjectId = 1,
                    Category = ArppCategory.New, ProjectReference = "Project", Cfa = "CFA", Fund = "Fund", DfpdsSchedule = "Schedule" } }
            }
        });
    }
    private static StageProgressService CreateService(ApplicationDbContext db, TestClock clock)
    {
        var workflowPolicy = StageWorkflowTestFactory.CreatePolicy(db);
        return new StageProgressService(
            db,
            clock,
            new FakeAudit(),
            new ProjectFactsReadService(db),
            new NullStageNotificationService(),
            workflowPolicy);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedStagesAsync(ApplicationDbContext db, params (string Code, StageStatus Status)[] stages)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });

        foreach (var (code, status) in stages)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1,
                StageCode = code,
                SortOrder = Array.IndexOf(StageCodes.All, code),
                Status = status
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            Microsoft.AspNetCore.Http.HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class NullStageNotificationService : IStageNotificationService
    {
        public Task NotifyStageStatusChangedAsync(ProjectStage stage, Project project, StageStatus previousStatus, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
