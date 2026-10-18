using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Runs within the disposable migrated PostgreSQL database used by the
/// production migration gate. SQLite cannot verify advisory-lock behavior.
/// </summary>
internal static class PlanRevisionConcurrencyPostgresFixture
{
    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await ConcurrentSubmissionsAsync(options);
        await RealignmentCreationHonoursProjectGateAsync(options);
        await DraftAndRealignmentAllocateDistinctVersionsAsync(options);
        await DraftBaselineFollowsCommittedPublicationAsync(options);
    }

    private static async Task ConcurrentSubmissionsAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var seed = new ApplicationDbContext(options);
        seed.Users.AddRange(
            new ApplicationUser { Id = "revision-actor-a", UserName = "revision-actor-a" },
            new ApplicationUser { Id = "revision-actor-b", UserName = "revision-actor-b" });
        var project = new Project
        {
            Name = "Concurrent timeline submissions",
            CreatedByUserId = "revision-actor-a",
            LeadPoUserId = "revision-actor-a",
            RowVersion = Array.Empty<byte>()
        };
        seed.Projects.Add(project);
        await seed.SaveChangesAsync();
        seed.PlanVersions.AddRange(
            new PlanVersion
            {
                ProjectId = project.Id, VersionNo = 1, Status = PlanVersionStatus.Draft,
                CreatedByUserId = "revision-actor-a", OwnerUserId = "revision-actor-a",
                PlanningInputVersion = PlanningInputVersions.Current
            },
            new PlanVersion
            {
                ProjectId = project.Id, VersionNo = 2, Status = PlanVersionStatus.Draft,
                CreatedByUserId = "revision-actor-b", OwnerUserId = "revision-actor-b",
                PlanningInputVersion = PlanningInputVersions.Current
            });
        await seed.SaveChangesAsync();

        await using var holder = new ApplicationDbContext(options);
        await using var hold = await holder.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(holder, project.Id, default);

        await using var first = new ApplicationDbContext(options);
        await using var second = new ApplicationDbContext(options);
        var firstSubmit = TrySubmitAsync(first, project.Id, "revision-actor-a");
        var secondSubmit = TrySubmitAsync(second, project.Id, "revision-actor-b");
        await Task.Delay(100);
        Assert.False(firstSubmit.IsCompleted);
        Assert.False(secondSubmit.IsCompleted);
        await hold.CommitAsync();

        var results = await Task.WhenAll(firstSubmit, secondSubmit)
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, results.Count(result => result));
        seed.ChangeTracker.Clear();
        Assert.Equal(1, await seed.PlanVersions.CountAsync(
            plan => plan.ProjectId == project.Id && plan.Status == PlanVersionStatus.PendingApproval));
    }

    private static async Task RealignmentCreationHonoursProjectGateAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var seed = new ApplicationDbContext(options);
        var project = new Project
        {
            Name = "Realignment timeline serialization",
            CreatedByUserId = "revision-actor-a",
            LeadPoUserId = "revision-actor-a",
            RowVersion = Array.Empty<byte>(),
            ActivePlanVersionNo = 1
        };
        seed.Projects.Add(project);
        await seed.SaveChangesAsync();
        var approved = new PlanVersion
        {
            ProjectId = project.Id, VersionNo = 1, Status = PlanVersionStatus.Approved,
            CreatedByUserId = "revision-actor-a", PncApplicable = false
        };
        approved.StagePlans.AddRange(new[]
        {
            new StagePlan
            {
                StageCode = StageCodes.FS, DurationDays = 4,
                PlannedStart = new DateOnly(2026, 11, 1),
                PlannedDue = new DateOnly(2026, 11, 5)
            },
            new StagePlan
            {
                StageCode = StageCodes.IPA, DurationDays = 3,
                PlannedStart = new DateOnly(2026, 11, 6),
                PlannedDue = new DateOnly(2026, 11, 8)
            }
        });
        var older = new PlanVersion
        {
            ProjectId = project.Id, VersionNo = 2, Status = PlanVersionStatus.PendingApproval,
            SubmittedByUserId = "revision-actor-a", CreatedByUserId = "revision-actor-a",
            SubmittedOn = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero)
        };
        seed.PlanVersions.AddRange(approved, older);
        await seed.SaveChangesAsync();

        await using var holder = new ApplicationDbContext(options);
        await using var hold = await holder.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(holder, project.Id, default);

        await using var realignmentDb = new ApplicationDbContext(options);
        var creation = new PlanRealignmentService(realignmentDb, new FixedClock())
            .CreateRealignmentDraftAsync(project.Id, StageCodes.FS, 3, "revision-actor-b");
        await Task.Delay(100);
        Assert.False(creation.IsCompleted);
        await hold.CommitAsync();
        await creation.WaitAsync(TimeSpan.FromSeconds(30));

        seed.ChangeTracker.Clear();
        Assert.Equal(PlanVersionStatus.PendingApproval,
            (await seed.PlanVersions.SingleAsync(v => v.ProjectId == project.Id && v.VersionNo == 3)).Status);
        var approvals = new PlanApprovalService(seed, new FixedClock(),
            NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(seed),
            new NoNotifications(), new AlwaysValidTimeline());
        Assert.False(await approvals.ApproveVersionAsync(
            older.Id, "revision-actor-b", false, true, expectedProjectId: project.Id));
    }

    private static async Task DraftAndRealignmentAllocateDistinctVersionsAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var seed = new ApplicationDbContext(options);
        var project = new Project
        {
            Name = "Concurrent draft and realignment allocation",
            CreatedByUserId = "revision-actor-a",
            LeadPoUserId = "revision-actor-a",
            RowVersion = Array.Empty<byte>(),
            ActivePlanVersionNo = 1
        };
        seed.Projects.Add(project);
        await seed.SaveChangesAsync();
        var published = new PlanVersion
        {
            ProjectId = project.Id, VersionNo = 1,
            Status = PlanVersionStatus.Approved,
            CreatedByUserId = "revision-actor-a"
        };
        published.StagePlans.AddRange(new[]
        {
            new StagePlan
            {
                StageCode = StageCodes.FS, DurationDays = 3,
                PlannedStart = new DateOnly(2026, 11, 1),
                PlannedDue = new DateOnly(2026, 11, 3)
            },
            new StagePlan
            {
                StageCode = StageCodes.IPA, DurationDays = 2,
                PlannedStart = new DateOnly(2026, 11, 4),
                PlannedDue = new DateOnly(2026, 11, 5)
            }
        });
        seed.PlanVersions.Add(published);
        await seed.SaveChangesAsync();

        await using var holder = new ApplicationDbContext(options);
        await using var hold = await holder.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(holder, project.Id, default);

        await using var draftDb = new ApplicationDbContext(options);
        await using var realignmentDb = new ApplicationDbContext(options);
        var actorDraft = new PlanDraftService(draftDb, new FixedClock(),
            NullLogger<PlanDraftService>.Instance, new NullAudit(),
            new ActorContext("revision-actor-a"))
            .CreateOrGetDraftAsync(project.Id);
        var realignment = new PlanRealignmentService(realignmentDb, new FixedClock())
            .CreateRealignmentDraftAsync(project.Id, StageCodes.FS, 2, "revision-actor-b");
        await Task.Delay(100);
        Assert.False(actorDraft.IsCompleted);
        Assert.False(realignment.IsCompleted);
        await hold.CommitAsync();

        var draft = await actorDraft.WaitAsync(TimeSpan.FromSeconds(30));
        await realignment.WaitAsync(TimeSpan.FromSeconds(30));

        seed.ChangeTracker.Clear();
        var versions = await seed.PlanVersions.AsNoTracking()
            .Where(p => p.ProjectId == project.Id)
            .OrderBy(p => p.VersionNo)
            .ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, versions.Select(p => p.VersionNo));
        Assert.Equal(PlanVersionStatus.Draft,
            Assert.Single(versions, p => p.Id == draft.Id).Status);
        Assert.Equal(PlanVersionStatus.PendingApproval,
            Assert.Single(versions, p => p.CreatedByUserId == "revision-actor-b").Status);
        Assert.Equal(3, versions.Select(p => p.VersionNo).Distinct().Count());
    }

    // Regression: reading accepted planning inputs before the project gate can
    // allocate N+1 using dates/policy published before revision N was committed.
    // Hold the gate, start a competing editor draft, publish a new authoritative
    // baseline inside the holder, then prove the created draft uses that baseline.
    private static async Task DraftBaselineFollowsCommittedPublicationAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var seed = new ApplicationDbContext(options);
        var project = new Project
        {
            Name = "Locked draft baseline publication",
            CreatedByUserId = "revision-actor-a",
            LeadPoUserId = "revision-actor-a",
            RowVersion = Array.Empty<byte>(),
            PncApplicable = false,
            PublishedPlanningInputVersion = PlanningInputVersions.Current
        };
        seed.Projects.Add(project);
        await seed.SaveChangesAsync();
        seed.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = project.Id,
            AnchorStart = new DateOnly(2026, 11, 1),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });
        seed.ProjectPlanDurations.Add(new ProjectPlanDuration
        {
            ProjectId = project.Id, StageCode = StageCodes.FS,
            DurationDays = 4, SortOrder = 10
        });
        seed.ProjectStages.Add(new ProjectStage
        {
            ProjectId = project.Id, StageCode = StageCodes.FS,
            Status = StageStatus.NotStarted, SortOrder = 10,
            PlannedStart = new DateOnly(2026, 11, 1),
            PlannedDue = new DateOnly(2026, 11, 4)
        });
        await seed.SaveChangesAsync();

        await using var publisher = new ApplicationDbContext(options);
        await using var heldTransaction = await publisher.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(publisher, project.Id, default);

        await using var actorDb = new ApplicationDbContext(options);
        var draftTask = new PlanDraftService(actorDb, new FixedClock(),
            NullLogger<PlanDraftService>.Instance, new NullAudit(),
            new ActorContext("revision-actor-a"))
            .CreateOrGetDraftAsync(project.Id);
        await Task.Delay(100);
        Assert.False(draftTask.IsCompleted);

        (await publisher.Projects.SingleAsync(p => p.Id == project.Id)).PncApplicable = true;
        var settings = await publisher.ProjectScheduleSettings.SingleAsync(p => p.ProjectId == project.Id);
        settings.AnchorStart = new DateOnly(2026, 12, 1);
        settings.IncludeWeekends = false;
        settings.SkipHolidays = true;
        settings.NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay;
        var duration = await publisher.ProjectPlanDurations.SingleAsync(p =>
            p.ProjectId == project.Id && p.StageCode == StageCodes.FS);
        duration.DurationDays = 9;
        var stage = await publisher.ProjectStages.SingleAsync(p =>
            p.ProjectId == project.Id && p.StageCode == StageCodes.FS);
        stage.PlannedStart = new DateOnly(2026, 12, 1);
        stage.PlannedDue = new DateOnly(2026, 12, 9);
        await publisher.SaveChangesAsync();
        await heldTransaction.CommitAsync();

        var created = await draftTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(created.PncApplicable);
        Assert.Equal(new DateOnly(2026, 12, 1), created.AnchorDate);
        Assert.True(created.SkipWeekends);
        Assert.True(created.SkipHolidays);
        Assert.Equal(PlanTransitionRule.NextWorkingDay, created.TransitionRule);
        var fs = Assert.Single(created.StagePlans, row => row.StageCode == StageCodes.FS);
        Assert.Equal(9, fs.DurationDays);
        Assert.Equal(new DateOnly(2026, 12, 1), fs.PlannedStart);
        Assert.Equal(new DateOnly(2026, 12, 9), fs.PlannedDue);

        seed.ChangeTracker.Clear();
        var stored = await seed.PlanVersions.AsNoTracking()
            .Include(p => p.StagePlans)
            .SingleAsync(p => p.Id == created.Id);
        Assert.Equal(created.AnchorDate, stored.AnchorDate);
        Assert.Equal(9, stored.StagePlans.Single(row => row.StageCode == StageCodes.FS).DurationDays);
    }

    private static async Task<bool> TrySubmitAsync(
        ApplicationDbContext db, int projectId, string actor)
    {
        var service = new PlanApprovalService(db, new FixedClock(),
            NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db),
            new NoNotifications(), new AlwaysValidTimeline());
        try
        {
            await service.SubmitAsync(projectId, actor);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private sealed class ActorContext(string actor) : IUserContext
    {
        public string? UserId => actor;
        public ClaimsPrincipal User => new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, actor) }, "Test"));
    }

    private sealed class NullAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info",
            string? userId = null, string? userName = null,
            IDictionary<string, string?>? data = null, HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class AlwaysValidTimeline : IPlannedTimelineValidationService
    {
        public Task<PlannedTimelineValidationResult> ValidateAsync(
            PlannedTimelineCandidate candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlannedTimelineValidationResult(Array.Empty<string>()));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoNotifications : IPlanNotificationService
    {
        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
