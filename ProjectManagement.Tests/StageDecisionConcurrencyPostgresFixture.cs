using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Phase D-1c relational guarantees for lifecycle decisions, run inside the
/// disposable migrated PostgreSQL database used by the production migration
/// gate. Races are made deterministic by pausing a real decision inside its
/// transaction (at its history save) and waiting until PostgreSQL reports the
/// competing writer blocked on a lock; no step relies on a sleep.
/// </summary>
internal static class StageDecisionConcurrencyPostgresFixture
{
    private const string Seeder = "d1c-seed";
    private const string LeadPo = "d1c-lead-po";
    private const string Hod = "d1c-hod";
    private const string OtherHod = "d1c-other-hod";
    private static readonly DateOnly Today = new(2026, 3, 31);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.AddRange(
                new ApplicationUser { Id = Seeder, UserName = Seeder },
                new ApplicationUser { Id = LeadPo, UserName = LeadPo });
            await seed.SaveChangesAsync();
        }

        await ApprovalVersusApprovalDecidesOnceAsync(options);
        await ApprovalVersusRejectionDecidesOnceAsync(options, approvalFirst: true);
        await ApprovalVersusRejectionDecidesOnceAsync(options, approvalFirst: false);
        await ApprovalRevalidatesAgainstStateCommittedBehindTheGateAsync(options);
        await HistoryFailureRollsBackTheWholeDecisionAsync(options, StageDecisionAction.Approve);
        await HistoryFailureRollsBackTheWholeDecisionAsync(options, StageDecisionAction.Reject);
        await SubmissionCannotSupersedeARequestBeingApprovedAsync(options);
        await ArppSynchronizationCannotSupersedeARequestBeingApprovedAsync(options);
        await DecisionCannotApproveARequestArppHasSupersededAsync(options);
    }

    // SECTION: Scenario A — approval vs approval
    /// <summary>
    /// Two HoDs approve the same pending request. The second waits on the project
    /// gate behind the first, re-reads the request inside the lock and is told it
    /// was already decided: one decision, one history pair, one stage transition.
    /// </summary>
    private static async Task ApprovalVersusApprovalDecidesOnceAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, requestId) = await SeedIpaStartRequestAsync(options, "D-1c approve vs approve");

        await using var pause = new PauseAtDecisionHistory();
        await using var first = Context(options, pause);
        var winner = Decide(first, requestId, StageDecisionAction.Approve, Hod);
        await pause.Reached.WaitAsync(Timeout);

        await using var second = Context(options);
        var loser = Decide(second, requestId, StageDecisionAction.Approve, OtherHod);
        await WaitUntilBlockedOnALockAsync(options, loser);
        pause.Release();

        Assert.Equal(StageDecisionOutcome.Success, (await winner.WaitAsync(Timeout)).Outcome);
        Assert.Equal(StageDecisionOutcome.AlreadyDecided, (await loser.WaitAsync(Timeout)).Outcome);

        var request = await RequestAsync(options, requestId);
        Assert.Equal("Approved", request.DecisionStatus);
        Assert.Equal(Hod, request.DecidedByUserId);
        Assert.Equal(StageStatus.InProgress, (await IpaAsync(options, projectId)).Status);
        Assert.Equal(new[] { "Approved", "Applied" }, await HistoryAsync(options, projectId));
    }

    // SECTION: Scenario B — approval vs rejection
    /// <summary>
    /// An approval and a rejection race for the same request. Whichever holds the
    /// gate first decides; the other is refused, and the stored request, stage and
    /// history all describe only the winner.
    /// </summary>
    private static async Task ApprovalVersusRejectionDecidesOnceAsync(
        DbContextOptions<ApplicationDbContext> options,
        bool approvalFirst)
    {
        var (projectId, requestId) = await SeedIpaStartRequestAsync(
            options, approvalFirst ? "D-1c approve then reject" : "D-1c reject then approve");
        var firstAction = approvalFirst ? StageDecisionAction.Approve : StageDecisionAction.Reject;
        var secondAction = approvalFirst ? StageDecisionAction.Reject : StageDecisionAction.Approve;

        await using var pause = new PauseAtDecisionHistory();
        await using var first = Context(options, pause);
        var winner = Decide(first, requestId, firstAction, Hod);
        await pause.Reached.WaitAsync(Timeout);

        await using var second = Context(options);
        var loser = Decide(second, requestId, secondAction, OtherHod);
        await WaitUntilBlockedOnALockAsync(options, loser);
        pause.Release();

        Assert.Equal(StageDecisionOutcome.Success, (await winner.WaitAsync(Timeout)).Outcome);
        Assert.Equal(StageDecisionOutcome.AlreadyDecided, (await loser.WaitAsync(Timeout)).Outcome);

        var request = await RequestAsync(options, requestId);
        var ipa = await IpaAsync(options, projectId);
        if (approvalFirst)
        {
            Assert.Equal("Approved", request.DecisionStatus);
            Assert.Equal(StageStatus.InProgress, ipa.Status);
            Assert.Equal(new[] { "Approved", "Applied" }, await HistoryAsync(options, projectId));
        }
        else
        {
            Assert.Equal("Rejected", request.DecisionStatus);
            Assert.Equal(StageStatus.NotStarted, ipa.Status);
            Assert.Null(ipa.ActualStart);
            Assert.Equal(new[] { "Rejected" }, await HistoryAsync(options, projectId));
        }

        Assert.Equal(Hod, request.DecidedByUserId);
    }

    // SECTION: Scenario C — protected revalidation
    /// <summary>
    /// A gate holder reopens the predecessor after the HoD opened the queue. The
    /// approval waits on the gate, re-validates against the committed state and is
    /// refused; before D-1c it validated before the gate and would have published.
    /// </summary>
    private static async Task ApprovalRevalidatesAgainstStateCommittedBehindTheGateAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, requestId) = await SeedIpaStartRequestAsync(options, "D-1c protected revalidation");

        await using var holder = new ApplicationDbContext(options);
        await using var hold = await holder.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(holder, projectId, default);
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectStages\" SET \"Status\" = 'InProgress', \"CompletedOn\" = NULL WHERE \"ProjectId\" = {projectId} AND \"StageCode\" = {StageCodes.FS}");

        await using var contender = Context(options);
        var decision = Decide(contender, requestId, StageDecisionAction.Approve, Hod);
        await WaitUntilBlockedOnALockAsync(options, decision);
        await hold.CommitAsync();

        var result = await decision.WaitAsync(Timeout);
        Assert.Equal(StageDecisionOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(StageCodes.FS, result.Error);
        Assert.Equal("Pending", (await RequestAsync(options, requestId)).DecisionStatus);
        Assert.Equal(StageStatus.NotStarted, (await IpaAsync(options, projectId)).Status);
        Assert.Empty(await HistoryAsync(options, projectId));
    }

    // SECTION: Scenario D — rollback
    /// <summary>
    /// The claim, the stage transition and the mandatory history commit together:
    /// a failure saving history leaves the request pending and the stage untouched.
    /// </summary>
    private static async Task HistoryFailureRollsBackTheWholeDecisionAsync(
        DbContextOptions<ApplicationDbContext> options,
        StageDecisionAction action)
    {
        var (projectId, requestId) = await SeedIpaStartRequestAsync(options, $"D-1c rollback {action}");

        await using var db = Context(options, new FailStageHistoryInterceptor());
        await Assert.ThrowsAsync<DbUpdateException>(() => Decide(db, requestId, action, Hod));

        var request = await RequestAsync(options, requestId);
        Assert.Equal("Pending", request.DecisionStatus);
        Assert.Null(request.DecidedByUserId);
        var ipa = await IpaAsync(options, projectId);
        Assert.Equal(StageStatus.NotStarted, ipa.Status);
        Assert.Null(ipa.ActualStart);
        Assert.Empty(await HistoryAsync(options, projectId));
    }

    // SECTION: Writers that supersede pending requests
    /// <summary>
    /// A project officer revises the same stage while an HoD is approving the
    /// pending request. Submission takes the gate before reading pending requests,
    /// so it waits, then sees the request as approved instead of overwriting the
    /// approval as Superseded.
    /// </summary>
    private static async Task SubmissionCannotSupersedeARequestBeingApprovedAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, requestId) = await SeedIpaStartRequestAsync(options, "D-1c submission vs approval");

        await using var pause = new PauseAtDecisionHistory();
        await using var decider = Context(options, pause);
        var decision = Decide(decider, requestId, StageDecisionAction.Approve, Hod);
        await pause.Reached.WaitAsync(Timeout);

        await using var submitter = Context(options);
        var policy = StageWorkflowTestFactory.CreatePolicy(submitter);
        var clock = FakeClock.ForIstDate(Today);
        var submission = new StageRequestService(
                submitter, clock, new StageValidationService(submitter, clock, policy), policy,
                arppIpaStageAuthority: new StubArppAuthority(null))
            .CreateAsync(
                new StageChangeRequestInput
                {
                    ProjectId = projectId,
                    StageCode = StageCodes.IPA,
                    RequestedStatus = StageStatus.InProgress.ToString(),
                    RequestedDate = Jan(13)
                },
                LeadPo);
        await WaitUntilBlockedOnALockAsync(options, submission);
        pause.Release();

        Assert.Equal(StageDecisionOutcome.Success, (await decision.WaitAsync(Timeout)).Outcome);
        await submission.WaitAsync(Timeout);

        var request = await RequestAsync(options, requestId);
        Assert.Equal("Approved", request.DecisionStatus);
        Assert.Equal(Hod, request.DecidedByUserId);
        Assert.DoesNotContain("Superseded", await HistoryAsync(options, projectId));
        Assert.Equal(StageStatus.InProgress, (await IpaAsync(options, projectId)).Status);
    }

    /// <summary>
    /// ARPP synchronization does not take the project gate. While an approval holds
    /// the IPA stage row, synchronization waits on that row, then supersedes the
    /// request only if it is still pending — it no longer overwrites the approval.
    /// </summary>
    private static async Task ArppSynchronizationCannotSupersedeARequestBeingApprovedAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, requestId) = await SeedIpaStartRequestAsync(options, "D-1c ARPP vs approval");

        await using var pause = new PauseAtDecisionHistory();
        await using var decider = Context(options, pause);
        var decision = Decide(decider, requestId, StageDecisionAction.Approve, Hod);
        await pause.Reached.WaitAsync(Timeout);

        await using var synchronizerDb = Context(options);
        var synchronization = Synchronizer(synchronizerDb, projectId).SynchronizeProjectsAsync(new[] { projectId });
        await WaitUntilBlockedOnALockAsync(options, synchronization);
        pause.Release();

        Assert.Equal(StageDecisionOutcome.Success, (await decision.WaitAsync(Timeout)).Outcome);
        var synchronized = await synchronization.WaitAsync(Timeout);

        Assert.Equal(0, synchronized.SupersededRequestCount);
        var request = await RequestAsync(options, requestId);
        Assert.Equal("Approved", request.DecisionStatus);
        Assert.Equal(new[] { "Approved", "Applied" }, await HistoryAsync(options, projectId));
        var ipa = await IpaAsync(options, projectId);
        Assert.Equal(StageStatus.Completed, ipa.Status);
        Assert.Equal(ArppIssueDate, ipa.CompletedOn);
    }

    /// <summary>
    /// The reverse order: synchronization has superseded the request (uncommitted,
    /// holding the stage row). The approval waits on the stage row, re-reads the
    /// request and reports it already decided; nothing is approved.
    /// </summary>
    private static async Task DecisionCannotApproveARequestArppHasSupersededAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, requestId) = await SeedIpaStartRequestAsync(options, "D-1c approval vs ARPP");

        await using var synchronizerDb = Context(options);
        await using var outer = await RelationalTransactionScope.CreateAsync(synchronizerDb.Database);
        var synchronized = await Synchronizer(synchronizerDb, projectId).SynchronizeProjectsAsync(new[] { projectId });
        Assert.Equal(1, synchronized.SupersededRequestCount);

        await using var decider = Context(options);
        var decision = Decide(decider, requestId, StageDecisionAction.Approve, Hod);
        await WaitUntilBlockedOnALockAsync(options, decision);
        await outer.CommitAsync();

        Assert.Equal(StageDecisionOutcome.AlreadyDecided, (await decision.WaitAsync(Timeout)).Outcome);
        var request = await RequestAsync(options, requestId);
        Assert.Equal("Superseded", request.DecisionStatus);
        Assert.Equal(new[] { "Superseded" }, await HistoryAsync(options, projectId));
        Assert.Equal(StageStatus.Completed, (await IpaAsync(options, projectId)).Status);
    }

    // SECTION: Helpers
    private static readonly DateOnly ArppIssueDate = new(2026, 2, 1);

    private static Task<StageDecisionResult> Decide(
        ApplicationDbContext db,
        int requestId,
        StageDecisionAction action,
        string userId)
    {
        var clock = FakeClock.ForIstDate(Today);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var arpp = new StubArppAuthority(null);
        var progress = new StageProgressService(
            db, clock, new RecordingAudit(), new ProjectFactsReadService(db), new NoNotifications(), policy,
            arppIpaStageAuthority: arpp);
        var service = new StageDecisionService(
            db, clock, progress, NullLogger<StageDecisionService>.Instance, policy,
            arppIpaStageAuthority: arpp);

        // Task.Run so a decision paused inside its transaction never blocks the test.
        return Task.Run(() => service.DecideAsync(
            new StageDecisionInput(requestId, action, null), userId, isAdmin: false, isHoD: true));
    }

    private static ArppIpaStageSynchronizer Synchronizer(ApplicationDbContext db, int projectId) =>
        new(db,
            new StubArppAuthority(new ArppIpaStageAuthority(
                projectId, 2025, 1, ArppIssueKind.Original, 1, "ARPP 2025-26", ArppIssueDate, 1, "1", null)),
            FakeClock.ForIstDate(Today));

    private static ApplicationDbContext Context(DbContextOptions<ApplicationDbContext> options, IInterceptor? interceptor = null)
    {
        if (interceptor is null)
        {
            return new ApplicationDbContext(options);
        }

        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(interceptor).Options);
    }

    /// <summary>FS completed, IPA not started, and a pending request to start IPA.</summary>
    private static async Task<(int ProjectId, int RequestId)> SeedIpaStartRequestAsync(
        DbContextOptions<ApplicationDbContext> options,
        string name)
    {
        await using var db = new ApplicationDbContext(options);
        var project = new Project
        {
            Name = name,
            CreatedByUserId = Seeder,
            LeadPoUserId = LeadPo,
            WorkflowVersion = ProcurementWorkflow.VersionV1,
            RowVersion = Array.Empty<byte>()
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = project.Id, StageCode = StageCodes.FS, SortOrder = 1,
                Status = StageStatus.Completed, ActualStart = Jan(1), CompletedOn = Jan(10)
            },
            new ProjectStage
            {
                ProjectId = project.Id, StageCode = StageCodes.IPA, SortOrder = 2, Status = StageStatus.NotStarted
            });
        var request = new StageChangeRequest
        {
            ProjectId = project.Id,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.InProgress.ToString(),
            RequestedDate = Jan(12),
            RequestedByUserId = LeadPo,
            RequestedOn = DateTimeOffset.UtcNow,
            DecisionStatus = "Pending"
        };
        db.StageChangeRequests.Add(request);
        await db.SaveChangesAsync();
        return (project.Id, request.Id);
    }

    /// <summary>
    /// Deterministic synchronization point: returns once PostgreSQL reports a
    /// session waiting on a lock. Fails if the writer finishes without waiting.
    /// </summary>
    private static async Task WaitUntilBlockedOnALockAsync(DbContextOptions<ApplicationDbContext> options, Task writer)
    {
        await using var probe = new ApplicationDbContext(options);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (writer.IsCompleted)
            {
                await writer; // surface its exception, if any
                Assert.Fail("The writer completed without waiting for a lock.");
            }

            var waiting = await probe.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity " +
                    "WHERE datname = current_database() AND wait_event_type = 'Lock'")
                .SingleAsync();
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The writer never waited on a lock.");
    }

    private static async Task<StageChangeRequest> RequestAsync(DbContextOptions<ApplicationDbContext> options, int requestId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.StageChangeRequests.AsNoTracking().SingleAsync(item => item.Id == requestId);
    }

    private static async Task<ProjectStage> IpaAsync(DbContextOptions<ApplicationDbContext> options, int projectId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.ProjectStages.AsNoTracking()
            .SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.IPA);
    }

    private static async Task<string[]> HistoryAsync(DbContextOptions<ApplicationDbContext> options, int projectId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.StageChangeLogs.AsNoTracking()
            .Where(log => log.ProjectId == projectId)
            .OrderBy(log => log.Id)
            .Select(log => log.Action)
            .ToArrayAsync();
    }

    private static DateOnly Jan(int day) => new(2026, 1, day);

    /// <summary>
    /// Holds a decision inside its transaction at the save that carries its
    /// decision history — after the gate, the stage row locks and the claim.
    /// </summary>
    private sealed class PauseAtDecisionHistory : SaveChangesInterceptor, IAsyncDisposable
    {
        private readonly SemaphoreSlim _reached = new(0);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;

        public SemaphoreSlim Reached => _reached;

        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var decisionHistory = eventData.Context?.ChangeTracker.Entries<StageChangeLog>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Action is "Approved" or "Rejected") == true;
            if (decisionHistory && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                _reached.Release();
                await _release.Task.WaitAsync(Timeout, cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            Release();
            _reached.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailStageHistoryInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StageChangeLog>().Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new DbUpdateException("Injected stage-history failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class NoNotifications : IStageNotificationService
    {
        public Task NotifyStageStatusChangedAsync(
            ProjectStage stage, Project project, StageStatus previousStatus, string actorUserId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubArppAuthority(ArppIpaStageAuthority? authority) : IArppIpaStageAuthorityService
    {
        public Task<ArppIpaStageAuthority?> ResolveAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(authority);

        public Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ArppIpaStageAuthority>>(
                authority is null
                    ? new Dictionary<int, ArppIpaStageAuthority>()
                    : new Dictionary<int, ArppIpaStageAuthority> { [authority.ProjectId] = authority });

        public Task<bool> IsManagedAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(authority is not null);

        public Task EnsureManualLifecycleMutationAllowedAsync(int projectId, string stageCode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
