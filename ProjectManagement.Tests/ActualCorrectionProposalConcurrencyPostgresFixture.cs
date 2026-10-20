using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Phase D-3a/b relational guarantees for Actual correction proposal submission,
/// run inside the disposable migrated PostgreSQL database used by the production
/// migration gate: the project gate serializes competing submissions, the
/// project-row and stage-row locks order submission after writers that do not
/// take the gate, and a failed supersession rolls back completely.
/// </summary>
internal static class ActualCorrectionProposalConcurrencyPostgresFixture
{
    private const string Lead = "d3-proposal-lead-po";
    private const string NewLead = "d3-proposal-new-lead-po";
    private static readonly DateOnly Today = new(2026, 3, 31);

    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.AddRange(
                new ApplicationUser { Id = Lead, UserName = Lead },
                new ApplicationUser { Id = NewLead, UserName = NewLead });
            await seed.SaveChangesAsync();
        }

        await ConcurrentFirstSubmissionsCreateOneRevisionAsync(options);
        await ConcurrentSuccessorsSupersedeOnceAsync(options);
        await ReassignmentCommittedDuringSubmissionForbidsItAsync(options);
        await DeletionCommittedDuringSubmissionRefusesItAsync(options);
        await DirectPublicationCommittedBehindTheGateMakesTheSubmissionStaleAsync(options);
        await WriterOutsideTheGateIsSeenThroughTheStageRowLockAsync(options);
        await FailedForwardLinkRollsBackTheWholeSupersessionAsync(options);
    }

    /// <summary>
    /// R8/S1–S2: two tabs both rendered with no pending revision submit while the
    /// gate is held. They run one after the other; the second sees the first's
    /// revision and is stale, so exactly one revision exists.
    /// </summary>
    private static async Task ConcurrentFirstSubmissionsCreateOneRevisionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-3 first submissions", InProgress(StageCodes.FS, Jan(5)));
        var first = await EditorAsync(options, projectId, None, StageCodes.FS, Jan(4));
        var second = await EditorAsync(options, projectId, None, StageCodes.FS, Jan(3));

        var results = await RunBehindHeldGateAsync(options, projectId, first, second);

        Assert.Single(results, result => result.Outcome == ActualCorrectionSubmissionOutcome.Submitted);
        var stale = Assert.Single(results, result => result.Outcome == ActualCorrectionSubmissionOutcome.Stale);
        Assert.Equal(ActualCorrectionStaleReason.PendingRevisionChanged, stale.StaleReason);
        var revision = Assert.Single(await RevisionsAsync(options, projectId));
        Assert.Equal(1, revision.RevisionNo);
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, revision.Status);
        await AssertNoOfficialChangeAsync(options, projectId, StageCodes.FS, Jan(5));
    }

    /// <summary>
    /// R8: two successors based on the same pending revision. One supersedes it
    /// with both lineage links; the other is stale. Revision numbers stay unique
    /// and only one revision is Pending.
    /// </summary>
    private static async Task ConcurrentSuccessorsSupersedeOnceAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-3 successors", InProgress(StageCodes.FS, Jan(5)));
        var original = await SubmitAsync(options, await EditorAsync(options, projectId, None, StageCodes.FS, Jan(4)), Lead);
        var basedOnOriginal = Based(original.Revision!);
        var first = await EditorAsync(options, projectId, basedOnOriginal, StageCodes.FS, Jan(3));
        var second = await EditorAsync(options, projectId, basedOnOriginal, StageCodes.FS, Jan(2));

        var results = await RunBehindHeldGateAsync(options, projectId, first, second);

        var winner = Assert.Single(results, result => result.Outcome == ActualCorrectionSubmissionOutcome.Submitted);
        Assert.Single(results, result => result.Outcome == ActualCorrectionSubmissionOutcome.Stale);
        var revisions = await RevisionsAsync(options, projectId);
        Assert.Equal(new[] { 1, 2 }, revisions.Select(revision => revision.RevisionNo).ToArray());
        Assert.Single(revisions, revision => revision.Status == ActualCorrectionRevisionStatus.Pending);
        Assert.Equal(ActualCorrectionRevisionStatus.Superseded, revisions[0].Status);
        Assert.Equal(winner.Revision!.Id, revisions[0].SupersededByRevisionId);
        Assert.Equal(revisions[0].Id, revisions[1].SupersedesRevisionId);
        Assert.Equal(Lead, revisions[0].ClosedByUserId);
    }

    /// <summary>
    /// S5: lead-PO reassignment updates the project row without the gate. A
    /// submission past the gate waits on the project-row share lock and is then
    /// refused against the committed assignment.
    /// </summary>
    private static async Task ReassignmentCommittedDuringSubmissionForbidsItAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-3 reassignment race", InProgress(StageCodes.FS, Jan(5)));
        var submission = await EditorAsync(options, projectId, None, StageCodes.FS, Jan(4));

        await using var assigner = new ApplicationDbContext(options);
        await using var assignment = await assigner.Database.BeginTransactionAsync();
        await assigner.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Projects\" SET \"LeadPoUserId\" = {NewLead} WHERE \"Id\" = {projectId}");

        var pending = SubmitAsync(options, submission, Lead);
        await WaitUntilLockWaitersAsync(options, 1, pending);
        await assignment.CommitAsync();

        Assert.Equal(ActualCorrectionSubmissionOutcome.Forbidden, (await pending.WaitAsync(TimeSpan.FromSeconds(30))).Outcome);
        Assert.Empty(await RevisionsAsync(options, projectId));
    }

    /// <summary>Moving the project to Trash during submission refuses it with no proposal written.</summary>
    private static async Task DeletionCommittedDuringSubmissionRefusesItAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-3 deletion race", InProgress(StageCodes.FS, Jan(5)));
        var submission = await EditorAsync(options, projectId, None, StageCodes.FS, Jan(4));

        await using var moderator = new ApplicationDbContext(options);
        await using var deletion = await moderator.Database.BeginTransactionAsync();
        await moderator.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Projects\" SET \"IsDeleted\" = TRUE WHERE \"Id\" = {projectId}");

        var pending = SubmitAsync(options, submission, Lead);
        await WaitUntilLockWaitersAsync(options, 1, pending);
        await deletion.CommitAsync();

        Assert.Equal(ActualCorrectionSubmissionOutcome.NotFound, (await pending.WaitAsync(TimeSpan.FromSeconds(30))).Outcome);
        Assert.Empty(await RevisionsAsync(options, projectId));
    }

    /// <summary>
    /// R12/S3: a direct HoD/Admin publication of stage X holds the gate and
    /// commits while the PO's submission touching X waits on it. The submission
    /// sees X's new official facts, fails its token check and writes nothing.
    /// </summary>
    private static async Task DirectPublicationCommittedBehindTheGateMakesTheSubmissionStaleAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-3 direct publication race",
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(14)));
        var submission = await EditorAsync(options, projectId, None, StageCodes.IPA, Jan(11));

        await using var publisher = new ApplicationDbContext(options);
        await using var publication = await publisher.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(publisher, projectId, default);
        await publisher.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectStages\" SET \"ActualStart\" = {Jan(13)} WHERE \"ProjectId\" = {projectId} AND \"StageCode\" = {StageCodes.IPA}");

        var pending = SubmitAsync(options, submission, Lead);
        await WaitUntilLockWaitersAsync(options, 1, pending);
        await publication.CommitAsync();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, result.Outcome);
        Assert.Equal(ActualCorrectionStaleReason.StagesChanged, result.StaleReason);
        Assert.Equal(new[] { StageCodes.IPA }, result.StageCodes);
        Assert.Empty(await RevisionsAsync(options, projectId));
    }

    /// <summary>
    /// R5 at submission: ARPP synchronization changes the IPA row without the gate.
    /// The submission's FOR UPDATE stage load waits for it, so the Before* snapshot
    /// and the token check use the committed ARPP state instead of a stale read.
    /// </summary>
    private static async Task WriterOutsideTheGateIsSeenThroughTheStageRowLockAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-3 ARPP row-lock race",
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var submission = await EditorAsync(options, projectId, None, StageCodes.IPA, Jan(11));

        await using var synchronizer = new ApplicationDbContext(options);
        await using var sync = await synchronizer.Database.BeginTransactionAsync();
        await synchronizer.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectStages\" SET \"Status\" = 'Completed', \"CompletedOn\" = {Jan(20)} WHERE \"ProjectId\" = {projectId} AND \"StageCode\" = {StageCodes.IPA}");

        var pending = SubmitAsync(options, submission, Lead);
        await WaitUntilLockWaitersAsync(options, 1, pending);
        await sync.CommitAsync();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, result.Outcome);
        Assert.Equal(new[] { StageCodes.IPA }, result.StageCodes);
        Assert.Empty(await RevisionsAsync(options, projectId));
    }

    /// <summary>
    /// C.4 step 9 on PostgreSQL: the previous revision is closed and the successor
    /// inserted, then the forward-link update fails. Everything rolls back: the
    /// previous revision is Pending with no closure, and no successor or items remain.
    /// </summary>
    private static async Task FailedForwardLinkRollsBackTheWholeSupersessionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-3 supersession rollback", InProgress(StageCodes.FS, Jan(5)));
        var original = await SubmitAsync(options, await EditorAsync(options, projectId, None, StageCodes.FS, Jan(4)), Lead);
        var successor = await EditorAsync(options, projectId, Based(original.Revision!), StageCodes.FS, Jan(3));

        var failing = new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .AddInterceptors(new FailForwardLinkInterceptor())
            .Options;
        await using (var db = new ApplicationDbContext(failing))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ActualCorrectionProposalService(db, FakeClock.ForIstDate(Today)).SubmitAsync(successor, TestActors.ProjectOfficer(Lead)));
            Assert.Empty(db.ChangeTracker.Entries<ActualCorrectionRevision>());
        }

        var revision = Assert.Single(await RevisionsAsync(options, projectId));
        Assert.Equal(original.Revision!.Id, revision.Id);
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, revision.Status);
        Assert.Null(revision.ClosedOn);
        Assert.Null(revision.ClosedByUserId);
        Assert.Null(revision.SupersededByRevisionId);
        Assert.Single(revision.Items);

        // The rolled-back successor number is reused, so numbering stays gap-free.
        var retried = await SubmitAsync(options, successor, Lead);
        Assert.Equal(2, retried.Revision!.RevisionNo);
    }

    // SECTION: Helpers
    private static readonly ActualCorrectionBaseline None = new(ActualStageToken.BaselineVersion);

    private static ActualCorrectionBaseline Based(ActualCorrectionRevisionIdentity revision) =>
        new(ActualStageToken.BaselineVersion, revision.Id, revision.SubmittedOn);

    /// <summary>
    /// Holds the project gate, starts every submission on its own connection, waits
    /// until PostgreSQL reports all of them waiting on a lock, then releases the gate.
    /// </summary>
    private static async Task<ActualCorrectionSubmissionResult[]> RunBehindHeldGateAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        params ActualCorrectionSubmission[] submissions)
    {
        await using var holder = new ApplicationDbContext(options);
        await using var hold = await holder.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(holder, projectId, default);

        var running = submissions.Select(submission => SubmitAsync(options, submission, Lead)).ToArray();
        await WaitUntilLockWaitersAsync(options, running.Length, running);
        await hold.CommitAsync();

        return await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<ActualCorrectionSubmissionResult> SubmitAsync(
        DbContextOptions<ApplicationDbContext> options,
        ActualCorrectionSubmission submission,
        string userId)
    {
        await using var db = new ApplicationDbContext(options);
        return await new ActualCorrectionProposalService(db, FakeClock.ForIstDate(Today))
            .SubmitAsync(submission, TestActors.ProjectOfficer(userId));
    }

    /// <summary>
    /// Deterministic synchronization point: returns once PostgreSQL reports the
    /// expected number of sessions waiting on a lock. Fails if a submission
    /// finishes first, which would mean it never waited.
    /// </summary>
    private static async Task WaitUntilLockWaitersAsync(
        DbContextOptions<ApplicationDbContext> options,
        int expected,
        params Task[] submissions)
    {
        await using var probe = new ApplicationDbContext(options);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            Assert.False(submissions.Any(submission => submission.IsCompleted), "A submission completed without waiting for the lock.");

            var waiting = await probe.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity " +
                    "WHERE datname = current_database() AND wait_event_type = 'Lock'")
                .SingleAsync();
            if (waiting >= expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The submissions never waited on a lock.");
    }

    private static async Task<ActualCorrectionSubmission> EditorAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        ActualCorrectionBaseline baseline,
        string stageCode,
        DateOnly actualStart)
    {
        await using var db = new ApplicationDbContext(options);
        var stage = await db.ProjectStages.AsNoTracking()
            .SingleAsync(item => item.ProjectId == projectId && item.StageCode == stageCode);
        return new ActualCorrectionSubmission(
            projectId,
            ActualChangeEntryPoint.Editor,
            new[] { new ActualCorrectionProposalRow(stageCode, actualStart, StageToken: ActualStageToken.Compute(stage)) },
            baseline);
    }

    private static async Task<int> SeedProjectAsync(
        DbContextOptions<ApplicationDbContext> options,
        string name,
        params ProjectStage[] stages)
    {
        await using var db = new ApplicationDbContext(options);
        var project = new Project
        {
            Name = name,
            CreatedByUserId = Lead,
            LeadPoUserId = Lead,
            WorkflowVersion = ProcurementWorkflow.VersionV1,
            RowVersion = Array.Empty<byte>()
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        foreach (var stage in stages)
        {
            stage.ProjectId = project.Id;
        }

        db.ProjectStages.AddRange(stages);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<List<ActualCorrectionRevision>> RevisionsAsync(DbContextOptions<ApplicationDbContext> options, int projectId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.ActualCorrectionRevisions.AsNoTracking()
            .Include(revision => revision.Items)
            .Where(revision => revision.ProjectId == projectId)
            .OrderBy(revision => revision.RevisionNo)
            .ToListAsync();
    }

    private static async Task AssertNoOfficialChangeAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        string stageCode,
        DateOnly start)
    {
        await using var db = new ApplicationDbContext(options);
        var stage = await db.ProjectStages.AsNoTracking()
            .SingleAsync(item => item.ProjectId == projectId && item.StageCode == stageCode);
        Assert.Equal(start, stage.ActualStart);
        Assert.False(await db.StageChangeLogs.AnyAsync(log => log.ProjectId == projectId));
    }

    private static ProjectStage InProgress(string code, DateOnly start) =>
        new() { StageCode = code, Status = StageStatus.InProgress, ActualStart = start };

    private static ProjectStage Completed(string code, DateOnly start, DateOnly completed) =>
        new() { StageCode = code, Status = StageStatus.Completed, ActualStart = start, CompletedOn = completed };

    private static DateOnly Jan(int day) => new(2026, 1, day);

    /// <summary>Fails the third supersession statement, after the closure and the insert ran.</summary>
    private sealed class FailForwardLinkInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"SupersededByRevisionId\" = ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Injected forward-link failure.");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
