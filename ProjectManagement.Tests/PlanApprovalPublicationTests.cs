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
        Assert.Equal(2, durations[StageCodes.PNC].DurationDays);
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
            Projects = projects.Select(p => new { p.Id, p.PncApplicable, p.ActivePlanVersionNo, p.PlanApprovedAt, p.PlanApprovedByUserId })
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
        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
