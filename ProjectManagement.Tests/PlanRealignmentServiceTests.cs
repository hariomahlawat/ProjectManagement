using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanRealignmentServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realignment_UsesCurrentPublishedInputsWithApprovedDateShiftAndIgnoresOtherActorsWork(bool relational)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (relational)
        {
            await connection.OpenAsync();
            options.UseSqlite(connection);
        }
        else options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        await using var db = new ApplicationDbContext(options.Options);
        if (relational) await db.Database.EnsureCreatedAsync();
        db.Projects.Add(new Project { Id = 1, Name = "Realignment", ActivePlanVersionNo = 1, PncApplicable = false, PublishedPlanningInputVersion = PlanningInputVersions.Current });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1, AnchorStart = new DateOnly(2026, 12, 9), IncludeWeekends = true,
            SkipHolidays = false, NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 11, SortOrder = 12 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.IPA, DurationDays = null, SortOrder = 24 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = "EXTENSION", DurationDays = 4, SortOrder = 88 });
        var approved = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, CreatedByUserId = "original-po", Status = PlanVersionStatus.Approved,
            AnchorStageCode = StageCodes.FS, AnchorDate = new DateOnly(2026, 11, 10),
            SkipWeekends = true, SkipHolidays = true, TransitionRule = PlanTransitionRule.NextWorkingDay, PncApplicable = true
        };
        approved.StagePlans.AddRange(new[]
        {
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 7, SortOrder = 10, PlannedStart = new DateOnly(2026, 11, 10), PlannedDue = new DateOnly(2026, 11, 16) },
            new StagePlan { StageCode = StageCodes.IPA, DurationDays = 3, SortOrder = 20, PlannedStart = new DateOnly(2026, 11, 16), PlannedDue = new DateOnly(2026, 11, 18) },
            new StagePlan { StageCode = "EXTENSION", DurationDays = 2, SortOrder = 73, PlannedStart = new DateOnly(2026, 11, 19), PlannedDue = new DateOnly(2026, 11, 20) }
        });
        db.PlanVersions.Add(approved);
        foreach (var (status, number) in new[] { (PlanVersionStatus.Draft, 2), (PlanVersionStatus.PendingApproval, 3) })
        {
            var other = new PlanVersion
            {
                ProjectId = 1, VersionNo = number, CreatedByUserId = "other-actor", OwnerUserId = "other-actor",
                Status = status, AnchorStageCode = StageCodes.IPA, AnchorDate = new DateOnly(2027, 5, 1),
                SkipWeekends = true, SkipHolidays = true, TransitionRule = PlanTransitionRule.NextWorkingDay, PncApplicable = true
            };
            other.StagePlans.Add(new StagePlan { StageCode = StageCodes.FS, DurationDays = 99, SortOrder = 900 });
            db.PlanVersions.Add(other);
        }
        await db.SaveChangesAsync();
        var clock = new TestClock();

        await new PlanRealignmentService(db, clock).CreateRealignmentDraftAsync(1, StageCodes.FS, 4, "trigger-user");

        db.ChangeTracker.Clear();
        var pending = await db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.VersionNo == 4);
        Assert.Equal(PlanVersionStatus.PendingApproval, pending.Status);
        Assert.Null(pending.OwnerUserId);
        Assert.Equal("trigger-user", pending.CreatedByUserId);
        Assert.Equal("trigger-user", pending.SubmittedByUserId);
        Assert.Equal(clock.UtcNow, pending.SubmittedOn);
        Assert.Equal(StageCodes.FS, pending.AnchorStageCode);
        Assert.Equal(new DateOnly(2026, 12, 9), pending.AnchorDate);
        Assert.False(pending.SkipWeekends);
        Assert.False(pending.SkipHolidays);
        Assert.Equal(PlanTransitionRule.SameDay, pending.TransitionRule);
        Assert.False(pending.PncApplicable);
        var rows = pending.StagePlans.ToDictionary(s => s.StageCode);
        Assert.Equal(11, rows[StageCodes.FS].DurationDays);
        Assert.Equal(ProcurementWorkflow.OrderOf(null, StageCodes.FS), rows[StageCodes.FS].SortOrder);
        Assert.Equal(new DateOnly(2026, 11, 10), rows[StageCodes.FS].PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 16), rows[StageCodes.FS].PlannedDue);
        Assert.Equal(0, rows[StageCodes.IPA].DurationDays);
        Assert.Equal(ProcurementWorkflow.OrderOf(null, StageCodes.IPA), rows[StageCodes.IPA].SortOrder);
        Assert.Equal(new DateOnly(2026, 11, 20), rows[StageCodes.IPA].PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 22), rows[StageCodes.IPA].PlannedDue);
        Assert.Equal(88, rows["EXTENSION"].SortOrder);
        Assert.Equal(4, rows["EXTENSION"].DurationDays);
        var audit = await db.PlanRealignmentAudits.SingleAsync();
        Assert.Equal(4, audit.PlanVersionNo);
        Assert.Equal(4, audit.DelayDays);
        Assert.Equal(StageCodes.FS, audit.SourceStageCode);
        Assert.Equal(1, (await db.Projects.SingleAsync()).ActivePlanVersionNo);
        Assert.Equal(2, await db.PlanVersions.CountAsync(p => p.OwnerUserId == "other-actor"));
    }

    [Fact]
    public async Task Realignment_AbsentPublishedInputsUseDefaultsInsteadOfOldApprovedInputs()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project
        {
            Id = 1, Name = "Legacy realignment", ActivePlanVersionNo = 1, PncApplicable = false,
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.FS, SortOrder = 15,
            PlannedStart = new DateOnly(2028, 1, 1), PlannedDue = new DateOnly(2028, 1, 2)
        });
        db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.IPA, Name = "IPA", Sequence = 20
        });
        var approved = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, CreatedByUserId = "original-po", Status = PlanVersionStatus.Approved,
            AnchorStageCode = StageCodes.FS, AnchorDate = new DateOnly(2026, 11, 10),
            SkipWeekends = false, SkipHolidays = false, TransitionRule = PlanTransitionRule.SameDay, PncApplicable = true
        };
        approved.StagePlans.AddRange(new[]
        {
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 7, SortOrder = 91, PlannedStart = new DateOnly(2026, 11, 10), PlannedDue = new DateOnly(2026, 11, 16) },
            new StagePlan { StageCode = StageCodes.IPA, DurationDays = 3, SortOrder = 92, PlannedStart = new DateOnly(2026, 11, 16), PlannedDue = new DateOnly(2026, 11, 18) },
            new StagePlan { StageCode = "EXTENSION", DurationDays = 2, SortOrder = 93 }
        });
        db.PlanVersions.Add(approved);
        await db.SaveChangesAsync();
        var extensionFallback = approved.StagePlans.Single(s => s.StageCode == "EXTENSION").Id;
        var clock = new TestClock();

        await new PlanRealignmentService(db, clock).CreateRealignmentDraftAsync(1, StageCodes.FS, 4, "trigger-user");

        db.ChangeTracker.Clear();
        var pending = await db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.VersionNo == 2);
        Assert.Null(pending.AnchorDate);
        Assert.True(pending.SkipWeekends);
        Assert.True(pending.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, pending.TransitionRule);
        Assert.False(pending.PncApplicable);
        Assert.Null(pending.OwnerUserId);
        Assert.Equal(PlanVersionStatus.PendingApproval, pending.Status);
        Assert.All(pending.StagePlans, s => Assert.Equal(0, s.DurationDays));
        var rows = pending.StagePlans.ToDictionary(s => s.StageCode);
        Assert.Equal(ProcurementWorkflow.OrderOf(PlanConstants.StageTemplateVersionV1, StageCodes.FS), rows[StageCodes.FS].SortOrder);
        Assert.Equal(20, rows[StageCodes.IPA].SortOrder);
        Assert.Equal(extensionFallback, rows["EXTENSION"].SortOrder);
        Assert.Equal(new DateOnly(2026, 11, 10), rows[StageCodes.FS].PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 20), rows[StageCodes.IPA].PlannedStart);
        Assert.Empty(await db.ProjectScheduleSettings.ToListAsync());
        Assert.Empty(await db.ProjectPlanDurations.ToListAsync());
        Assert.Equal(new DateOnly(2028, 1, 1), (await db.ProjectStages.SingleAsync()).PlannedStart);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realignment_LegacySharedPrivateInputsAreIgnoredEvenAfterOldDraftDiscard(bool discarded)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Projects.Add(new Project
        {
            Id = 1, Name = "Legacy private inputs", PncApplicable = false, ActivePlanVersionNo = 1,
            PublishedPlanningInputVersion = PlanningInputVersions.Legacy
        });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1, AnchorStart = new DateOnly(2027, 5, 1), IncludeWeekends = true,
            SkipHolidays = false, NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 99, SortOrder = 999 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 88, SortOrder = 998 });
        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.Completed,
            PlannedStart = new DateOnly(2026, 11, 20), PlannedDue = new DateOnly(2026, 11, 21),
            ActualStart = new DateOnly(2026, 11, 20), CompletedOn = new DateOnly(2026, 11, 21)
        });
        var approved = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, CreatedByUserId = "old-po", Status = PlanVersionStatus.Approved,
            PlanningInputVersion = PlanningInputVersions.Legacy
        };
        approved.StagePlans.AddRange(new[]
        {
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 5, PlannedStart = new DateOnly(2026, 11, 1), PlannedDue = new DateOnly(2026, 11, 5) },
            new StagePlan { StageCode = StageCodes.PNC, DurationDays = 4, PlannedStart = new DateOnly(2026, 11, 20), PlannedDue = new DateOnly(2026, 11, 21) }
        });
        db.PlanVersions.Add(approved);
        if (!discarded)
            db.PlanVersions.Add(new PlanVersion
            {
                ProjectId = 1, VersionNo = 2, CreatedByUserId = "private-po", OwnerUserId = "private-po",
                Status = PlanVersionStatus.Draft, PlanningInputVersion = PlanningInputVersions.Legacy,
                AnchorDate = new DateOnly(2027, 5, 1)
            });
        await db.SaveChangesAsync();

        await new PlanRealignmentService(db, new TestClock()).CreateRealignmentDraftAsync(1, StageCodes.FS, 4, "trigger-user");

        db.ChangeTracker.Clear();
        var pending = await db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.Status == PlanVersionStatus.PendingApproval);
        Assert.Equal(PlanningInputVersions.Current, pending.PlanningInputVersion);
        Assert.Null(pending.OwnerUserId);
        Assert.Null(pending.AnchorDate);
        Assert.True(pending.SkipWeekends);
        Assert.True(pending.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, pending.TransitionRule);
        Assert.False(pending.PncApplicable);
        var fs = pending.StagePlans.Single(s => s.StageCode == StageCodes.FS);
        Assert.Equal(0, fs.DurationDays);
        Assert.Equal(new DateOnly(2026, 11, 1), fs.PlannedStart);
        var pnc = pending.StagePlans.Single(s => s.StageCode == StageCodes.PNC);
        Assert.Equal(0, pnc.DurationDays);
        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        var official = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 11, 20), official.PlannedStart);
        Assert.Equal(StageStatus.Completed, official.Status);
        Assert.Equal(99, (await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.FS)).DurationDays);
        Assert.Equal(PlanningInputVersions.Legacy, (await db.Projects.SingleAsync()).PublishedPlanningInputVersion);
    }

    [Fact]
    public async Task Realignment_RejectionPreservesActorsExistingPrivateDraftWithoutOwnerCollision()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Projects.Add(new Project { Id = 1, Name = "Realignment rejection", ActivePlanVersionNo = 1 });
        var approved = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, CreatedByUserId = "original-po", Status = PlanVersionStatus.Approved
        };
        approved.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS, DurationDays = 3,
            PlannedStart = new DateOnly(2026, 11, 10), PlannedDue = new DateOnly(2026, 11, 12)
        });
        var privateDraft = new PlanVersion
        {
            ProjectId = 1, VersionNo = 2, CreatedByUserId = "trigger-user", OwnerUserId = "trigger-user",
            Status = PlanVersionStatus.Draft, AnchorDate = new DateOnly(2027, 1, 1),
            SkipHolidays = false, PncApplicable = false
        };
        privateDraft.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS, DurationDays = 17, SortOrder = 12,
            PlannedStart = new DateOnly(2027, 1, 1), PlannedDue = new DateOnly(2027, 1, 17)
        });
        db.PlanVersions.AddRange(approved, privateDraft);
        await db.SaveChangesAsync();
        var privateDraftId = privateDraft.Id;
        var privateStageId = privateDraft.StagePlans.Single().Id;
        var clock = new TestClock();

        await new PlanRealignmentService(db, clock).CreateRealignmentDraftAsync(1, StageCodes.FS, 4, "trigger-user");
        var pending = await db.PlanVersions.SingleAsync(p => p.VersionNo == 3);
        Assert.Equal(PlanVersionStatus.PendingApproval, pending.Status);
        Assert.Equal("trigger-user", pending.CreatedByUserId);
        Assert.Equal("trigger-user", pending.SubmittedByUserId);
        var approval = new PlanApprovalService(db, clock, NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(db), new NullNotifications());

        Assert.True(await approval.RejectVersionAsync(pending.Id, "hod-reviewer", false, true, "Revise this realignment."));

        db.ChangeTracker.Clear();
        var rejected = await db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.VersionNo == 3);
        Assert.Equal(PlanVersionStatus.Draft, rejected.Status);
        Assert.Null(rejected.OwnerUserId);
        Assert.Equal("trigger-user", rejected.CreatedByUserId);
        Assert.Equal("hod-reviewer", rejected.RejectedByUserId);
        Assert.Equal("Revise this realignment.", rejected.RejectionNote);
        Assert.Single(rejected.StagePlans);
        var unchanged = await db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.Id == privateDraftId);
        Assert.Equal(PlanVersionStatus.Draft, unchanged.Status);
        Assert.Equal("trigger-user", unchanged.OwnerUserId);
        Assert.Equal(new DateOnly(2027, 1, 1), unchanged.AnchorDate);
        Assert.False(unchanged.SkipHolidays);
        Assert.False(unchanged.PncApplicable);
        var stage = Assert.Single(unchanged.StagePlans);
        Assert.Equal(privateStageId, stage.Id);
        Assert.Equal(17, stage.DurationDays);
        Assert.Equal(12, stage.SortOrder);
        Assert.Equal(new DateOnly(2027, 1, 1), stage.PlannedStart);
        Assert.Equal(new DateOnly(2027, 1, 17), stage.PlannedDue);
        Assert.Equal(1, await db.PlanVersions.CountAsync(p => p.OwnerUserId == "trigger-user" && p.Status == PlanVersionStatus.Draft));
        Assert.Equal("Rejected", (await db.PlanApprovalLogs.SingleAsync()).Action);
    }

    private sealed class NullNotifications : IPlanNotificationService
    {
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    }
}
