using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-3d: HoD/Admin decisions on PO Actual correction revisions. Approval
/// publishes the exact reviewed values with their stage history atomically, and
/// only against an unchanged, still-valid baseline; rejection publishes nothing.
/// Relational races are covered on PostgreSQL by
/// <see cref="ActualCorrectionDecisionConcurrencyPostgresFixture"/>.
/// </summary>
public sealed class ActualCorrectionDecisionTests
{
    private const int ProjectId = 1;
    private const int OtherProjectId = 2;
    private const string Lead = "assigned-po";
    private const string Reviewer = "reviewer";
    private const string Other = "someone-else";
    private static readonly DateOnly Today = new(2026, 3, 31);

    // SECTION: Authority (K-1) and self-decision
    public static IEnumerable<object[]> Deciders() => new[]
    {
        new object[] { TestActors.Hod(Reviewer) },
        new object[] { TestActors.Admin(Reviewer) },
        new object[] { TestActors.HodAndProjectOfficer(Reviewer) },
        new object[] { TestActors.AdminAndProjectOfficer(Reviewer) }
    };

    public static IEnumerable<object[]> NonDeciders() => new[]
    {
        new object[] { TestActors.ProjectOfficer(Lead) },
        new object[] { TestActors.ProjectOfficer(Other) },
        new object[] { new ActualWriteActor(Reviewer, IsAdmin: false, IsHoD: false, IsProjectOfficer: false) }
    };

