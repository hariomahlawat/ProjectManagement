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
/// Phase D-3d relational guarantees for HoD/Admin decisions on Actual correction
/// revisions, on the migrated PostgreSQL database of the production migration gate:
/// the first decision wins, a competing writer committed first makes the approval
/// refuse rather than publish over it, and a failed publication rolls back the
/// facts, the history and the claim together.
/// </summary>
internal static class ActualCorrectionDecisionConcurrencyPostgresFixture
{
    private const string Lead = "d3d-lead-po";
    private const string ReviewerA = "d3d-reviewer-a";
    private const string ReviewerB = "d3d-reviewer-b";
    private static readonly DateOnly Today = new(2026, 3, 31);

    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.AddRange(
                new ApplicationUser { Id = Lead, UserName = Lead },
                new ApplicationUser { Id = ReviewerA, UserName = ReviewerA },
                new ApplicationUser { Id = ReviewerB, UserName = ReviewerB });
            await seed.SaveChangesAsync();
        }

        await TwoApprovalsPublishOnceAsync(options);
        await ApprovalAgainstRejectionLeavesOneTerminalStateAsync(options);
        await ApprovalAgainstWithdrawalLeavesOneTerminalStateAsync(options);
        await ApprovalAgainstSuccessorSubmissionNeverDecidesASupersededRevisionAsync(options);
        await DirectCorrectionCommittedFirstMakesApprovalSourceChangedAsync(options);
        await ContradictoryLifecycleRequestCommittedFirstBlocksApprovalAsync(options);
        await ArppWriteOutsideTheGateIsSeenThroughTheStageRowLockAsync(options);
        await HistoryFailureRollsBackFactsAndClaimAsync(options);
        await ClaimFailureRollsBackBeforeAnyPublicationAsync(options);
    }

    /// <summary>R1: two reviewers approve the same revision; one publishes, the other is not actionable.</summary>
    private static async Task TwoApprovalsPublishOnceAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d double approval");

        var results = new ActualCorrectionDecisionResult[2];
        await RunBehindHeldGateAsync(options, projectId,
            async () => results[0] = await ApproveAsync(options, projectId, revision, ReviewerA),
            async () => results[1] = await ApproveAsync(options, projectId, revision, ReviewerB));

        Assert.Single(results, result => result.Outcome == ActualCorrectionDecisionOutcome.Approved);
        Assert.Single(results, result => result.Outcome == ActualCorrectionDecisionOutcome.NotActionable);
        await AssertPublishedOnceAsync(options, projectId);
    }

    /// <summary>R3 analogue: approval and rejection race; exactly one terminal state, consistent facts.</summary>
    private static async Task ApprovalAgainstRejectionLeavesOneTerminalStateAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d approve vs reject");

        var results = new ActualCorrectionDecisionResult[2];
        await RunBehindHeldGateAsync(options, projectId,
            async () => results[0] = await ApproveAsync(options, projectId, revision, ReviewerA),
            async () =>
            {
                await using var db = new ApplicationDbContext(options);
                results[1] = await NewService(db).RejectAsync(Decision(projectId, revision, "No"), TestActors.Admin(ReviewerB));
            });

        Assert.Single(results, result => result.Outcome == ActualCorrectionDecisionOutcome.NotActionable);
        await AssertTerminalStateMatchesFactsAsync(options, projectId, results);
    }

    /// <summary>R3: the PO withdraws while a reviewer approves; one terminal state, consistent facts.</summary>
    private static async Task ApprovalAgainstWithdrawalLeavesOneTerminalStateAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d approve vs withdraw");

        var approval = default(ActualCorrectionDecisionResult);
        var withdrawal = default(ActualCorrectionWithdrawalResult);
        await RunBehindHeldGateAsync(options, projectId,
            async () => approval = await ApproveAsync(options, projectId, revision, ReviewerA),
            async () =>
            {
                await using var db = new ApplicationDbContext(options);
                withdrawal = await NewService(db).WithdrawAsync(projectId, revision.Id, revision.SubmittedOn, TestActors.ProjectOfficer(Lead));
            });

        var approved = approval!.Outcome == ActualCorrectionDecisionOutcome.Approved;
        var withdrawn = withdrawal!.Outcome == ActualCorrectionWithdrawalOutcome.Withdrawn;
        Assert.True(approved ^ withdrawn, "Exactly one of approval and withdrawal must take effect.");
        await using var verify = new ApplicationDbContext(options);
        var status = (await verify.ActualCorrectionRevisions.SingleAsync(item => item.ProjectId == projectId)).Status;
        Assert.Equal(approved ? ActualCorrectionRevisionStatus.Approved : ActualCorrectionRevisionStatus.Withdrawn, status);
        Assert.Equal(approved ? 1 : 0, await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId));
    }

    /// <summary>
    /// R2: the PO submits revision N+1 while a reviewer approves N. Either N is approved and the
    /// successor (based on N) is stale, or N is superseded and the approval is not actionable.
    /// </summary>
    private static async Task ApprovalAgainstSuccessorSubmissionNeverDecidesASupersededRevisionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d approve vs successor");
        var successor = await EditorSubmissionAsync(options, projectId, revision, Jan(10));

        var approval = default(ActualCorrectionDecisionResult);
        var submission = default(ActualCorrectionSubmissionResult);
        await RunBehindHeldGateAsync(options, projectId,
            async () => approval = await ApproveAsync(options, projectId, revision, ReviewerA),
            async () =>
            {
                await using var db = new ApplicationDbContext(options);
                submission = await NewService(db).SubmitAsync(successor, TestActors.ProjectOfficer(Lead));
            });

        await using var verify = new ApplicationDbContext(options);
        var revisions = await verify.ActualCorrectionRevisions.AsNoTracking()
            .Where(item => item.ProjectId == projectId).OrderBy(item => item.RevisionNo).ToListAsync();
        if (approval!.Outcome == ActualCorrectionDecisionOutcome.Approved)
        {
            Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, submission!.Outcome);
            Assert.Equal(ActualCorrectionRevisionStatus.Approved, Assert.Single(revisions).Status);
            Assert.Equal(1, await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId));
        }
        else
        {
            Assert.Equal(ActualCorrectionDecisionOutcome.NotActionable, approval.Outcome);
            Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, submission!.Outcome);
            Assert.Equal(new[] { ActualCorrectionRevisionStatus.Superseded, ActualCorrectionRevisionStatus.Pending },
                revisions.Select(item => item.Status).ToArray());
            Assert.Equal(0, await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId));
        }
    }

    /// <summary>R4: a direct correction of the item's stage commits behind the gate first; approval is SourceChanged.</summary>
    private static async Task DirectCorrectionCommittedFirstMakesApprovalSourceChangedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d approve vs direct");

        await using var publisher = new ApplicationDbContext(options);
        await using var publication = await publisher.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(publisher, projectId, default);
        await publisher.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectStages\" SET \"ActualStart\" = {Jan(13)} WHERE \"ProjectId\" = {projectId} AND \"StageCode\" = {StageCodes.IPA}");

        var pending = ApproveAsync(options, projectId, revision, ReviewerA);
        await WaitUntilLockWaitersAsync(options, 1, pending);
        await publication.CommitAsync();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ActualCorrectionDecisionOutcome.SourceChanged, result.Outcome);
        await AssertUnpublishedAndPendingAsync(options, projectId, Jan(13));
    }

    /// <summary>R14: a contradictory lifecycle request (reopen) commits behind the gate first; approval refuses.</summary>
    private static async Task ContradictoryLifecycleRequestCommittedFirstBlocksApprovalAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d approve vs lifecycle");

        await using var requester = new ApplicationDbContext(options);
        await using var request = await requester.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(requester, projectId, default);
        requester.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = projectId,
            StageCode = StageCodes.IPA,
            RequestedStatus = StageStatus.NotStarted.ToString(),
            RequestedByUserId = Lead,
            RequestedOn = DateTimeOffset.UtcNow,
            DecisionStatus = "Pending"
        });
        await requester.SaveChangesAsync();

        var pending = ApproveAsync(options, projectId, revision, ReviewerA);
        await WaitUntilLockWaitersAsync(options, 1, pending);
        await request.CommitAsync();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ActualCorrectionDecisionOutcome.LifecycleConflict, result.Outcome);
        await AssertUnpublishedAndPendingAsync(options, projectId, Jan(12));
    }

    /// <summary>
    /// R5: an ARPP-style write to the IPA row (no gate) is in flight. The approval's FOR UPDATE
    /// stage load waits for it, then sees the changed baseline and refuses; ARPP's value stands.
    /// </summary>
    private static async Task ArppWriteOutsideTheGateIsSeenThroughTheStageRowLockAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d approve vs ARPP");

        await using var synchronizer = new ApplicationDbContext(options);
        await using var sync = await synchronizer.Database.BeginTransactionAsync();
        await synchronizer.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectStages\" SET \"Status\" = 'Completed', \"CompletedOn\" = {Jan(20)} WHERE \"ProjectId\" = {projectId} AND \"StageCode\" = {StageCodes.IPA}");

        var pending = ApproveAsync(options, projectId, revision, ReviewerA);
        await WaitUntilLockWaitersAsync(options, 1, pending);
        await sync.CommitAsync();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ActualCorrectionDecisionOutcome.SourceChanged, result.Outcome);
        await using var verify = new ApplicationDbContext(options);
        var ipa = await verify.ProjectStages.AsNoTracking().SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.IPA);
        Assert.Equal((StageStatus.Completed, Jan(12), (DateOnly?)Jan(20)), (ipa.Status, ipa.ActualStart, ipa.CompletedOn));
        Assert.Equal(0, await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId));
    }

    /// <summary>
    /// R9: the save carrying the stage changes and their history fails after the claim
    /// committed its row change in the transaction; the claim rolls back with it.
    /// </summary>
    private static async Task HistoryFailureRollsBackFactsAndClaimAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d history rollback");

        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options)
                         .AddInterceptors(new FailStageHistoryInterceptor()).Options))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                NewService(db).ApproveAsync(Decision(projectId, revision), TestActors.Hod(ReviewerA)));
        }

        await AssertUnpublishedAndPendingAsync(options, projectId, Jan(12));
    }

    /// <summary>The conditional claim itself fails; nothing is published and the revision stays Pending.</summary>
    private static async Task ClaimFailureRollsBackBeforeAnyPublicationAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, revision) = await SeedWithProposalAsync(options, "D-3d claim failure");

        await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options)
                         .AddInterceptors(new FailClaimInterceptor()).Options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                NewService(db).ApproveAsync(Decision(projectId, revision), TestActors.Hod(ReviewerA)));
        }

        await AssertUnpublishedAndPendingAsync(options, projectId, Jan(12));
        // The intact revision can still be approved afterwards.
        Assert.Equal(ActualCorrectionDecisionOutcome.Approved, (await ApproveAsync(options, projectId, revision, ReviewerB)).Outcome);
        await AssertPublishedOnceAsync(options, projectId);
    }

    // SECTION: Helpers
    private static ActualCorrectionProposalService NewService(ApplicationDbContext db) =>
        new(db, FakeClock.ForIstDate(Today), audit: new RecordingAudit());

    private static ActualCorrectionDecision Decision(int projectId, ActualCorrectionRevisionIdentity revision, string? remarks = null) =>
        new(projectId, revision.Id, revision.SubmittedOn, revision.RevisionNo, remarks);

    private static async Task<ActualCorrectionDecisionResult> ApproveAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        ActualCorrectionRevisionIdentity revision,
        string reviewer)
    {
        await using var db = new ApplicationDbContext(options);
        return await NewService(db).ApproveAsync(Decision(projectId, revision), TestActors.Hod(reviewer));
    }

    /// <summary>
    /// Holds the project gate, starts every contender on its own connection, waits until
    /// PostgreSQL reports all of them blocked on a lock, then releases the gate.
    /// </summary>
    private static async Task RunBehindHeldGateAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        params Func<Task>[] contenders)
    {
        await using var holder = new ApplicationDbContext(options);
        await using var hold = await holder.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(holder, projectId, default);

        var running = contenders.Select(contender => Task.Run(contender)).ToArray();
        await WaitUntilLockWaitersAsync(options, running.Length, running);
        await hold.CommitAsync();

        await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task WaitUntilLockWaitersAsync(
        DbContextOptions<ApplicationDbContext> options,
        int expected,
        params Task[] contenders)
    {
        await using var probe = new ApplicationDbContext(options);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            Assert.False(contenders.Any(contender => contender.IsCompleted), "A contender completed without waiting for the lock.");

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

        Assert.Fail("The contenders never waited on a lock.");
    }

    // FS completed 1–10 Jan; IPA in progress from 12 Jan; the PO proposes an IPA start of 11 Jan.
    private static async Task<(int ProjectId, ActualCorrectionRevisionIdentity Revision)> SeedWithProposalAsync(
        DbContextOptions<ApplicationDbContext> options,
        string name)
    {
        int projectId;
        await using (var db = new ApplicationDbContext(options))
        {
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
            projectId = project.Id;
            db.ProjectStages.AddRange(
                new ProjectStage { ProjectId = projectId, StageCode = StageCodes.FS, Status = StageStatus.Completed, ActualStart = Jan(1), CompletedOn = Jan(10) },
                new ProjectStage { ProjectId = projectId, StageCode = StageCodes.IPA, Status = StageStatus.InProgress, ActualStart = Jan(12) });
            await db.SaveChangesAsync();
        }

        var submission = await EditorSubmissionAsync(options, projectId, null, Jan(11));
        await using var submitter = new ApplicationDbContext(options);
        var result = await NewService(submitter).SubmitAsync(submission, TestActors.ProjectOfficer(Lead));
        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, result.Outcome);
        return (projectId, result.Revision!);
    }

    private static async Task<ActualCorrectionSubmission> EditorSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        ActualCorrectionRevisionIdentity? basedOn,
        DateOnly ipaStart)
    {
        await using var db = new ApplicationDbContext(options);
        var ipa = await db.ProjectStages.AsNoTracking().SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.IPA);
        return new ActualCorrectionSubmission(
            projectId,
            ActualChangeEntryPoint.Editor,
            new[] { new ActualCorrectionProposalRow(StageCodes.IPA, ipaStart, StageToken: ActualStageToken.Compute(ipa)) },
            new ActualCorrectionBaseline(ActualStageToken.BaselineVersion, basedOn?.Id, basedOn?.SubmittedOn));
    }

    private static async Task AssertPublishedOnceAsync(DbContextOptions<ApplicationDbContext> options, int projectId)
    {
        await using var verify = new ApplicationDbContext(options);
        var ipa = await verify.ProjectStages.AsNoTracking().SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.IPA);
        Assert.Equal(Jan(11), ipa.ActualStart);
        Assert.Equal(1, await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId));
        Assert.Equal(ActualCorrectionRevisionStatus.Approved,
            (await verify.ActualCorrectionRevisions.SingleAsync(item => item.ProjectId == projectId)).Status);
    }

    private static async Task AssertTerminalStateMatchesFactsAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        IReadOnlyList<ActualCorrectionDecisionResult> results)
    {
        await using var verify = new ApplicationDbContext(options);
        var revision = await verify.ActualCorrectionRevisions.SingleAsync(item => item.ProjectId == projectId);
        var ipa = await verify.ProjectStages.AsNoTracking().SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.IPA);
        var logs = await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId);
        if (results.Any(result => result.Outcome == ActualCorrectionDecisionOutcome.Approved))
        {
            Assert.Equal((ActualCorrectionRevisionStatus.Approved, (DateOnly?)Jan(11), 1), (revision.Status, ipa.ActualStart, logs));
        }
        else
        {
            Assert.Single(results, result => result.Outcome == ActualCorrectionDecisionOutcome.Rejected);
            Assert.Equal((ActualCorrectionRevisionStatus.Rejected, (DateOnly?)Jan(12), 0), (revision.Status, ipa.ActualStart, logs));
        }
    }

    private static async Task AssertUnpublishedAndPendingAsync(DbContextOptions<ApplicationDbContext> options, int projectId, DateOnly ipaStart)
    {
        await using var verify = new ApplicationDbContext(options);
        var ipa = await verify.ProjectStages.AsNoTracking().SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.IPA);
        Assert.Equal(ipaStart, ipa.ActualStart);
        Assert.Equal(0, await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId));
        var revision = await verify.ActualCorrectionRevisions.AsNoTracking().SingleAsync(item => item.ProjectId == projectId);
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, revision.Status);
        Assert.Null(revision.ClosedOn);
        Assert.Null(revision.ClosedByUserId);
    }

    private static DateOnly Jan(int day) => new(2026, 1, day);

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

    /// <summary>Fails the compare-and-set closure of the revision.</summary>
    private sealed class FailClaimInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"ActualCorrectionRevisions\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"ClosedOn\" = ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Injected claim failure.");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
