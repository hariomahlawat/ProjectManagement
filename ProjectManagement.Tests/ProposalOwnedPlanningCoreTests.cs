using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Scheduling;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProposalOwnedPlanningCoreTests
{
    [Fact]
    public async Task NewDraft_SnapshotsPublishedInputsAndExtensionsInsteadOfAnotherActorsProposal()
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "other", OwnerUserId = "other", AnchorDate = new DateOnly(2030, 1, 1), PncApplicable = true });
        await db.SaveChangesAsync();
        var draft = await Service(db, "actor").CreateOrGetDraftAsync(1);
        Assert.Equal(new DateOnly(2026, 11, 1), draft.AnchorDate);
        Assert.False(draft.SkipWeekends);
        Assert.False(draft.SkipHolidays);
        Assert.Equal(PlanTransitionRule.SameDay, draft.TransitionRule);
        Assert.False(draft.PncApplicable);
        var fs = Assert.Single(draft.StagePlans, s => s.StageCode == StageCodes.FS);
        Assert.Equal(5, fs.DurationDays);
        Assert.Equal(new DateOnly(2026, 11, 2), fs.PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 6), fs.PlannedDue);
        var extension = Assert.Single(draft.StagePlans, s => s.StageCode == "EXT");
        Assert.Equal(13, extension.SortOrder);
        Assert.Equal(4, extension.DurationDays);
        Assert.Single(await db.ProjectScheduleSettings.ToListAsync());
        Assert.Equal(2, await db.ProjectPlanDurations.CountAsync());
        Assert.Equal(2, await db.ProjectStages.CountAsync());
        Assert.Equal(new DateOnly(2026, 11, 1), (await db.ProjectScheduleSettings.SingleAsync()).AnchorStart);
    }

    [Fact]
    public async Task PublishedBaseline_IsCoherentAcrossPublication_AndLaterDraftUsesCommittedRevision()
    {
        await using var connection = new SqliteConnection($"Data Source=proposal-baseline-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False");
        await connection.OpenAsync();
        var observer = new PublishAfterBaselineRead();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(observer).Options);
        await db.Database.EnsureCreatedAsync();
        await SeedPublishedAsync(db);
        db.ChangeTracker.Clear();
        observer.PublishAsync = async () =>
        {
            await using var publisherConnection = new SqliteConnection(connection.ConnectionString);
            await publisherConnection.OpenAsync();
            await using var publisher = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(publisherConnection).Options);
            await using var transaction = await publisher.Database.BeginTransactionAsync();
            await publisher.Database.ExecuteSqlRawAsync("""
                UPDATE "Projects" SET "PncApplicable" = TRUE WHERE "Id" = 1;
                UPDATE "ProjectScheduleSettings" SET "AnchorStart" = '2026-11-10', "IncludeWeekends" = FALSE,
                    "SkipHolidays" = TRUE, "NextStageStartPolicy" = 'NextWorkingDay' WHERE "ProjectId" = 1;
                UPDATE "ProjectPlanDurations" SET "DurationDays" = 12, "SortOrder" = 20 WHERE "ProjectId" = 1 AND "StageCode" = 'FS';
                UPDATE "ProjectStages" SET "PlannedStart" = '2026-11-10', "PlannedDue" = '2026-11-21' WHERE "ProjectId" = 1 AND "StageCode" = 'FS';
                """);
            await transaction.CommitAsync();
        };

        // The source snapshot is one SQL statement: a later publication must not
        // retroactively change any of the baseline's captured inputs.
        var old = await PublishedPlanningBaseline.LoadAsync(db, 1);
        Assert.False(old.PncApplicable);
        Assert.Equal(new DateOnly(2026, 11, 1), old.Settings?.AnchorStart);
        Assert.Equal(5, old.Durations.Single(row => row.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(new DateOnly(2026, 11, 2),
            old.Stages.Single(row => row.StageCode == StageCodes.FS).PlannedStart);

        // The next read triggers an independent full publication after the
        // snapshot's reader has been materialized. It must not occur mid-snapshot.
        await db.ProjectStages.AsNoTracking().ToListAsync();
        Assert.True(observer.Published);
        Assert.True((await db.Projects.AsNoTracking().SingleAsync()).PncApplicable);

        // Draft allocation now loads accepted planning inputs *inside* its
        // project gate, so a later draft reflects the newer committed baseline.
        var draft = await Service(db, "actor").CreateOrGetDraftAsync(1);
        Assert.True(draft.PncApplicable);
        Assert.Equal(new DateOnly(2026, 11, 10), draft.AnchorDate);
        Assert.True(draft.SkipWeekends);
        Assert.True(draft.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, draft.TransitionRule);
        var fs = Assert.Single(draft.StagePlans, row => row.StageCode == StageCodes.FS);
        Assert.Equal(12, fs.DurationDays);
        // Configured workflow ordering takes precedence over compatibility-row order.
        Assert.Equal(10, fs.SortOrder);
        Assert.Equal(new DateOnly(2026, 11, 10), fs.PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 21), fs.PlannedDue);
        Assert.Equal(new DateOnly(2026, 11, 10),
            (await db.ProjectScheduleSettings.AsNoTracking().SingleAsync()).AnchorStart);
    }

    [Fact]
    public async Task NewDraft_LegacyProjectUsesSafeDefaultsWithoutCreatingGlobalRows()
    {
        await using var db = CreateDb();
        db.Projects.Add(new Project { Id = 1, Name = "Legacy", PncApplicable = false, WorkflowVersion = PlanConstants.StageTemplateVersionV1 });
        await db.SaveChangesAsync();
        var draft = await Service(db, "actor").CreateOrGetDraftAsync(1);
        Assert.Null(draft.AnchorDate);
        Assert.True(draft.SkipHolidays);
        Assert.True(draft.SkipWeekends);
        Assert.False(draft.PncApplicable);
        Assert.Contains(draft.StagePlans, s => s.StageCode == StageCodes.FS);
        Assert.All(draft.StagePlans, s => Assert.Equal(0, s.DurationDays));
        Assert.Empty(await db.ProjectScheduleSettings.ToListAsync());
        Assert.Empty(await db.ProjectPlanDurations.ToListAsync());
        Assert.Empty(await db.ProjectStages.ToListAsync());
    }

    [Fact]
    public async Task NewDraft_DoesNotAdoptAnotherActorsOwnerlessProposal()
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        var foreign = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "other", OwnerUserId = null, AnchorDate = new DateOnly(2030, 1, 1) };
        db.PlanVersions.Add(foreign);
        await db.SaveChangesAsync();
        var draft = await Service(db, "actor").CreateOrGetDraftAsync(1);
        Assert.NotEqual(foreign.Id, draft.Id);
        Assert.Null(foreign.OwnerUserId);
        Assert.Equal(new DateOnly(2026, 11, 1), draft.AnchorDate);
    }

    [Fact]
    public async Task PendingOwner_MayClaimOwnOrphanButMustKeepSubmittedRevisionIntact()
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        db.PlanVersions.AddRange(
            new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "actor", OwnerUserId = "actor", Status = PlanVersionStatus.PendingApproval },
            new PlanVersion { ProjectId = 1, VersionNo = 2, CreatedByUserId = "actor" });
        await db.SaveChangesAsync();
        var reclaimed = await Service(db, "actor").CreateOrGetDraftAsync(1);
        Assert.Equal(2, reclaimed.VersionNo);
        Assert.Equal("actor", reclaimed.OwnerUserId);
        Assert.Equal(PlanVersionStatus.PendingApproval,
            (await db.PlanVersions.SingleAsync(p => p.VersionNo == 1)).Status);
    }

    [Fact]
    public async Task Discard_SelectsActorsDraftDespiteOlderApprovedHistoryAndPreservesOtherState()
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        var history = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "actor", OwnerUserId = "actor", Status = PlanVersionStatus.Approved };
        var draft = new PlanVersion { ProjectId = 1, VersionNo = 2, CreatedByUserId = "actor", OwnerUserId = "actor" };
        draft.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, DurationDays = 12 });
        var other = new PlanVersion { ProjectId = 1, VersionNo = 3, CreatedByUserId = "other", OwnerUserId = "other" };
        db.PlanVersions.AddRange(history, draft, other);
        await db.SaveChangesAsync();
        Assert.Equal(PlanDraftDeleteResult.Success, await Service(db, "actor").DeleteDraftAsync(1));
        Assert.False(await db.PlanVersions.AnyAsync(p => p.Id == draft.Id));
        Assert.False(await db.StagePlans.AnyAsync(s => s.PlanVersionId == draft.Id));
        Assert.True(await db.PlanVersions.AnyAsync(p => p.Id == history.Id));
        Assert.True(await db.PlanVersions.AnyAsync(p => p.Id == other.Id));
        Assert.False((await db.Projects.SingleAsync()).PncApplicable);
        Assert.Equal(5, (await db.ProjectPlanDurations.SingleAsync(s => s.StageCode == StageCodes.FS)).DurationDays);
        var reloaded = await Service(db, "actor").CreateOrGetDraftAsync(1);
        Assert.Equal(new DateOnly(2026, 11, 1), reloaded.AnchorDate);
        Assert.Equal(5, reloaded.StagePlans.Single(s => s.StageCode == StageCodes.FS).DurationDays);
    }

    [Theory]
    [InlineData(false, 16)]
    [InlineData(true, 17)]
    public async Task GenerateDraft_UsesOnlyRevisionInputsAcrossGlobalChanges(bool skipHolidays, int dueDay)
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        db.Holidays.Add(new Holiday { Date = new DateOnly(2026, 11, 11), Name = "Holiday", Type = HolidayType.Gazetted });
        var draft = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "actor", OwnerUserId = "actor", AnchorDate = new DateOnly(2026, 11, 10), SkipWeekends = false, SkipHolidays = skipHolidays, TransitionRule = PlanTransitionRule.NextWorkingDay };
        draft.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, DurationDays = 7 });
        db.PlanVersions.Add(draft);
        await db.SaveChangesAsync();
        var generator = new PlanGenerationService(db);
        await generator.GenerateDraftAsync(1, draft.Id);
        var fs = draft.StagePlans.Single(s => s.StageCode == StageCodes.FS);
        Assert.Equal(new DateOnly(2026, 11, 10), fs.PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, dueDay), fs.PlannedDue);
        var settings = await db.ProjectScheduleSettings.SingleAsync();
        settings.AnchorStart = new DateOnly(2030, 1, 1);
        settings.SkipHolidays = !skipHolidays;
        (await db.ProjectPlanDurations.SingleAsync(s => s.StageCode == StageCodes.FS)).DurationDays = 99;
        await db.SaveChangesAsync();
        await generator.GenerateDraftAsync(1, draft.Id);
        Assert.Equal(new DateOnly(2026, 11, 10), fs.PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, dueDay), fs.PlannedDue);
        Assert.Equal(7, fs.DurationDays);
        Assert.Equal(skipHolidays, draft.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, draft.TransitionRule);
    }

    [Fact]
    public async Task GenerateDraft_OrdersExtensionsByProposalOrderWithoutAnyGlobalInputs()
    {
        await using var db = CreateDb();
        db.Projects.Add(new Project { Id = 1, Name = "Extensions", WorkflowVersion = PlanConstants.StageTemplateVersionV1 });
        var draft = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "actor", AnchorDate = new DateOnly(2026, 11, 10), SkipWeekends = false, SkipHolidays = false, TransitionRule = PlanTransitionRule.NextWorkingDay };
        draft.StagePlans.AddRange(new[] {
            new StagePlan { StageCode = "ALPHA", DurationDays = 1, SortOrder = 40 },
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 1, SortOrder = 999 },
            new StagePlan { StageCode = "ZULU", DurationDays = 1, SortOrder = 20 } });
        db.PlanVersions.Add(draft);
        await db.SaveChangesAsync();
        await new PlanGenerationService(db).GenerateDraftAsync(1, draft.Id);
        Assert.Equal(new DateOnly(2026, 11, 10), draft.StagePlans.Single(s => s.StageCode == StageCodes.FS).PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 11), draft.StagePlans.Single(s => s.StageCode == "ZULU").PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 12), draft.StagePlans.Single(s => s.StageCode == "ALPHA").PlannedStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cutover_UntrustedGlobalsNeverInitializeAnotherActorsDraft(bool oldDraftDiscarded)
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        (await db.Projects.SingleAsync()).PublishedPlanningInputVersion = PlanningInputVersions.Legacy;
        (await db.ProjectScheduleSettings.SingleAsync()).AnchorStart = new DateOnly(2035, 1, 1);
        (await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.FS)).DurationDays = 99;
        db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = "PRIVATE", DurationDays = 45, SortOrder = 999 });
        if (!oldDraftDiscarded)
            db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 1, OwnerUserId = "other", CreatedByUserId = "other" });
        await db.SaveChangesAsync();

        var draft = await Service(db, "actor").CreateOrGetDraftAsync(1);

        Assert.Null(draft.AnchorDate);
        Assert.True(draft.SkipWeekends);
        Assert.True(draft.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, draft.TransitionRule);
        Assert.All(draft.StagePlans, stage => Assert.Equal(0, stage.DurationDays));
        Assert.DoesNotContain(draft.StagePlans, stage => stage.StageCode == "PRIVATE");
        Assert.Equal(new DateOnly(2026, 11, 2), draft.StagePlans.Single(stage => stage.StageCode == StageCodes.FS).PlannedStart);
        Assert.Equal(PlanningInputVersions.Current, draft.PlanningInputVersion);
        Assert.Equal(new DateOnly(2035, 1, 1), (await db.ProjectScheduleSettings.SingleAsync()).AnchorStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cutover_NewAndResumedDraftsNormalizeFalsePncWithoutChangingOfficialHistory(bool resumeLegacy)
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 15, SortOrder = 777 });
        db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC, PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 15) });
        if (resumeLegacy)
        {
            var legacy = new PlanVersion { ProjectId = 1, VersionNo = 1, OwnerUserId = "actor", CreatedByUserId = "actor", AnchorDate = new DateOnly(2024, 4, 4), PncApplicable = true };
            legacy.PlanningInputVersion = PlanningInputVersions.Legacy;
            legacy.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, DurationDays = 88, PlannedStart = new DateOnly(2026, 12, 1), PlannedDue = new DateOnly(2026, 12, 2) });
            legacy.StagePlans.Add(new StagePlan { StageCode = StageCodes.PNC, DurationDays = 99, PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 15) });
            db.PlanVersions.Add(legacy);
        }
        await db.SaveChangesAsync();

        var draft = await Service(db, "actor").CreateOrGetDraftAsync(1);

        if (resumeLegacy)
            await UpgradeAsync(Service(db, "actor"), draft, exact: true);

        Assert.False(draft.PncApplicable);
        var pnc = draft.StagePlans.Single(stage => stage.StageCode == StageCodes.PNC);
        Assert.Equal(0, pnc.DurationDays);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.Equal(new DateOnly(2024, 1, 1), (await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.PNC)).PlannedStart);
        Assert.Equal(15, (await db.ProjectPlanDurations.SingleAsync(stage => stage.StageCode == StageCodes.PNC)).DurationDays);
        Assert.Equal(new DateOnly(2026, 11, 1), draft.AnchorDate);
        Assert.False(draft.SkipWeekends);
        Assert.Equal(resumeLegacy ? 2 : 5, draft.StagePlans.Single(stage => stage.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(resumeLegacy ? new DateOnly(2026, 12, 1) : new DateOnly(2026, 11, 2), draft.StagePlans.Single(stage => stage.StageCode == StageCodes.FS).PlannedStart);
        Assert.Equal(PlanningInputVersions.Current, draft.PlanningInputVersion);
    }

    [Fact]
    public async Task NewDraft_KnownCanonicalOrderWinsStaleCompatibilityOrder()
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        (await db.ProjectPlanDurations.SingleAsync(stage => stage.StageCode == StageCodes.FS)).SortOrder = 999;
        (await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.FS)).SortOrder = 888;
        await db.SaveChangesAsync();
        var draft = await Service(db, "actor").CreateOrGetDraftAsync(1);
        Assert.Equal(10, draft.StagePlans.Single(stage => stage.StageCode == StageCodes.FS).SortOrder);
    }

    [Fact]
    public async Task LegacyDraft_InspectionDoesNotUpgradeAndResumeCannotMutateAnotherActor()
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        var mine = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "actor", OwnerUserId = "actor", PlanningInputVersion = PlanningInputVersions.Legacy, AnchorDate = new DateOnly(2024, 1, 1) };
        var other = new PlanVersion { ProjectId = 1, VersionNo = 2, CreatedByUserId = "other", OwnerUserId = "other", PlanningInputVersion = PlanningInputVersions.Legacy, AnchorDate = new DateOnly(2024, 2, 2) };
        db.PlanVersions.AddRange(mine, other);
        await db.SaveChangesAsync();
        var service = Service(db, "actor");
        Assert.Equal(PlanningInputVersions.Legacy, (await service.GetMyDraftAsync(1))!.PlanningInputVersion);
        await service.CreateOrGetDraftAsync(1);
        Assert.Equal(PlanningInputVersions.Legacy, mine.PlanningInputVersion);
        await UpgradeAsync(service, mine, exact: true);
        Assert.Equal(PlanningInputVersions.Current, mine.PlanningInputVersion);
        Assert.Equal(new DateOnly(2026, 11, 1), mine.AnchorDate);
        Assert.Equal(PlanningInputVersions.Legacy, other.PlanningInputVersion);
        Assert.Equal(new DateOnly(2024, 2, 2), other.AnchorDate);
    }

    [Fact]
    public async Task GenerateDraft_RejectsLegacyPolicyBeforeProducingDates()
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        var legacy = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "actor", PlanningInputVersion = PlanningInputVersions.Legacy, AnchorDate = new DateOnly(2024, 1, 1) };
        legacy.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, DurationDays = 7 });
        db.PlanVersions.Add(legacy);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PlanGenerationService(db).GenerateDraftAsync(1, legacy.Id));
        Assert.Contains("upgrade", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(legacy.StagePlans.Single().PlannedStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyDraft_GenericRetrievalPreservesPrivateDurationInputsBeforeModeSelection(bool orphan)
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        var legacy = LegacyDurationDraft();
        if (orphan) legacy.OwnerUserId = null;
        db.PlanVersions.Add(legacy);
        await db.SaveChangesAsync();

        var retrieved = await Service(db, "actor").CreateOrGetDraftAsync(1);

        Assert.Equal(legacy.Id, retrieved.Id);
        Assert.Equal("actor", retrieved.OwnerUserId);
        Assert.Equal(PlanningInputVersions.Legacy, retrieved.PlanningInputVersion);
        Assert.Equal(new DateOnly(2035, 1, 1), retrieved.AnchorDate);
        Assert.False(retrieved.SkipWeekends);
        Assert.Equal(PlanTransitionRule.SameDay, retrieved.TransitionRule);
        Assert.True(retrieved.PncApplicable);
        Assert.Equal(99, retrieved.StagePlans.Single(row => row.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(15, retrieved.StagePlans.Single(row => row.StageCode == StageCodes.PNC).DurationDays);
        Assert.Equal(45, retrieved.StagePlans.Single(row => row.StageCode == "PRIVATE").DurationDays);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task LegacyDurationUpgrade_PreservesPrivateInputsAndNormalizesOnlyMissingPolicyPncAndKnownOrder(
        bool trusted, bool livePnc, bool legacyPnc)
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        var project = await db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = trusted ? PlanningInputVersions.Current : PlanningInputVersions.Legacy;
        project.PncApplicable = livePnc;
        var legacy = LegacyDurationDraft();
        legacy.PncApplicable = legacyPnc;
        legacy.SkipHolidays = trusted;
        db.PlanVersions.Add(legacy);
        await db.SaveChangesAsync();
        var originalRows = legacy.StagePlans.ToDictionary(row => row.StageCode,
            row => (row.DurationDays, row.PlannedStart, row.PlannedDue, row.SortOrder));
        var service = Service(db, "actor");

        await UpgradeAsync(service, legacy, exact: false);

        Assert.Equal(PlanningInputVersions.Current, legacy.PlanningInputVersion);
        Assert.Equal(new DateOnly(2035, 1, 1), legacy.AnchorDate);
        Assert.False(legacy.SkipWeekends);
        Assert.Equal(PlanTransitionRule.SameDay, legacy.TransitionRule);
        Assert.Equal(!trusted, legacy.SkipHolidays);
        Assert.Equal(livePnc && legacyPnc, legacy.PncApplicable);
        Assert.Equal(10, legacy.StagePlans.Single(row => row.StageCode == StageCodes.FS).SortOrder);
        foreach (var row in legacy.StagePlans.Where(row =>
                     livePnc && legacyPnc || row.StageCode != StageCodes.PNC))
        {
            var original = originalRows[row.StageCode];
            Assert.Equal(original.DurationDays, row.DurationDays);
            Assert.Equal(original.PlannedStart, row.PlannedStart);
            Assert.Equal(original.PlannedDue, row.PlannedDue);
        }
        var extension = legacy.StagePlans.Single(row => row.StageCode == "PRIVATE");
        Assert.Equal(45, extension.DurationDays);
        Assert.Equal(731, extension.SortOrder);
        var pnc = legacy.StagePlans.Single(row => row.StageCode == StageCodes.PNC);
        if (!livePnc)
        {
            Assert.Equal(0, pnc.DurationDays);
            Assert.Null(pnc.PlannedStart);
            Assert.Null(pnc.PlannedDue);
        }
        Assert.Equal(3, legacy.StagePlans.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), (await db.ProjectScheduleSettings.SingleAsync()).AnchorStart);
        Assert.Equal(5, (await db.ProjectPlanDurations.SingleAsync(row => row.StageCode == StageCodes.FS)).DurationDays);
        Assert.Equal(new DateOnly(2026, 11, 2), (await db.ProjectStages.SingleAsync(row => row.StageCode == StageCodes.FS)).PlannedStart);

        // Current proposals must never have their edited fields reset by either cutover API.
        legacy.AnchorDate = new DateOnly(2040, 2, 3);
        legacy.SkipHolidays = trusted;
        extension.DurationDays = 46;
        await db.SaveChangesAsync();
        await UpgradeAsync(service, legacy, exact: false);
        await UpgradeAsync(service, legacy, exact: true);
        Assert.Equal(new DateOnly(2040, 2, 3), legacy.AnchorDate);
        Assert.Equal(trusted, legacy.SkipHolidays);
        Assert.Equal(46, extension.DurationDays);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task LegacyExactUpgrade_RecalculatesAllPreservedDatesWithAcceptedOfficeCalendar(
        bool injectedCalendar, int privateDuration)
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        (await db.Projects.SingleAsync()).PublishedPlanningInputVersion = PlanningInputVersions.Legacy;
        db.Holidays.AddRange(
            new Holiday { Date = new DateOnly(2026, 11, 11), Name = "Gazetted", Type = HolidayType.Gazetted },
            new Holiday { Date = new DateOnly(2026, 12, 25), Name = "Gazetted", Type = HolidayType.Gazetted },
            new Holiday { Date = new DateOnly(2026, 12, 24), Name = "Observed", Type = HolidayType.Restricted, IsObservedAsOfficeHoliday = true },
            new Holiday { Date = new DateOnly(2026, 12, 28), Name = "Informational", Type = HolidayType.Restricted, IsObservedAsOfficeHoliday = false });
        var legacy = LegacyDurationDraft();
        legacy.StagePlans.Single(row => row.StageCode == StageCodes.FS).PlannedStart = new DateOnly(2026, 11, 10);
        legacy.StagePlans.Single(row => row.StageCode == StageCodes.FS).PlannedDue = new DateOnly(2026, 11, 12);
        var extension = legacy.StagePlans.Single(row => row.StageCode == "PRIVATE");
        extension.PlannedStart = new DateOnly(2026, 12, 24);
        extension.PlannedDue = new DateOnly(2026, 12, 28);
        legacy.StagePlans.Add(new StagePlan { StageCode = "INCOMPLETE", DurationDays = 9, PlannedStart = new DateOnly(2027, 1, 1) });
        legacy.StagePlans.Add(new StagePlan { StageCode = "REVERSED", DurationDays = 9, PlannedStart = new DateOnly(2027, 2, 2), PlannedDue = new DateOnly(2027, 2, 1) });
        db.PlanVersions.Add(legacy);
        await db.SaveChangesAsync();
        var office = new CutoverOfficeCalendar();
        var service = injectedCalendar
            ? ServiceWithCalendar(db, office)
            : Service(db, "actor");

        await UpgradeAsync(service, legacy, exact: true);

        Assert.Equal(PlanningInputVersions.Current, legacy.PlanningInputVersion);
        Assert.Null(legacy.AnchorDate);
        Assert.True(legacy.SkipWeekends);
        Assert.True(legacy.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, legacy.TransitionRule);
        Assert.False(legacy.PncApplicable);
        Assert.Equal(2, legacy.StagePlans.Single(row => row.StageCode == StageCodes.FS).DurationDays);
        Assert.Equal(privateDuration, extension.DurationDays);
        Assert.Equal(new DateOnly(2026, 12, 24), extension.PlannedStart);
        Assert.Equal(new DateOnly(2026, 12, 28), extension.PlannedDue);
        Assert.Equal(0, legacy.StagePlans.Single(row => row.StageCode == "INCOMPLETE").DurationDays);
        Assert.Equal(0, legacy.StagePlans.Single(row => row.StageCode == "REVERSED").DurationDays);
        Assert.Equal(0, legacy.StagePlans.Single(row => row.StageCode == StageCodes.PNC).DurationDays);
        Assert.Null(legacy.StagePlans.Single(row => row.StageCode == StageCodes.PNC).PlannedStart);
        if (injectedCalendar)
        {
            Assert.True(office.Start <= new DateOnly(2026, 11, 10));
            Assert.True(office.End > new DateOnly(2027, 2, 2));
        }
        Assert.Equal(new DateOnly(2026, 11, 1), (await db.ProjectScheduleSettings.SingleAsync()).AnchorStart);
        Assert.Equal(5, (await db.ProjectPlanDurations.SingleAsync(row => row.StageCode == StageCodes.FS)).DurationDays);
    }

    [Theory]
    [InlineData(true, true, PlanVersionStatus.Draft)]
    [InlineData(false, true, PlanVersionStatus.Draft)]
    [InlineData(true, false, PlanVersionStatus.PendingApproval)]
    [InlineData(false, false, PlanVersionStatus.Approved)]
    public async Task LegacyUpgrade_RejectsAnotherOwnerOrUneditableRevision(bool exact, bool foreign, PlanVersionStatus status)
    {
        await using var db = CreateDb();
        await SeedPublishedAsync(db);
        var legacy = LegacyDurationDraft();
        legacy.Status = status;
        if (foreign) legacy.OwnerUserId = "other";
        db.PlanVersions.Add(legacy);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => UpgradeAsync(Service(db, "actor"), legacy, exact));

        Assert.Equal(PlanningInputVersions.Legacy, legacy.PlanningInputVersion);
        Assert.Equal(new DateOnly(2035, 1, 1), legacy.AnchorDate);
        Assert.Equal(99, legacy.StagePlans.Single(row => row.StageCode == StageCodes.FS).DurationDays);
    }

    private static PlanVersion LegacyDurationDraft()
    {
        var draft = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, CreatedByUserId = "actor", OwnerUserId = "actor",
            PlanningInputVersion = PlanningInputVersions.Legacy, AnchorDate = new DateOnly(2035, 1, 1),
            SkipWeekends = false, SkipHolidays = false, TransitionRule = PlanTransitionRule.SameDay, PncApplicable = true
        };
        draft.StagePlans.AddRange(new[]
        {
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 99, SortOrder = 999, PlannedStart = new DateOnly(2035, 1, 1), PlannedDue = new DateOnly(2035, 4, 9) },
            new StagePlan { StageCode = StageCodes.PNC, DurationDays = 15, SortOrder = 888, PlannedStart = new DateOnly(2035, 4, 9), PlannedDue = new DateOnly(2035, 4, 23) },
            new StagePlan { StageCode = "PRIVATE", DurationDays = 45, SortOrder = 731, PlannedStart = new DateOnly(2035, 4, 23), PlannedDue = new DateOnly(2035, 6, 6) }
        });
        return draft;
    }

    private static async Task UpgradeAsync(PlanDraftService service, PlanVersion draft, bool exact)
    {
        var methodName = exact ? "UpgradeLegacyDraftForExactAsync" : "UpgradeLegacyDraftForDurationAsync";
        var method = typeof(PlanDraftService).GetMethod(methodName, new[] { typeof(PlanVersion), typeof(CancellationToken) });
        Assert.NotNull(method);
        await (Task)method.Invoke(service, new object[] { draft, CancellationToken.None })!;
    }

    private static PlanDraftService ServiceWithCalendar(ApplicationDbContext db, IOfficeCalendarService office)
    {
        var constructor = typeof(PlanDraftService).GetConstructor(new[]
        {
            typeof(ApplicationDbContext), typeof(IClock), typeof(Microsoft.Extensions.Logging.ILogger<PlanDraftService>),
            typeof(IAuditService), typeof(IUserContext), typeof(IOfficeCalendarService)
        });
        Assert.NotNull(constructor);
        return (PlanDraftService)constructor.Invoke(new object[]
        {
            db, new TestClock(), NullLogger<PlanDraftService>.Instance, new NullAudit(), new UserContext("actor"), office
        });
    }

    private sealed class CutoverOfficeCalendar : IOfficeCalendarService
    {
        public DateOnly Start { get; private set; }
        public DateOnly End { get; private set; }
        public Task<IReadOnlySet<DateOnly>> GetNonWorkingDatesAsync(DateOnly startInclusive, DateOnly endExclusive, CancellationToken cancellationToken = default)
        {
            Start = startInclusive;
            End = endExclusive;
            return Task.FromResult<IReadOnlySet<DateOnly>>(new HashSet<DateOnly> { new(2026, 11, 12), new(2026, 12, 28) });
        }
        public Task<IReadOnlyList<OfficeCalendarDay>> GetCalendarDaysAsync(DateOnly startInclusive, DateOnly endExclusive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsOfficeHolidayAsync(DateOnly date, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class PublishAfterBaselineRead : DbCommandInterceptor
    {
        public Func<Task>? PublishAsync { get; set; }
        public bool Published { get; private set; }
        private bool _baselineReadStarted;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (PublishAsync is not null && !Published)
            {
                if (_baselineReadStarted)
                {
                    // The preceding command's reader has been fully materialized.
                    // Publish all compatibility state atomically before any next read.
                    Published = true;
                    await PublishAsync();
                }
                else if (command.CommandText.Contains("FROM \"Projects\"", StringComparison.Ordinal) &&
                         command.CommandText.Contains("\"PublishedPlanningInputVersion\"", StringComparison.Ordinal))
                {
                    _baselineReadStarted = true;
                }
            }
            return result;
        }
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task SeedPublishedAsync(ApplicationDbContext db)
    {
        db.Projects.Add(new Project { Id = 1, Name = "Published", PncApplicable = false, WorkflowVersion = PlanConstants.StageTemplateVersionV1 });
        db.Projects.Local.Single().PublishedPlanningInputVersion = PlanningInputVersions.Current;
        db.StageTemplates.Add(new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.FS, Name = "FS", Sequence = 10 });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings { ProjectId = 1, AnchorStart = new DateOnly(2026, 11, 1), IncludeWeekends = true, SkipHolidays = false, NextStageStartPolicy = NextStageStartPolicies.SameDay });
        db.ProjectPlanDurations.AddRange(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 5, SortOrder = 10 }, new ProjectPlanDuration { ProjectId = 1, StageCode = "EXT", DurationDays = 4, SortOrder = 13 });
        db.ProjectStages.AddRange(new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, PlannedStart = new DateOnly(2026, 11, 2), PlannedDue = new DateOnly(2026, 11, 6) }, new ProjectStage { ProjectId = 1, StageCode = "EXT", SortOrder = 90 });
        await db.SaveChangesAsync();
    }
    private static PlanDraftService Service(ApplicationDbContext db, string actor) => new(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new NullAudit(), new UserContext(actor));
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero); }
    private sealed class UserContext(string actor) : IUserContext
    {
        public string? UserId => actor;
        public ClaimsPrincipal User => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, actor) }, "Test"));
    }
    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null) => Task.CompletedTask;
    }
}
