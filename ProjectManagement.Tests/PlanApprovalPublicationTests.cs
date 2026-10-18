using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanApprovalPublicationTests
{
    public static IEnumerable<object[]> PublicationPolicies()
    {
        foreach (var includeWeekends in new[] { false, true })
        foreach (var skipHolidays in new[] { false, true })
        foreach (var sameDay in new[] { false, true })
        foreach (var pncApplicable in new[] { false, true })
            yield return new object[] { includeWeekends, skipHolidays, sameDay, pncApplicable };
    }

    [Theory]
    [MemberData(nameof(PublicationPolicies))]
    public async Task Approval_PublishesReviewedInputsInsteadOfContradictoryGlobals(
        bool includeWeekends, bool skipHolidays, bool sameDay, bool pncApplicable)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var plan = await SeedAsync(db, includeWeekends, skipHolidays, sameDay, pncApplicable);
        var notifications = new RecordingNotifications();
        var approval = CreateApproval(db, notifications);

        Assert.True(await approval.ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));

        db.ChangeTracker.Clear();
        var settings = await db.ProjectScheduleSettings.SingleAsync();
        Assert.Equal(new DateOnly(2026, 11, 10), settings.AnchorStart);
        Assert.Equal(includeWeekends, settings.IncludeWeekends);
        Assert.Equal(skipHolidays, settings.SkipHolidays);
        Assert.Equal(sameDay ? NextStageStartPolicies.SameDay : NextStageStartPolicies.NextWorkingDay, settings.NextStageStartPolicy);
        var durations = await db.ProjectPlanDurations.ToDictionaryAsync(d => d.StageCode);
        Assert.Equal(7, durations[StageCodes.FS].DurationDays);
        Assert.Equal(10, durations[StageCodes.FS].SortOrder);
        Assert.Null(durations[StageCodes.IPA].DurationDays);
        Assert.Equal(20, durations[StageCodes.IPA].SortOrder);
        Assert.Equal(3, durations["EXTENSION"].DurationDays);
        Assert.Equal(73, durations["EXTENSION"].SortOrder);
        Assert.Equal(pncApplicable ? 2 : (int?)null, durations[StageCodes.PNC].DurationDays);
        Assert.Equal(4, durations.Count);
        var stages = await db.ProjectStages.ToDictionaryAsync(s => s.StageCode);
        Assert.Equal(new DateOnly(2026, 11, 10), stages[StageCodes.FS].PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 18), stages[StageCodes.FS].PlannedDue);
        Assert.Null(stages[StageCodes.IPA].PlannedStart);
        Assert.Null(stages[StageCodes.IPA].PlannedDue);
        Assert.Equal(new DateOnly(2026, 11, 19), stages["EXTENSION"].PlannedStart);
        Assert.Equal(73, stages["EXTENSION"].SortOrder);
        Assert.Equal(pncApplicable ? new DateOnly(2026, 11, 22) : (DateOnly?)null, stages[StageCodes.PNC].PlannedStart);
        Assert.Equal(pncApplicable ? new DateOnly(2026, 11, 23) : (DateOnly?)null, stages[StageCodes.PNC].PlannedDue);
        Assert.Equal(StageStatus.Completed, stages[StageCodes.FS].Status);
        Assert.Equal(new DateOnly(2025, 1, 1), stages[StageCodes.FS].ActualStart);
        Assert.Equal(new DateOnly(2025, 1, 2), stages[StageCodes.FS].CompletedOn);
        var project = await db.Projects.SingleAsync();
        Assert.Equal(pncApplicable, project.PncApplicable);
        Assert.Equal(PlanningInputVersions.Current, project.PublishedPlanningInputVersion);
        Assert.Equal(2, project.ActivePlanVersionNo);
        Assert.Equal("hod-reviewer", project.PlanApprovedByUserId);
        Assert.Equal(PlanVersionStatus.Approved, (await db.PlanVersions.SingleAsync(p => p.Id == plan.Id)).Status);
        Assert.Single(await db.ProjectPlanSnapshots.ToListAsync());
        Assert.Equal(4, await db.ProjectPlanSnapshotRows.CountAsync());
        Assert.Equal(1, notifications.Approvals);
    }

    [Fact]
    public async Task Approval_CreatesMissingPublishedSettingsAndKeepsClearedDurationRow()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var plan = await SeedAsync(db, true, false, true, false);
        db.ProjectScheduleSettings.RemoveRange(db.ProjectScheduleSettings);
        db.ProjectPlanDurations.RemoveRange(db.ProjectPlanDurations);
        await db.SaveChangesAsync();

        Assert.True(await CreateApproval(db, new RecordingNotifications()).ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));

        db.ChangeTracker.Clear();
        var settings = await db.ProjectScheduleSettings.SingleAsync();
        Assert.Equal(plan.AnchorDate, settings.AnchorStart);
        Assert.True(settings.IncludeWeekends);
        Assert.False(settings.SkipHolidays);
        Assert.Equal(NextStageStartPolicies.SameDay, settings.NextStageStartPolicy);
        var cleared = await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.IPA);
        Assert.Null(cleared.DurationDays);
        Assert.Equal(20, cleared.SortOrder);
    }

    [Fact]
    public async Task Approval_FinalSaveFailureRollsBackAllPublishedStoresAndSnapshots()
    {
        // Match the planner's disposable SQLite transaction fixture. PostgreSQL
        // migration coverage owns FK validation; this test owns publication rollback.
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var failure = new FailFinalApprovalSave();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(failure).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var plan = await SeedAsync(db, true, false, true, false);
        var baseline = await PublishedStateAsync(db);
        var notifications = new RecordingNotifications();
        failure.Armed = true;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateApproval(db, notifications).ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));

        Assert.Equal("Injected failure before approval commit.", error.Message);
        Assert.True(failure.CompletedSaves >= 3); // Inputs/dates and snapshot writes reached the database.
        await using var verify = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        Assert.Equal(baseline, await PublishedStateAsync(verify));
        var pending = await verify.PlanVersions.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(PlanVersionStatus.PendingApproval, pending.Status);
        Assert.Null(pending.ApprovedByUserId);
        Assert.Null(pending.ApprovedOn);
        Assert.Equal(new DateOnly(2026, 11, 10), pending.AnchorDate);
        Assert.False(pending.SkipHolidays);
        Assert.Equal(7, (await verify.StagePlans.SingleAsync(s => s.PlanVersionId == plan.Id && s.StageCode == StageCodes.FS)).DurationDays);
        Assert.Empty(await verify.ProjectPlanSnapshots.ToListAsync());
        Assert.Empty(await verify.ProjectPlanSnapshotRows.ToListAsync());
        Assert.Equal(0, notifications.Approvals);
    }

    [Fact]
    public async Task Approval_RevalidatesCurrentHolidayCalendarBeforeAnyAuthoritativeMutation()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project
        {
            Id = 71,
            Name = "Approval calendar change",
            LeadPoUserId = "po-user",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1,
            PncApplicable = false,
            ActivePlanVersionNo = 4,
            PlanApprovedByUserId = "previous-reviewer",
            PlanApprovedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PublishedPlanningInputVersion = PlanningInputVersions.Current
        });
        db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.IPA, Name = "In-Principle Approval", Sequence = 20 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.SOW, Name = "SOW Vetting", Sequence = 30 });
        db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.SOW,
            DependsOnStageCode = StageCodes.IPA
        });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 71,
            AnchorStart = new DateOnly(2026, 10, 20),
            IncludeWeekends = false,
            SkipHolidays = true,
            NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay
        });
        db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 71, StageCode = StageCodes.IPA, DurationDays = 4, SortOrder = 20 });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 71, StageCode = StageCodes.IPA, SortOrder = 20, Status = StageStatus.NotStarted, PlannedStart = new DateOnly(2026, 1, 1), PlannedDue = new DateOnly(2026, 1, 2) },
            new ProjectStage { ProjectId = 71, StageCode = StageCodes.SOW, SortOrder = 30, Status = StageStatus.NotStarted, PlannedStart = new DateOnly(2026, 1, 3), PlannedDue = new DateOnly(2026, 1, 4) });
        var draft = new PlanVersion
        {
            ProjectId = 71,
            VersionNo = 5,
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "po-user",
            OwnerUserId = "po-user",
            AnchorStageCode = StageCodes.IPA,
            AnchorDate = new DateOnly(2026, 10, 20),
            TransitionRule = PlanTransitionRule.NextWorkingDay,
            SkipWeekends = true,
            SkipHolidays = true,
            PncApplicable = false,
            PlanningInputVersion = PlanningInputVersions.Current
        };
        draft.StagePlans.AddRange(new[]
        {
            new StagePlan { StageCode = StageCodes.IPA, SortOrder = 20, DurationDays = 4, PlannedStart = new DateOnly(2026, 10, 20), PlannedDue = new DateOnly(2026, 10, 23) },
            new StagePlan { StageCode = StageCodes.SOW, SortOrder = 30, DurationDays = 5, PlannedStart = new DateOnly(2026, 10, 26), PlannedDue = new DateOnly(2026, 10, 30) }
        });
        db.PlanVersions.Add(draft);
        await db.SaveChangesAsync();
        var notifications = new RecordingNotifications();
        var approval = CreateApproval(db, notifications);
        await approval.SubmitForApprovalAsync(71, "po-user");
        db.Holidays.Add(new Holiday
        {
            Date = new DateOnly(2026, 10, 26),
            Name = "New gazetted closure",
            Type = HolidayType.Gazetted,
            IsObservedAsOfficeHoliday = true
        });
        await db.SaveChangesAsync();
        var publishedBefore = await PublishedStateAsync(db);

        var error = await Assert.ThrowsAsync<PlanApprovalValidationException>(() =>
            approval.ApproveVersionAsync(draft.Id, "hod-reviewer", false, true));

        Assert.Contains(error.Errors, message => message.Contains("27 Oct 2026"));
        db.ChangeTracker.Clear();
        Assert.Equal(publishedBefore, await PublishedStateAsync(db));
        var pending = await db.PlanVersions.SingleAsync(plan => plan.Id == draft.Id);
        Assert.Equal(PlanVersionStatus.PendingApproval, pending.Status);
        Assert.Null(pending.ApprovedByUserId);
        Assert.Null(pending.ApprovedOn);
        Assert.Empty(await db.ProjectPlanSnapshots.ToListAsync());
        Assert.Empty(await db.ProjectPlanSnapshotRows.ToListAsync());
        Assert.Equal(0, notifications.Approvals);
    }

    [Fact]
    public async Task Approval_LegacyPendingUsesAcceptedPublishedTransitionPolicy()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var plan = await SeedAsync(db, includeWeekends: true, skipHolidays: false, sameDay: true, pncApplicable: false);
        plan.PlanningInputVersion = PlanningInputVersions.Legacy;
        (await db.Projects.SingleAsync()).PublishedPlanningInputVersion = PlanningInputVersions.Current;
        (await db.ProjectScheduleSettings.SingleAsync()).NextStageStartPolicy = NextStageStartPolicies.SameDay;
        plan.AnchorStageCode = StageCodes.IPA;
        plan.TransitionRule = PlanTransitionRule.NextWorkingDay;
        var ipa = plan.StagePlans.Single(stage => stage.StageCode == StageCodes.IPA);
        ipa.PlannedStart = new DateOnly(2026, 11, 18);
        ipa.PlannedDue = new DateOnly(2026, 11, 20);
        plan.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.SOW,
            SortOrder = 30,
            PlannedStart = new DateOnly(2026, 11, 20),
            PlannedDue = new DateOnly(2026, 11, 21)
        });
        await db.SaveChangesAsync();

        var approval = CreateApproval(db, new RecordingNotifications());
        var errors = await approval.GetValidationErrorsAsync(plan.Id);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.True(await approval.ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));
    }

    [Fact]
    public async Task Approval_SparseLegacyPendingRemainsApprovableAcrossCompleteCatalogCutover()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var plan = await SeedAsync(db, includeWeekends: true, skipHolidays: false, sameDay: true, pncApplicable: false);
        plan.PlanningInputVersion = PlanningInputVersions.Legacy;
        db.StagePlans.RemoveRange(plan.StagePlans.Where(stage => stage.StageCode != StageCodes.FS));
        await db.SaveChangesAsync();

        var approval = CreateApproval(db, new RecordingNotifications());
        var errors = await approval.GetValidationErrorsAsync(plan.Id);

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.True(await approval.ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));
        Assert.Equal(PlanVersionStatus.Approved, (await db.PlanVersions.SingleAsync(item => item.Id == plan.Id)).Status);
    }

    [Theory]
    [InlineData(PlanningInputVersions.Legacy)]
    [InlineData(PlanningInputVersions.Current)]
    public async Task Approval_LegacyPendingPublishesDatesAndPncWithoutPromotingOrChangingCompatibilityInputs(int publishedVersion)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var plan = await SeedAsync(db, true, false, true, false);
        plan.PlanningInputVersion = PlanningInputVersions.Legacy;
        var project = await db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = publishedVersion;
        // Previous Exact proposals retained creation defaults, not the policy used by Exact calculation.
        plan.AnchorDate = new DateOnly(2026, 10, 1);
        plan.SkipWeekends = true;
        plan.TransitionRule = PlanTransitionRule.NextWorkingDay;
        var settings = await db.ProjectScheduleSettings.SingleAsync();
        settings.AnchorStart = new DateOnly(2026, 11, 1);
        settings.IncludeWeekends = true;
        settings.SkipHolidays = false;
        settings.NextStageStartPolicy = NextStageStartPolicies.SameDay;
        await db.SaveChangesAsync();
        var compatibilityBefore = await CompatibilityStateAsync(db);

        Assert.True(await CreateApproval(db, new RecordingNotifications()).ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));

        db.ChangeTracker.Clear();
        Assert.Equal(compatibilityBefore, await CompatibilityStateAsync(db));
        project = await db.Projects.SingleAsync();
        Assert.Equal(publishedVersion, project.PublishedPlanningInputVersion);
        Assert.False(project.PncApplicable);
        Assert.Equal(2, project.ActivePlanVersionNo);
        var fs = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS);
        Assert.Equal(new DateOnly(2026, 11, 10), fs.PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 18), fs.PlannedDue);
        var pnc = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.Equal(StageStatus.Completed, pnc.Status);
        Assert.Equal(PlanVersionStatus.Approved, (await db.PlanVersions.SingleAsync(p => p.Id == plan.Id)).Status);
        Assert.Single(await db.ProjectPlanSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Approval_FirstCompletePublicationRemovesPrivateOnlyCatalogRowsAndKeepsAuthoritativeExtensions()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var plan = await SeedAsync(db, true, false, true, false);
        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration
            {
                ProjectId = 1, StageCode = "OLD_PRIVATE_EXTENSION", DurationDays = 99, SortOrder = 987
            },
            new ProjectPlanDuration
            {
                ProjectId = 1, StageCode = "OFFICIAL_EXTENSION", DurationDays = 88, SortOrder = 986
            });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = "OFFICIAL_EXTENSION", SortOrder = 986,
            PlannedStart = new DateOnly(2026, 11, 24), PlannedDue = new DateOnly(2026, 11, 25)
        });
        await db.SaveChangesAsync();

        Assert.True(await CreateApproval(db, new RecordingNotifications()).ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));

        db.ChangeTracker.Clear();
        Assert.False(await db.ProjectPlanDurations.AnyAsync(d => d.StageCode == "OLD_PRIVATE_EXTENSION"));
        var officialExtension = await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == "OFFICIAL_EXTENSION");
        Assert.Null(officialExtension.DurationDays);
        Assert.Equal(986, officialExtension.SortOrder);
        Assert.Equal(3, (await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == "EXTENSION")).DurationDays);
        Assert.Equal(7, (await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.FS)).DurationDays);
        Assert.Null((await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.PNC)).DurationDays);
        Assert.False((await db.Projects.SingleAsync()).PncApplicable);
        Assert.Equal(PlanningInputVersions.Current, (await db.Projects.SingleAsync()).PublishedPlanningInputVersion);

        var editor = await new PlanReadService(db).GetAsync(1, "uninvolved-after-approval");
        Assert.DoesNotContain(editor.Durations.Rows, row => row.Code == "OLD_PRIVATE_EXTENSION");
        Assert.Contains(editor.Durations.Rows, row => row.Code == "OFFICIAL_EXTENSION");
        Assert.Contains(editor.Durations.Rows, row => row.Code == "EXTENSION");
    }

    private static async Task<string> CompatibilityStateAsync(ApplicationDbContext db)
    {
        var settings = await db.ProjectScheduleSettings.AsNoTracking().OrderBy(s => s.ProjectId).ToListAsync();
        var durations = await db.ProjectPlanDurations.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Settings = settings.Select(s => new { s.ProjectId, s.AnchorStart, s.IncludeWeekends, s.SkipHolidays, s.NextStageStartPolicy }),
            Durations = durations.Select(s => new { s.Id, s.ProjectId, s.StageCode, s.DurationDays, s.SortOrder })
        });
    }


    [Fact]
    public async Task ExactDecisions_RejectCrossProjectIdentifiersWithoutPublication()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var plan = await SeedAsync(db, true, false, true, false);
        var baseline = await PublishedStateAsync(db);
        var notifications = new RecordingNotifications();
        var approval = CreateApproval(db, notifications);

        Assert.False(await approval.ApproveVersionAsync(
            plan.Id, "hod-reviewer", false, true, expectedProjectId: 99));
        Assert.False(await approval.RejectVersionAsync(
            plan.Id, "hod-reviewer", false, true, "wrong project", expectedProjectId: 99));
        Assert.Equal(baseline, await PublishedStateAsync(db));
        Assert.Equal(PlanVersionStatus.PendingApproval, (await db.PlanVersions.SingleAsync(p => p.Id == plan.Id)).Status);
        Assert.Empty(await db.ProjectPlanSnapshots.ToListAsync());
        Assert.Empty(await db.PlanApprovalLogs.ToListAsync());
        Assert.Equal(0, notifications.Approvals);
        Assert.Equal(0, notifications.Rejections);
    }

    [Fact]
    public async Task ExactApproval_PublishesOnlyReviewedRevisionEvenWithNewerPending()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var reviewed = await SeedAsync(db, true, false, true, false);
        var newer = AddNewerPending(db);
        await db.SaveChangesAsync();
        var notifications = new RecordingNotifications();

        Assert.True(await CreateApproval(db, notifications).ApproveVersionAsync(
            reviewed.Id, "hod-reviewer", false, true, expectedProjectId: 1));

        db.ChangeTracker.Clear();
        Assert.Equal(PlanVersionStatus.Approved, (await db.PlanVersions.SingleAsync(p => p.Id == reviewed.Id)).Status);
        Assert.Equal(PlanVersionStatus.PendingApproval, (await db.PlanVersions.SingleAsync(p => p.Id == newer.Id)).Status);
        Assert.Equal(2, (await db.Projects.SingleAsync()).ActivePlanVersionNo);
        Assert.Equal(new DateOnly(2026, 11, 10),
            (await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.FS)).PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 10), (await db.ProjectScheduleSettings.SingleAsync()).AnchorStart);
        Assert.Single(await db.ProjectPlanSnapshots.ToListAsync());
        Assert.Equal(1, notifications.Approvals);
    }

    [Fact]
    public async Task ExactRejection_ReturnsOnlyReviewedRevisionToDraft()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var reviewed = await SeedAsync(db, true, false, true, false);
        var newer = AddNewerPending(db);
        await db.SaveChangesAsync();
        var baseline = await PublishedStateAsync(db);
        var notifications = new RecordingNotifications();

        Assert.True(await CreateApproval(db, notifications).RejectVersionAsync(
            reviewed.Id, "hod-reviewer", false, true, "Revise it", expectedProjectId: 1));

        db.ChangeTracker.Clear();
        Assert.Equal(PlanVersionStatus.Draft, (await db.PlanVersions.SingleAsync(p => p.Id == reviewed.Id)).Status);
        Assert.Equal(PlanVersionStatus.PendingApproval, (await db.PlanVersions.SingleAsync(p => p.Id == newer.Id)).Status);
        Assert.Equal(baseline, await PublishedStateAsync(db));
        var log = Assert.Single(await db.PlanApprovalLogs.ToListAsync());
        Assert.Equal(reviewed.Id, log.PlanVersionId);
        Assert.Equal("Rejected", log.Action);
        Assert.Equal(1, notifications.Rejections);
        Assert.Equal(0, notifications.Approvals);
    }

    [Fact]
    public async Task ExactApproval_RepeatedOrOppositeDecisionCannotMutateAfterCommit()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var plan = await SeedAsync(db, true, false, true, false);
        var notifications = new RecordingNotifications();

        Assert.True(await CreateApproval(db, notifications).ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));
        var published = await PublishedStateAsync(db);
        await using var staleContext = new ApplicationDbContext(options);
        var staleService = CreateApproval(staleContext, notifications);
        Assert.False(await staleService.ApproveVersionAsync(plan.Id, "second-hod", false, true));
        Assert.False(await staleService.RejectVersionAsync(plan.Id, "second-hod", false, true, "too late"));

        Assert.Equal(published, await PublishedStateAsync(staleContext));
        Assert.Equal(PlanVersionStatus.Approved, (await staleContext.PlanVersions.SingleAsync(p => p.Id == plan.Id)).Status);
        Assert.Single(await staleContext.ProjectPlanSnapshots.ToListAsync());
        Assert.Empty(await staleContext.PlanApprovalLogs.ToListAsync());
        Assert.Equal(1, notifications.Approvals);
        Assert.Equal(0, notifications.Rejections);
    }

    [Fact]
    public async Task ExactRejection_RepeatedOrOppositeDecisionCannotMutateAfterCommit()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var plan = await SeedAsync(db, true, false, true, false);
        var published = await PublishedStateAsync(db);
        var notifications = new RecordingNotifications();

        Assert.True(await CreateApproval(db, notifications).RejectVersionAsync(plan.Id, "hod-reviewer", false, true, "Revise"));
        await using var staleContext = new ApplicationDbContext(options);
        var staleService = CreateApproval(staleContext, notifications);
        Assert.False(await staleService.RejectVersionAsync(plan.Id, "second-hod", false, true, "again"));
        Assert.False(await staleService.ApproveVersionAsync(plan.Id, "second-hod", false, true));

        Assert.Equal(published, await PublishedStateAsync(staleContext));
        Assert.Equal(PlanVersionStatus.Draft, (await staleContext.PlanVersions.SingleAsync(p => p.Id == plan.Id)).Status);
        Assert.Single(await staleContext.PlanApprovalLogs.ToListAsync());
        Assert.Empty(await staleContext.ProjectPlanSnapshots.ToListAsync());
        Assert.Equal(0, notifications.Approvals);
        Assert.Equal(1, notifications.Rejections);
    }

    [Fact]
    public async Task ExactApproval_StaleStatusDoesNotPublishAnything()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var plan = await SeedAsync(db, true, false, true, false);
        plan.Status = PlanVersionStatus.Draft;
        await db.SaveChangesAsync();
        var published = await PublishedStateAsync(db);
        var notifications = new RecordingNotifications();
        Assert.False(await CreateApproval(db, notifications).ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));

        Assert.Equal(published, await PublishedStateAsync(db));
        Assert.Empty(await db.ProjectPlanSnapshots.ToListAsync());
        Assert.Empty(await db.PlanApprovalLogs.ToListAsync());
        Assert.Equal(0, notifications.Approvals);
    }

    [Fact]
    public async Task ExactComparison_UsesSelectedRevisionNotLatestPending()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var reviewed = await SeedAsync(db, true, false, true, false);
        var newer = AddNewerPending(db);
        await db.SaveChangesAsync();
        var compare = new PlanCompareService(db);

        var exact = await compare.GetReviewAsync(1, reviewed.Id);
        var latest = await compare.GetLatestPendingReviewAsync(1);
        Assert.NotNull(exact);
        Assert.NotNull(latest);
        Assert.Equal(reviewed.Id, exact.PlanVersionId);
        Assert.Equal(newer.Id, latest.PlanVersionId);
        Assert.Equal(new DateOnly(2026, 11, 10), exact.Rows.Single(r => r.StageCode == StageCodes.FS).NewStart);
        Assert.Equal(new DateOnly(2026, 12, 1), latest.Rows.Single(r => r.StageCode == StageCodes.FS).NewStart);
        Assert.Null(await compare.GetReviewAsync(99, reviewed.Id));
    }

    private static PlanVersion AddNewerPending(ApplicationDbContext db)
    {
        var newer = new PlanVersion
        {
            ProjectId = 1,
            VersionNo = 3,
            Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "another-po",
            OwnerUserId = "another-po",
            SubmittedByUserId = "another-po",
            SubmittedOn = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero),
            AnchorDate = new DateOnly(2026, 12, 1),
            PncApplicable = true
        };
        newer.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS,
            DurationDays = 5,
            PlannedStart = new DateOnly(2026, 12, 1),
            PlannedDue = new DateOnly(2026, 12, 5)
        });
        db.PlanVersions.Add(newer);
        return newer;
    }

    private static async Task<PlanVersion> SeedAsync(ApplicationDbContext db, bool includeWeekends, bool skipHolidays, bool sameDay, bool pncApplicable)
    {
        db.Projects.Add(new Project
        {
            Id = 1, Name = "Reviewed proposal", LeadPoUserId = "po-user",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1,
            PncApplicable = !pncApplicable, ActivePlanVersionNo = 1,
            PlanApprovedByUserId = "previous-reviewer", PlanApprovedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
        db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.FS, Name = "FS", Sequence = 10 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.IPA, Name = "IPA", Sequence = 20 });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1, AnchorStart = new DateOnly(2026, 11, 1), IncludeWeekends = !includeWeekends,
            SkipHolidays = !skipHolidays,
            NextStageStartPolicy = sameDay ? NextStageStartPolicies.NextWorkingDay : NextStageStartPolicies.SameDay
        });
        foreach (var code in new[] { StageCodes.FS, StageCodes.IPA, StageCodes.PNC })
        {
            db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = code, DurationDays = 99, SortOrder = 900 });
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1, StageCode = code, SortOrder = 900,
                PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2),
                Status = StageStatus.Completed, ActualStart = new DateOnly(2025, 1, 1), CompletedOn = new DateOnly(2025, 1, 2)
            });
        }
        db.PlanVersions.Add(new PlanVersion { ProjectId = 1, VersionNo = 1, Status = PlanVersionStatus.Approved, CreatedByUserId = "po-user" });
        var plan = new PlanVersion
        {
            ProjectId = 1, VersionNo = 2, Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "po-user", OwnerUserId = "po-user", SubmittedByUserId = "po-user",
            AnchorDate = new DateOnly(2026, 11, 10), SkipWeekends = !includeWeekends, SkipHolidays = skipHolidays,
            TransitionRule = sameDay ? PlanTransitionRule.SameDay : PlanTransitionRule.NextWorkingDay,
            PncApplicable = pncApplicable
        };
        plan.StagePlans.AddRange(new[]
        {
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 7, SortOrder = 10, PlannedStart = new DateOnly(2026, 11, 10), PlannedDue = new DateOnly(2026, 11, 18) },
            new StagePlan { StageCode = StageCodes.IPA, DurationDays = 0, SortOrder = 20 },
            new StagePlan { StageCode = "EXTENSION", DurationDays = 3, SortOrder = 73, PlannedStart = new DateOnly(2026, 11, 19), PlannedDue = new DateOnly(2026, 11, 21) },
            new StagePlan { StageCode = StageCodes.PNC, DurationDays = 2, SortOrder = 80, PlannedStart = new DateOnly(2026, 11, 22), PlannedDue = new DateOnly(2026, 11, 23) }
        });
        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private static async Task<string> PublishedStateAsync(ApplicationDbContext db)
    {
        var settings = await db.ProjectScheduleSettings.AsNoTracking().OrderBy(s => s.ProjectId).ToListAsync();
        var durations = await db.ProjectPlanDurations.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        var stages = await db.ProjectStages.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        var projects = await db.Projects.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Settings = settings.Select(s => new { s.ProjectId, s.AnchorStart, s.IncludeWeekends, s.SkipHolidays, s.NextStageStartPolicy }),
            Durations = durations.Select(s => new { s.Id, s.ProjectId, s.StageCode, s.DurationDays, s.SortOrder }),
            Stages = stages.Select(s => new { s.Id, s.ProjectId, s.StageCode, s.PlannedStart, s.PlannedDue, s.SortOrder, s.Status, s.ActualStart, s.CompletedOn }),
            Projects = projects.Select(p => new { p.Id, p.PncApplicable, p.PublishedPlanningInputVersion, p.ActivePlanVersionNo, p.PlanApprovedAt, p.PlanApprovedByUserId })
        });
    }

    private static PlanApprovalService CreateApproval(ApplicationDbContext db, RecordingNotifications notifications) =>
        new(db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db), notifications);

    private sealed class FailFinalApprovalSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int CompletedSaves { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<PlanVersion>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Status == PlanVersionStatus.Approved))
                throw new InvalidOperationException("Injected failure before approval commit.");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed) CompletedSaves++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingNotifications : IPlanNotificationService
    {
        public int Approvals { get; private set; }
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
        {
            Approvals++;
            return Task.CompletedTask;
        }
        public int Rejections { get; private set; }
        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
        {
            Rejections++;
            return Task.CompletedTask;
        }
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