    [Theory]
    [MemberData(nameof(Deciders))]
    public async Task HodAndAdmin_IncludingMixedRoles_ApproveAndPublishTheReviewedValues(ActualWriteActor reviewer)
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));

        var result = await fixture.ApproveAsync(revision, reviewer, "Checked against the register");

        Assert.Equal(ActualCorrectionDecisionOutcome.Approved, result.Outcome);
        Assert.Equal(new[] { StageCodes.IPA }, result.StageCodes);
        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(11), (await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA)).ActualStart);
        var closed = await verify.ActualCorrectionRevisions.SingleAsync();
        Assert.Equal(ActualCorrectionRevisionStatus.Approved, closed.Status);
        Assert.Equal(Reviewer, closed.ClosedByUserId);
        Assert.NotNull(closed.ClosedOn);
        Assert.Equal("Checked against the register", closed.DecisionNote);
        var log = await verify.StageChangeLogs.SingleAsync();
        Assert.Equal(ActualFactPublisher.ActualsUpdatedAction, log.Action);
        Assert.Equal((Jan(12), Jan(11)), (log.FromActualStart, log.ToActualStart));
        Assert.Equal(Reviewer, log.UserId);
        Assert.Equal(closed.ClosedOn, log.At);
        Assert.Contains($"rev 1 (#{revision.Id}) submitted by {Lead}", log.Note, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Deciders))]
    public async Task HodAndAdmin_RejectWithoutTouchingOfficialFactsOrEvidence(ActualWriteActor reviewer)
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        var before = await fixture.SnapshotAsync();

        var result = await fixture.RejectAsync(revision, reviewer, "Wrong stage");

        Assert.Equal(ActualCorrectionDecisionOutcome.Rejected, result.Outcome);
        await using var verify = fixture.NewContext();
        var closed = await verify.ActualCorrectionRevisions.SingleAsync();
        Assert.Equal(ActualCorrectionRevisionStatus.Rejected, closed.Status);
        Assert.Equal((Reviewer, "Wrong stage"), (closed.ClosedByUserId, closed.DecisionNote));
        Assert.Equal(before, await fixture.SnapshotAsync());
        await fixture.AssertOfficialAsync(StageCodes.IPA, Jan(12), null);
    }

    [Theory]
    [MemberData(nameof(NonDeciders))]
    public async Task PoOnlyUsersAndViewers_CannotDecide(ActualWriteActor actor)
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));

        Assert.Equal(ActualCorrectionDecisionOutcome.Forbidden, (await fixture.ApproveAsync(revision, actor)).Outcome);
        Assert.Equal(ActualCorrectionDecisionOutcome.Forbidden, (await fixture.RejectAsync(revision, actor, "No")).Outcome);
        await fixture.AssertStillPendingAsync(StageCodes.IPA, Jan(12), null);
    }

    [Fact]
    public async Task OriginalSubmitter_CannotDecideAfterRoleElevationOrReassignment()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await fixture.ReassignLeadAsync(Other);

        // The submitter is now HoD (or Admin) and no longer the project's PO.
        var approve = await fixture.ApproveAsync(revision, TestActors.HodAndProjectOfficer(Lead));
        var reject = await fixture.RejectAsync(revision, TestActors.Admin(Lead), "Mine");

        Assert.Equal(ActualCorrectionDecisionOutcome.SelfDecisionForbidden, approve.Outcome);
        Assert.Equal(ActualCorrectionDecisionOutcome.SelfDecisionForbidden, reject.Outcome);
        await fixture.AssertStillPendingAsync(StageCodes.IPA, Jan(12), null);
    }

    [Fact]
    public async Task ForgedProjectOrRevisionIdentity_IsRefused()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await using var db = fixture.NewContext();
        var service = fixture.Service(db);

        var otherProject = await service.ApproveAsync(
            new ActualCorrectionDecision(OtherProjectId, revision.Id, revision.SubmittedOn), TestActors.Hod(Reviewer));
        var missingProject = await service.ApproveAsync(
            new ActualCorrectionDecision(404, revision.Id, revision.SubmittedOn), TestActors.Hod(Reviewer));
        var wrongNumber = await service.ApproveAsync(
            new ActualCorrectionDecision(ProjectId, revision.Id, revision.SubmittedOn, ExpectedRevisionNo: 2), TestActors.Hod(Reviewer));
        var wrongEpoch = await service.RejectAsync(
            new ActualCorrectionDecision(ProjectId, revision.Id, revision.SubmittedOn.AddSeconds(-1), Remarks: "x"), TestActors.Hod(Reviewer));

        Assert.Equal(ActualCorrectionDecisionOutcome.NotActionable, otherProject.Outcome);
        Assert.Equal(ActualCorrectionDecisionOutcome.ProjectUnavailable, missingProject.Outcome);
        Assert.Equal(ActualCorrectionDecisionOutcome.NotActionable, wrongNumber.Outcome);
        Assert.Equal(ActualCorrectionDecisionOutcome.NotActionable, wrongEpoch.Outcome);
        await fixture.AssertStillPendingAsync(StageCodes.IPA, Jan(12), null);
    }

    // SECTION: Publication
    [Fact]
    public async Task BackfillApproval_PublishesTheCompletionAndClearsTheBackfillFlag()
    {
        await using var fixture = await Fixture.CreateAsync(AwaitingBackfill(StageCodes.BID, Feb(1)));
        var revision = await fixture.SubmitAsync(ActualChangeEntryPoint.Backfill, Row(StageCodes.BID, completed: Feb(10)));

        Assert.Equal(ActualCorrectionDecisionOutcome.Approved, (await fixture.ApproveAsync(revision, TestActors.Hod(Reviewer))).Outcome);

        await using var verify = fixture.NewContext();
        var bid = await verify.ProjectStages.SingleAsync();
        Assert.Equal((StageStatus.Completed, Feb(1), Feb(10), false), (bid.Status, bid.ActualStart, bid.CompletedOn, bid.RequiresBackfill));
        var log = await verify.StageChangeLogs.SingleAsync();
        Assert.Equal(ActualFactPublisher.BackfillAction, log.Action);
        Assert.Equal(((DateOnly?)null, (DateOnly?)Feb(10)), (log.FromCompletedOn, log.ToCompletedOn));
    }

    [Fact]
    public async Task MultiStageApproval_IsAtomic_AndAFailedHistoryWriteLeavesEverythingAsItWas()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), AwaitingBackfill(StageCodes.BID, Feb(1)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.FS, clear: true), Row(StageCodes.IPA, start: Jan(11)), Row(StageCodes.BID, completed: Feb(10)));
        var before = await fixture.SnapshotAsync();

        await using (var failing = fixture.NewContext(failStageHistory: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service(failing).ApproveAsync(
                new ActualCorrectionDecision(ProjectId, revision.Id, revision.SubmittedOn), TestActors.Hod(Reviewer)));
            Assert.Empty(failing.ChangeTracker.Entries<StageChangeLog>());
            Assert.DoesNotContain(failing.ChangeTracker.Entries<ProjectStage>(), entry => entry.State == EntityState.Modified);
        }

        Assert.Equal(before, await fixture.SnapshotAsync());
        await fixture.AssertStillPendingAsync(StageCodes.IPA, Jan(12), null);

        Assert.Equal(ActualCorrectionDecisionOutcome.Approved, (await fixture.ApproveAsync(revision, TestActors.Admin(Reviewer))).Outcome);
        await using var verify = fixture.NewContext();
        var stages = await verify.ProjectStages.ToDictionaryAsync(stage => stage.StageCode);
        Assert.Null(stages[StageCodes.FS].ActualStart);
        Assert.Equal(Jan(10), stages[StageCodes.FS].CompletedOn);
        Assert.Equal(Jan(11), stages[StageCodes.IPA].ActualStart);
        Assert.Equal(Feb(10), stages[StageCodes.BID].CompletedOn);
        Assert.Equal(3, await verify.StageChangeLogs.CountAsync());
    }

    [Fact]
    public async Task PostCommitAuditFailure_DoesNotUndoTheApproval()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db, new ThrowingAudit()).ApproveAsync(
            new ActualCorrectionDecision(ProjectId, revision.Id, revision.SubmittedOn), TestActors.Hod(Reviewer));

        Assert.Equal(ActualCorrectionDecisionOutcome.Approved, result.Outcome);
        await fixture.AssertOfficialAsync(StageCodes.IPA, Jan(11), null, expectedLogs: 1);
    }

    // SECTION: Approval-time revalidation
    [Fact]
    public async Task ChangedOfficialBaseline_IsSourceChanged_AndTheRevisionStaysWithThePo()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(14)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await fixture.ChangeStoredStartAsync(StageCodes.IPA, Jan(13));

        var result = await fixture.ApproveAsync(revision, TestActors.Hod(Reviewer));

        Assert.Equal(ActualCorrectionDecisionOutcome.SourceChanged, result.Outcome);
        Assert.Equal(new[] { StageCodes.IPA }, result.StageCodes);
        await fixture.AssertStillPendingAsync(StageCodes.IPA, Jan(13), null);
        await using var db = fixture.NewContext();
        Assert.Equal(ActualCorrectionWithdrawalOutcome.Withdrawn,
            (await fixture.Service(db).WithdrawAsync(ProjectId, revision.Id, revision.SubmittedOn, TestActors.ProjectOfficer(Lead))).Outcome);
    }

    [Fact]
    public async Task ProposalMadeInvalidByALaterPredecessorChange_IsRefusedWithoutPublishing()
    {
        // Valid at submission (IPA not complete); IPA then completes on 20 Jan, after the proposed SOW start.
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), InProgress(StageCodes.SOW, Jan(16)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.SOW, start: Jan(14)));
        await fixture.CompleteStageAsync(StageCodes.IPA, Jan(20));

        var result = await fixture.ApproveAsync(revision, TestActors.Hod(Reviewer));

        Assert.Equal(ActualCorrectionDecisionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.StartsWith("SOW:", StringComparison.Ordinal));
        await fixture.AssertStillPendingAsync(StageCodes.SOW, Jan(16), null);
    }

    [Fact]
    public async Task RepublishedArppCompletion_ProtectsIpaFromAnOutdatedProposal()
    {
        var arpp = new MutableArppAuthority(Authority(Jan(20)));
        await using var fixture = await Fixture.CreateAsync(
            new[] { Completed(StageCodes.FS, Jan(1), Jan(10)), Completed(StageCodes.IPA, Jan(12), Jan(20)) }, arpp);
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        arpp.Current = Authority(Jan(22));

        var result = await fixture.ApproveAsync(revision, TestActors.Hod(Reviewer));

        Assert.Equal(ActualCorrectionDecisionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.Contains("published ARPP record", StringComparison.Ordinal));
        await fixture.AssertStillPendingAsync(StageCodes.IPA, Jan(12), Jan(20));
    }

    [Fact]
    public async Task PendingLifecycleRequests_BlockOnlyWhenTheyContradictTheProposal()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), InProgress(StageCodes.BID, Feb(1)));
        var first = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        // Created after submission: an unrelated pending request, then a contradictory reopen.
        await fixture.AddPendingLifecycleRequestAsync(StageCodes.BID, StageStatus.Completed, Feb(5));
        await fixture.AddPendingLifecycleRequestAsync(StageCodes.IPA, StageStatus.NotStarted);

        var blocked = await fixture.ApproveAsync(first, TestActors.Hod(Reviewer));
        await fixture.DecideLifecycleRequestsAsync(StageCodes.IPA, "Rejected");
        var approved = await fixture.ApproveAsync(first, TestActors.Hod(Reviewer));

        Assert.Equal(ActualCorrectionDecisionOutcome.LifecycleConflict, blocked.Outcome);
        Assert.Contains(blocked.Errors!, error => error.Contains("pending lifecycle update for IPA", StringComparison.Ordinal));
        Assert.Equal(ActualCorrectionDecisionOutcome.Approved, approved.Outcome);
        await fixture.AssertOfficialAsync(StageCodes.IPA, Jan(11), null, expectedLogs: 1);
    }

    // SECTION: Exact identity
    [Fact]
    public async Task DecidedWithdrawnOrSupersededRevisions_CannotBeDecidedAgain()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        var approved = await fixture.SubmitAsync(Row(StageCodes.FS, start: Jan(4)));
        Assert.Equal(ActualCorrectionDecisionOutcome.Approved, (await fixture.ApproveAsync(approved, TestActors.Hod(Reviewer))).Outcome);
        var repeated = await fixture.ApproveAsync(approved, TestActors.Admin(Reviewer));
        var rejectAfter = await fixture.RejectAsync(approved, TestActors.Admin(Reviewer), "Late");

        var superseded = await fixture.SubmitAsync(Row(StageCodes.FS, start: Jan(3)));
        await fixture.SubmitAsync(Row(StageCodes.FS, start: Jan(2)));
        var staleTab = await fixture.ApproveAsync(superseded, TestActors.Hod(Reviewer));

        Assert.Equal(ActualCorrectionDecisionOutcome.NotActionable, repeated.Outcome);
        Assert.Equal(ActualCorrectionDecisionOutcome.NotActionable, rejectAfter.Outcome);
        Assert.Equal(ActualCorrectionDecisionOutcome.NotActionable, staleTab.Outcome);
        await fixture.AssertOfficialAsync(StageCodes.FS, Jan(4), null, expectedLogs: 1);
    }

    // SECTION: Decision Centre
    [Fact]
    public async Task DecisionCentre_ListsTheRevisionAndShowsRecordedAgainstProposedValues()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var revision = await fixture.SubmitAsync("Register shows 11 Jan", Row(StageCodes.FS, clear: true), Row(StageCodes.IPA, start: Jan(11)));
        await using var db = fixture.NewContext();
        var queue = Queue(db);

        var pending = await queue.GetPendingAsync(new ApprovalQueueQuery { Type = ApprovalQueueType.ActualCorrection }, Principal(Reviewer, "HoD"));
        var count = await queue.GetPendingCountAsync(Principal(Reviewer, "HoD"));
        var detail = await queue.GetDetailAsync(ApprovalQueueType.ActualCorrection, revision.Id.ToString(), Principal(Reviewer, "HoD"));
        var own = await queue.GetDetailAsync(ApprovalQueueType.ActualCorrection, revision.Id.ToString(), Principal(Lead, "HoD"));
        var hiddenFromPo = await queue.GetDetailAsync(ApprovalQueueType.ActualCorrection, revision.Id.ToString(), Principal(Lead, "Project Officer"));

        var listed = Assert.Single(pending);
        Assert.Equal("Actual date correction revision 1: 2 stages.", listed.Summary);
        Assert.Equal(1, count);
        Assert.Equal(ApprovalReadiness.Ready, detail!.Item.Readiness);
        var actual = detail.ActualCorrection!;
        Assert.Equal(revision.SubmittedOn, actual.SubmittedOnUtc);
        Assert.Equal("Register shows 11 Jan", actual.Note);
        var fs = actual.Items.Single(item => item.StageCode == StageCodes.FS);
        Assert.Equal((Jan(1), (DateOnly?)null, true), (fs.RecordedActualStart, fs.ProposedActualStart, fs.ClearsActualStart));
        Assert.Equal(Jan(11), actual.Items.Single(item => item.StageCode == StageCodes.IPA).ProposedActualStart);
        Assert.Equal(ApprovalReadiness.Blocked, own!.Item.Readiness);
        Assert.True(own.ActualCorrection!.SubmittedByViewer);
        Assert.Null(hiddenFromPo);
    }

    [Fact]
    public async Task DecisionCentre_FlagsAChangedBaselineAndDropsDecidedRevisionsFromTheQueue()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(14)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await fixture.ChangeStoredStartAsync(StageCodes.IPA, Jan(13));
        await using var db = fixture.NewContext();

        var stale = await Queue(db).GetDetailAsync(ApprovalQueueType.ActualCorrection, revision.Id.ToString(), Principal(Reviewer, "Admin"));
        Assert.Equal(ApprovalReadiness.Stale, stale!.Item.Readiness);
        var ipa = Assert.Single(stale.ActualCorrection!.Items);
        Assert.True(ipa.SourceChanged);
        Assert.Equal((Jan(14), (DateOnly?)Jan(13)), (ipa.RecordedActualStart, ipa.CurrentActualStart));

        Assert.Equal(ActualCorrectionDecisionOutcome.Rejected, (await fixture.RejectAsync(revision, TestActors.Admin(Reviewer), "Revise")).Outcome);
        await using var after = fixture.NewContext();
        Assert.Empty(await Queue(after).GetPendingAsync(new ApprovalQueueQuery(), Principal(Reviewer, "Admin")));
        Assert.Equal(ApprovalReadiness.Superseded,
            (await Queue(after).GetDetailAsync(ApprovalQueueType.ActualCorrection, revision.Id.ToString(), Principal(Reviewer, "Admin")))!.Item.Readiness);
    }

    [Fact]
    public async Task DecisionCentre_WarnsThatPendingLifecycleUpdatesAreCheckedAtApproval()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), InProgress(StageCodes.BID, Feb(1)));
        var revision = await fixture.SubmitAsync(Row(StageCodes.IPA, start: Jan(11)));
        await fixture.AddPendingLifecycleRequestAsync(StageCodes.BID, StageStatus.Completed, Feb(5));
        await using var db = fixture.NewContext();

        var detail = await Queue(db).GetDetailAsync(ApprovalQueueType.ActualCorrection, revision.Id.ToString(), Principal(Reviewer, "HoD"));

        Assert.Contains(detail!.ActualCorrection!.Warnings, warning => warning.Contains("Lifecycle updates are pending for BID", StringComparison.Ordinal));
        Assert.Null(detail.ActualCorrection.Items.Single().PublishedStatus);
    }

    [Fact]
    public void DecisionCentre_ReportsSuccessOnlyForCommittedDecisions()
    {
        var identity = new ActualCorrectionRevisionIdentity(5, 2, DateTimeOffset.UnixEpoch);

        var approved = ApprovalDecisionService.ToApprovalResult(new ActualCorrectionDecisionResult(
            ActualCorrectionDecisionOutcome.Approved, identity, StageCodes: new[] { StageCodes.IPA }));
        var rejected = ApprovalDecisionService.ToApprovalResult(new ActualCorrectionDecisionResult(ActualCorrectionDecisionOutcome.Rejected, identity));
        var source = ApprovalDecisionService.ToApprovalResult(ActualCorrectionDecisionResult.SourceChanged(identity, new[] { StageCodes.IPA }));
        var stale = ApprovalDecisionService.ToApprovalResult(ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.NotActionable));
        var self = ApprovalDecisionService.ToApprovalResult(ActualCorrectionDecisionResult.Of(ActualCorrectionDecisionOutcome.SelfDecisionForbidden));

        Assert.Equal(ApprovalDecisionOutcome.Success, approved.Outcome);
        Assert.Contains("Official dates were updated for IPA", approved.Message, StringComparison.Ordinal);
        Assert.Contains("Official dates are unchanged", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(ApprovalDecisionOutcome.ValidationFailed, source.Outcome);
        Assert.Contains("Nothing was approved", source.Message, StringComparison.Ordinal);
        Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, stale.Outcome);
        Assert.Equal(ApprovalDecisionOutcome.Forbidden, self.Outcome);
    }

    // SECTION: Helpers
    private static ActualCorrectionProposalRow Row(string code, DateOnly? start = null, DateOnly? completed = null, bool clear = false) =>
        new(code, start, clear, completed);

    private static ApprovalQueueService Queue(ApplicationDbContext db)
    {
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        return new ApprovalQueueService(db, new PlanCompareService(db),
            new StageApprovalSequenceService(db, policy, new ProjectFactsReadService(db)),
            new ActualTimelineValidationService(policy, new ArppIpaStageAuthorityService(db), FakeClock.ForIstDate(Today)));
    }

    private static ClaimsPrincipal Principal(string userId, string role) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role) }, "Test"));

    private static ProjectStage InProgress(string code, DateOnly start) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.InProgress, ActualStart = start };

    private static ProjectStage Completed(string code, DateOnly? start, DateOnly completed) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.Completed, ActualStart = start, CompletedOn = completed };

    private static ProjectStage AwaitingBackfill(string code, DateOnly? start) =>
        new() { ProjectId = ProjectId, StageCode = code, Status = StageStatus.Completed, ActualStart = start, RequiresBackfill = true };

    private static ArppIpaStageAuthority Authority(DateOnly issueDate) =>
        new(ProjectId, 2025, 7, ArppIssueKind.Original, 0, "ARPP 2025-26", issueDate, 70, "1", "PPP-1");

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static DateOnly Feb(int day) => new(2026, 2, day);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly IArppIpaStageAuthorityService _arpp;
        private readonly FakeClock _clock = FakeClock.ForIstDate(Today);

        private Fixture(SqliteConnection connection, IArppIpaStageAuthorityService arpp)
        {
            _connection = connection;
            _arpp = arpp;
        }

        public static Task<Fixture> CreateAsync(params ProjectStage[] stages) => CreateAsync(stages, new MutableArppAuthority(null));

        public static async Task<Fixture> CreateAsync(ProjectStage[] stages, IArppIpaStageAuthorityService arpp)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection, arpp);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            foreach (var userId in new[] { "seed", Lead, Reviewer, Other })
            {
                db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });
            }

            foreach (var id in new[] { ProjectId, OtherProjectId })
            {
                db.Projects.Add(new Project
                {
                    Id = id,
                    Name = $"Decision project {id}",
                    CreatedByUserId = "seed",
                    LeadPoUserId = Lead,
                    WorkflowVersion = ProcurementWorkflow.VersionV1
                });
            }

            db.ProjectStages.AddRange(stages);
            await db.SaveChangesAsync();
            return fixture;
        }

        public ApplicationDbContext NewContext(bool failStageHistory = false)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection);
            if (failStageHistory)
            {
                builder.AddInterceptors(new FailStageHistoryInterceptor());
            }

            return new ApplicationDbContext(builder.Options);
        }

        public ActualCorrectionProposalService Service(ApplicationDbContext db, IAuditService? audit = null)
        {
            _clock.Set(_clock.UtcNow.AddSeconds(1));
            return new ActualCorrectionProposalService(db, _clock, _arpp, StageWorkflowTestFactory.CreatePolicy(db), audit: audit ?? new RecordingAudit());
        }

        public Task<ActualCorrectionRevisionIdentity> SubmitAsync(params ActualCorrectionProposalRow[] rows) =>
            SubmitAsync(ActualChangeEntryPoint.Editor, null, rows);

        public Task<ActualCorrectionRevisionIdentity> SubmitAsync(string note, params ActualCorrectionProposalRow[] rows) =>
            SubmitAsync(ActualChangeEntryPoint.Editor, note, rows);

        public Task<ActualCorrectionRevisionIdentity> SubmitAsync(ActualChangeEntryPoint entryPoint, params ActualCorrectionProposalRow[] rows) =>
            SubmitAsync(entryPoint, null, rows);

        // Submits as the assigned PO from a fresh form: current tokens and the current pending identity.
        private async Task<ActualCorrectionRevisionIdentity> SubmitAsync(
            ActualChangeEntryPoint entryPoint,
            string? note,
            ActualCorrectionProposalRow[] rows)
        {
            await using var db = NewContext();
            var stages = await db.ProjectStages.AsNoTracking().ToListAsync();
            var pending = await Service(db).GetPendingAsync(ProjectId);
            var result = await Service(db).SubmitAsync(
                new ActualCorrectionSubmission(
                    ProjectId,
                    entryPoint,
                    rows.Select(row => row with { StageToken = ActualStageToken.Compute(stages.Single(stage => stage.StageCode == row.StageCode)) }).ToArray(),
                    new ActualCorrectionBaseline(ActualStageToken.BaselineVersion, pending?.Id, pending?.SubmittedOn),
                    note),
                TestActors.ProjectOfficer(Lead));
            Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, result.Outcome);
            return result.Revision!;
        }

        public async Task<ActualCorrectionDecisionResult> ApproveAsync(ActualCorrectionRevisionIdentity revision, ActualWriteActor reviewer, string? remarks = null)
        {
            await using var db = NewContext();
            return await Service(db).ApproveAsync(
                new ActualCorrectionDecision(ProjectId, revision.Id, revision.SubmittedOn, revision.RevisionNo, remarks), reviewer);
        }

        public async Task<ActualCorrectionDecisionResult> RejectAsync(ActualCorrectionRevisionIdentity revision, ActualWriteActor reviewer, string remarks)
        {
            await using var db = NewContext();
            return await Service(db).RejectAsync(
                new ActualCorrectionDecision(ProjectId, revision.Id, revision.SubmittedOn, revision.RevisionNo, remarks), reviewer);
        }

        /// <summary>Official stage facts, history and the submitted evidence, for byte-level comparison.</summary>
        public async Task<string> SnapshotAsync()
        {
            await using var db = NewContext();
            var stages = await db.ProjectStages.AsNoTracking().OrderBy(stage => stage.StageCode).ToListAsync();
            var items = await db.ActualCorrectionItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
            var revisions = await db.ActualCorrectionRevisions.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
            return string.Join(";",
                stages.Select(stage => $"{stage.StageCode}|{stage.Status}|{stage.ActualStart}|{stage.CompletedOn}|{stage.RequiresBackfill}|{stage.IsAutoCompleted}")
                    .Concat(items.Select(item => $"{item.Id}|{item.StageCode}|{item.Kind}|{item.BeforeStatus}|{item.BeforeActualStart}|{item.BeforeCompletedOn}|{item.ProposedActualStart}|{item.ProposedCompletedOn}|{item.ClearsActualStart}"))
                    .Concat(revisions.Select(revision => $"{revision.Id}|{revision.RevisionNo}|{revision.SubmittedByUserId}|{revision.SubmittedOn:O}|{revision.ContentHash}|{revision.Note}"))
                    .Append($"logs={await db.StageChangeLogs.CountAsync()}"));
        }

        public async Task ChangeStoredStartAsync(string code, DateOnly start)
        {
            await using var db = NewContext();
            (await db.ProjectStages.SingleAsync(item => item.StageCode == code)).ActualStart = start;
            await db.SaveChangesAsync();
        }

        public async Task CompleteStageAsync(string code, DateOnly completed)
        {
            await using var db = NewContext();
            var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == code);
            stage.Status = StageStatus.Completed;
            stage.CompletedOn = completed;
            await db.SaveChangesAsync();
        }

        public async Task ReassignLeadAsync(string leadPoUserId)
        {
            await using var db = NewContext();
            (await db.Projects.SingleAsync(item => item.Id == ProjectId)).LeadPoUserId = leadPoUserId;
            await db.SaveChangesAsync();
        }

        public async Task AddPendingLifecycleRequestAsync(string code, StageStatus status, DateOnly? date = null)
        {
            await using var db = NewContext();
            db.StageChangeRequests.Add(new StageChangeRequest
            {
                ProjectId = ProjectId,
                StageCode = code,
                RequestedStatus = status.ToString(),
                RequestedDate = date,
                RequestedByUserId = Lead,
                RequestedOn = _clock.UtcNow,
                DecisionStatus = "Pending"
            });
            await db.SaveChangesAsync();
        }

        public async Task DecideLifecycleRequestsAsync(string code, string status)
        {
            await using var db = NewContext();
            foreach (var request in await db.StageChangeRequests.Where(item => item.StageCode == code && item.DecisionStatus == "Pending").ToListAsync())
            {
                request.DecisionStatus = status;
                request.DecidedByUserId = Reviewer;
                request.DecidedOn = _clock.UtcNow;
            }

            await db.SaveChangesAsync();
        }

        public async Task AssertOfficialAsync(string code, DateOnly? start, DateOnly? completed, int expectedLogs = 0)
        {
            await using var verify = NewContext();
            var stage = await verify.ProjectStages.AsNoTracking().SingleAsync(item => item.StageCode == code);
            Assert.Equal(start, stage.ActualStart);
            Assert.Equal(completed, stage.CompletedOn);
            Assert.Equal(expectedLogs, await verify.StageChangeLogs.CountAsync());
        }

        public async Task AssertStillPendingAsync(string code, DateOnly? start, DateOnly? completed)
        {
            await AssertOfficialAsync(code, start, completed);
            await using var verify = NewContext();
            var revision = await verify.ActualCorrectionRevisions.SingleAsync(item => item.Status == ActualCorrectionRevisionStatus.Pending);
            Assert.Null(revision.ClosedOn);
            Assert.Null(revision.ClosedByUserId);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class MutableArppAuthority(ArppIpaStageAuthority? initial) : IArppIpaStageAuthorityService
    {
        public ArppIpaStageAuthority? Current { get; set; } = initial;

        public Task<ArppIpaStageAuthority?> ResolveAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ArppIpaStageAuthority>>(
                Current is null
                    ? new Dictionary<int, ArppIpaStageAuthority>()
                    : new Dictionary<int, ArppIpaStageAuthority> { [Current.ProjectId] = Current });

        public Task<bool> IsManagedAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current is not null);

        public Task EnsureManualLifecycleMutationAllowedAsync(int projectId, string stageCode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

    private sealed class ThrowingAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null) =>
            throw new InvalidOperationException("Injected audit failure.");
    }
}
