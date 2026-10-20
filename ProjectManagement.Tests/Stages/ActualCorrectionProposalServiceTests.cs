using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-3a/b: assigned-PO Actual correction proposals. Submission records
/// immutable revision evidence only and supersedes the pending revision
/// atomically. Relational races are covered on PostgreSQL by
/// <see cref="ActualCorrectionProposalConcurrencyPostgresFixture"/>.
/// </summary>
public sealed class ActualCorrectionProposalServiceTests
{
    private const int ProjectId = 1;
    private const string Lead = "assigned-po";
    private const string Other = "someone-else";
    private static readonly DateOnly Today = new(2026, 3, 31);

    // SECTION: Authorization
    [Fact]
    public async Task AssignedPoOnly_SubmitsProposalEvidenceWithoutTouchingOfficialFacts()
    {
        var audit = new RecordingAudit();
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db, audit).SubmitAsync(
            Editor(None, "  Start was recorded late.  ", Row(db, StageCodes.IPA, start: Jan(11))),
            TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, result.Outcome);
        await using var verify = fixture.NewContext();
        var revision = await verify.ActualCorrectionRevisions.Include(item => item.Items).SingleAsync();
        Assert.Equal(result.Revision!.Id, revision.Id);
        Assert.Equal(1, revision.RevisionNo);
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, revision.Status);
        Assert.Equal(ActualCorrectionEntryPoint.Editor, revision.EntryPoint);
        Assert.Equal(Lead, revision.SubmittedByUserId);
        Assert.Equal(result.Revision.SubmittedOn, revision.SubmittedOn);
        Assert.Equal("Start was recorded late.", revision.Note);
        Assert.Equal(64, revision.ContentHash.Length);
        Assert.Null(revision.SupersedesRevisionId);
        Assert.Null(revision.ClosedOn);

        var item = Assert.Single(revision.Items);
        Assert.Equal(StageCodes.IPA, item.StageCode);
        Assert.Equal(ActualCorrectionItemKind.Correction, item.Kind);
        Assert.Equal(StageStatus.InProgress, item.BeforeStatus);
        Assert.Equal(Jan(12), item.BeforeActualStart);
        Assert.Null(item.BeforeCompletedOn);
        Assert.False(item.BeforeRequiresBackfill);
        Assert.Equal(Jan(11), item.ProposedActualStart);
        Assert.Null(item.ProposedCompletedOn);
        Assert.False(item.ClearsActualStart);

        await AssertOfficialUnchangedAsync(fixture, StageCodes.IPA, StageStatus.InProgress, Jan(12), null, requiresBackfill: false);
        Assert.Equal("Projects.ActualCorrectionSubmitted", Assert.Single(audit.Entries).Action);
    }

    public static IEnumerable<object[]> ActorsWithoutProposalRoute() => new[]
    {
        new object[] { TestActors.ProjectOfficer(Other), ActualCorrectionSubmissionOutcome.Forbidden },
        new object[] { new ActualWriteActor(Lead, IsAdmin: false, IsHoD: false, IsProjectOfficer: false), ActualCorrectionSubmissionOutcome.Forbidden },
        new object[] { TestActors.Hod(Other), ActualCorrectionSubmissionOutcome.DirectAuthority },
        new object[] { TestActors.Admin(Other), ActualCorrectionSubmissionOutcome.DirectAuthority },
        new object[] { TestActors.HodAndProjectOfficer(Lead), ActualCorrectionSubmissionOutcome.DirectAuthority },
        new object[] { TestActors.AdminAndProjectOfficer(Lead), ActualCorrectionSubmissionOutcome.DirectAuthority }
    };

    [Theory]
    [MemberData(nameof(ActorsWithoutProposalRoute))]
    public async Task OnlyTheAssignedPoOnlyUser_IsRoutedToSubmission(ActualWriteActor actor, ActualCorrectionSubmissionOutcome expected)
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.IPA, start: Jan(11))), actor);

        Assert.Equal(expected, result.Outcome);
        await AssertNoRevisionsAsync(fixture);
    }

    [Fact]
    public async Task ReassignedFormerPo_CannotSubmitOrWithdraw()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.IPA, Jan(11));
        await fixture.ReassignLeadAsync(Other);

        await using var db = fixture.NewContext();
        var resubmit = await fixture.Service(db).SubmitAsync(
            Editor(Based(first), Row(db, StageCodes.IPA, start: Jan(10))), TestActors.ProjectOfficer(Lead));
        var withdraw = await fixture.Service(db).WithdrawAsync(
            ProjectId, first.Id, first.SubmittedOn, TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Forbidden, resubmit.Outcome);
        Assert.Equal(ActualCorrectionWithdrawalOutcome.Forbidden, withdraw.Outcome);
        await using var verify = fixture.NewContext();
        var only = await verify.ActualCorrectionRevisions.SingleAsync();
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, only.Status);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task DeletedClosedOrLegacyProject_IsRefused(bool deleted, bool closed, bool legacy)
    {
        await using var fixture = await Fixture.CreateAsync(
            new[] { Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)) },
            project =>
            {
                project.IsDeleted = deleted;
                project.LifecycleStatus = closed ? ProjectLifecycleStatus.Completed : ProjectLifecycleStatus.Active;
                project.IsLegacy = legacy;
            });
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.IPA, start: Jan(11))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(
            deleted ? ActualCorrectionSubmissionOutcome.NotFound : ActualCorrectionSubmissionOutcome.ProjectNotEditable,
            result.Outcome);
        await AssertNoRevisionsAsync(fixture);
    }

    [Fact]
    public async Task MissingProject_IsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            new ActualCorrectionSubmission(404, ActualChangeEntryPoint.Editor,
                new[] { new ActualCorrectionProposalRow(StageCodes.FS, Jan(4)) }, None),
            TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.NotFound, result.Outcome);
    }

    // SECTION: Submission validation
    [Fact]
    public async Task BackfillProposal_DerivesBackfillKindAndLeavesTheStageAwaitingBackfill()
    {
        await using var fixture = await Fixture.CreateAsync(AwaitingBackfill(StageCodes.BID, Jan(5)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Backfill(None, Row(db, StageCodes.BID, completed: Jan(20))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, result.Outcome);
        await using var verify = fixture.NewContext();
        var revision = await verify.ActualCorrectionRevisions.Include(item => item.Items).SingleAsync();
        Assert.Equal(ActualCorrectionEntryPoint.Backfill, revision.EntryPoint);
        var item = Assert.Single(revision.Items);
        Assert.Equal(ActualCorrectionItemKind.Backfill, item.Kind);
        Assert.Null(item.BeforeCompletedOn);
        Assert.True(item.BeforeRequiresBackfill);
        Assert.Equal(Jan(5), item.ProposedActualStart);
        Assert.Equal(Jan(20), item.ProposedCompletedOn);
        await AssertOfficialUnchangedAsync(fixture, StageCodes.BID, StageStatus.Completed, Jan(5), null, requiresBackfill: true);
    }

    [Fact]
    public async Task BackfillEntry_ForAStageNoLongerAwaitingBackfill_IsAConflict()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Backfill(None, Row(db, StageCodes.FS, completed: Jan(9))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Conflict, result.Outcome);
        Assert.Equal(new[] { StageCodes.FS }, result.StageCodes);
        await AssertNoRevisionsAsync(fixture);
    }

    [Fact]
    public async Task InvalidChronology_IsRefusedWithTheSharedValidatorMessage()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.IPA, start: Jan(9))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.Contains("Same-day commencement is permitted", StringComparison.Ordinal));
        await AssertNoRevisionsAsync(fixture);
    }

    [Theory]
    [InlineData("XYZ", "not found")]
    [InlineData("", "must name a stage")]
    public async Task UnknownOrMissingStage_IsRefused(string code, string expected)
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Editor(None, new ActualCorrectionProposalRow(code, Jan(4))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.Contains(expected, StringComparison.Ordinal));
        await AssertNoRevisionsAsync(fixture);
    }

    [Fact]
    public async Task DuplicateStageRows_AreRefused()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Editor(None, new[]
            {
                Row(db, StageCodes.FS, start: Jan(4)),
                Row(db, StageCodes.FS, start: Jan(3)) with { StageCode = "fs" }
            }),
            TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.Contains("Include each stage once", StringComparison.Ordinal));
        await AssertNoRevisionsAsync(fixture);
    }

    [Fact]
    public async Task ExplicitClear_IsRecordedAndBlankStartPreservesTheRecordedValue()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)));
        await using var db = fixture.NewContext();
        var service = fixture.Service(db);

        var blank = await service.SubmitAsync(
            Editor(None, Row(db, StageCodes.FS)), TestActors.ProjectOfficer(Lead));
        var cleared = await service.SubmitAsync(
            Editor(None, Row(db, StageCodes.FS, clear: true)), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, blank.Outcome);
        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, cleared.Outcome);
        await using var verify = fixture.NewContext();
        var item = await verify.ActualCorrectionItems.SingleAsync();
        Assert.True(item.ClearsActualStart);
        Assert.Null(item.ProposedActualStart);
        Assert.Equal(Jan(10), item.ProposedCompletedOn);
        Assert.Equal(Jan(1), item.BeforeActualStart);
        await AssertOfficialUnchangedAsync(fixture, StageCodes.FS, StageStatus.Completed, Jan(1), Jan(10), requiresBackfill: false);
    }

    [Fact]
    public async Task PublishedArppIpa_RefusesCompletionButAllowsAStartCorrection()
    {
        var authority = Authority(Jan(20));
        await using var fixture = await Fixture.CreateAsync(
            new[] { Completed(StageCodes.FS, Jan(1), Jan(10)), Completed(StageCodes.IPA, Jan(12), Jan(20)) },
            authority: authority);
        await using var db = fixture.NewContext();
        var service = fixture.Service(db);

        var completion = await service.SubmitAsync(
            Editor(None, Row(db, StageCodes.IPA, completed: Jan(18))), TestActors.ProjectOfficer(Lead));
        var start = await service.SubmitAsync(
            Editor(None, Row(db, StageCodes.IPA, start: Jan(11))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, completion.Outcome);
        Assert.Contains(completion.Errors!, error => error.Contains("published ARPP record", StringComparison.Ordinal));
        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, start.Outcome);
        await using var verify = fixture.NewContext();
        var item = await verify.ActualCorrectionItems.SingleAsync();
        Assert.Equal(Jan(11), item.ProposedActualStart);
        Assert.Equal(Jan(20), item.ProposedCompletedOn);
    }

    // SECTION: Editor baseline (E.4 S1–S6)
    [Fact]
    public async Task StaleStageToken_IsRefusedWhileAnUnrelatedStageChangeIsAccepted()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), InProgress(StageCodes.BID, Feb(1)));
        await using var db = fixture.NewContext();
        var ipa = Row(db, StageCodes.IPA, start: Jan(11));
        var bid = Row(db, StageCodes.BID, start: Feb(2));
        await fixture.ChangeStoredStartAsync(StageCodes.IPA, Jan(13));

        var stale = await fixture.Service(db).SubmitAsync(Editor(None, ipa), TestActors.ProjectOfficer(Lead));
        // IPA is posted at its rendered value plus an unrelated BID change (S3b).
        var unrelated = await fixture.Service(db).SubmitAsync(
            Editor(None, new[] { bid, new ActualCorrectionProposalRow(StageCodes.IPA, StageToken: ipa.StageToken) }),
            TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, stale.Outcome);
        Assert.Equal(ActualCorrectionStaleReason.StagesChanged, stale.StaleReason);
        Assert.Equal(new[] { StageCodes.IPA }, stale.StageCodes);
        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, unrelated.Outcome);
        await using var verify = fixture.NewContext();
        Assert.Equal(StageCodes.BID, (await verify.ActualCorrectionItems.SingleAsync()).StageCode);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(2, null, null)]
    [InlineData(1, 7, null)]
    public async Task MissingOrIncompleteBaseline_IsStaleAndWritesNothing(int? version, int? basedOn, int? epochOffset)
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        await using var db = fixture.NewContext();
        var baseline = version is null
            ? null
            : new ActualCorrectionBaseline(version, basedOn, epochOffset is null ? null : DateTimeOffset.UnixEpoch);

        var result = await fixture.Service(db).SubmitAsync(
            new ActualCorrectionSubmission(ProjectId, ActualChangeEntryPoint.Editor, new[] { Row(db, StageCodes.FS, start: Jan(4)) }, baseline),
            TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, result.Outcome);
        Assert.Equal(ActualCorrectionStaleReason.MissingBaseline, result.StaleReason);
        await AssertNoRevisionsAsync(fixture);
    }

    [Fact]
    public async Task SecondTabPostingNone_AfterARevisionExists_IsStale()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(4));

        await using var db = fixture.NewContext();
        var second = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.FS, start: Jan(3))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, second.Outcome);
        Assert.Equal(ActualCorrectionStaleReason.PendingRevisionChanged, second.StaleReason);
        await using var verify = fixture.NewContext();
        var only = await verify.ActualCorrectionRevisions.SingleAsync();
        Assert.Equal(first.Id, only.Id);
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, only.Status);
    }

    [Fact]
    public async Task FormBasedOnAWithdrawnOrSupersededRevision_IsStale()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(4));
        var second = await SubmitAsync(fixture, Based(first), Lead, StageCodes.FS, Jan(3));

        await using var db = fixture.NewContext();
        var basedOnSuperseded = await fixture.Service(db).SubmitAsync(
            Editor(Based(first), Row(db, StageCodes.FS, start: Jan(2))), TestActors.ProjectOfficer(Lead));
        var withdrawn = await fixture.Service(db).WithdrawAsync(ProjectId, second.Id, second.SubmittedOn, TestActors.ProjectOfficer(Lead));
        var basedOnWithdrawn = await fixture.Service(db).SubmitAsync(
            Editor(Based(second), Row(db, StageCodes.FS, start: Jan(2))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, basedOnSuperseded.Outcome);
        Assert.Equal(ActualCorrectionWithdrawalOutcome.Withdrawn, withdrawn.Outcome);
        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, basedOnWithdrawn.Outcome);
        await using var verify = fixture.NewContext();
        Assert.Equal(2, await verify.ActualCorrectionRevisions.CountAsync());
        Assert.False(await verify.ActualCorrectionRevisions.AnyAsync(item => item.Status == ActualCorrectionRevisionStatus.Pending));
    }

    // SECTION: Pending lifecycle contradiction (F.6)
    [Fact]
    public async Task ProposalContradictingAPendingLifecycleRequest_IsRefused_UnrelatedRequestsDoNotBlock()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), InProgress(StageCodes.BID, Feb(1)));
        await fixture.AddPendingLifecycleRequestAsync(StageCodes.IPA, StageStatus.NotStarted);
        await using var db = fixture.NewContext();

        var contradicting = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.IPA, start: Jan(11))), TestActors.ProjectOfficer(Lead));
        var unrelated = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.BID, start: Feb(2))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, contradicting.Outcome);
        Assert.Contains(contradicting.Errors!, error => error.Contains("pending lifecycle update for IPA", StringComparison.Ordinal));
        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, unrelated.Outcome);
    }

    [Fact]
    public async Task ProposalBreakingChronologyAgainstAPendingPredecessorCompletion_IsRefused()
    {
        // Official facts allow SOW to start on 14 Jan; the pending IPA completion on 15 Jan would not.
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), InProgress(StageCodes.SOW, Jan(16)));
        await fixture.AddPendingLifecycleRequestAsync(StageCodes.IPA, StageStatus.Completed, Jan(15));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.SOW, start: Jan(14))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.Contains("pending lifecycle update for IPA", StringComparison.Ordinal));
        await AssertNoRevisionsAsync(fixture);
    }

    // SECTION: Revision management
    [Fact]
    public async Task EquivalentResubmission_IsUnchangedAndConsumesNoRevisionNumber()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)), InProgress(StageCodes.BID, Feb(1)));
        await using var db = fixture.NewContext();
        var first = await fixture.Service(db).SubmitAsync(
            Editor(None, "Recorded late", new[]
            {
                Row(db, StageCodes.FS, start: Jan(4)) with { Note = "From the register" },
                Row(db, StageCodes.BID, start: Feb(2))
            }),
            TestActors.ProjectOfficer(Lead));

        // Reordered rows, padded notes and Windows line endings are the same content.
        var again = await fixture.Service(db).SubmitAsync(
            Editor(Based(first.Revision!), " Recorded late \r\n", new[]
            {
                Row(db, StageCodes.BID, start: Feb(2)) with { Note = "   " },
                Row(db, StageCodes.FS, start: Jan(4)) with { Note = "From the register " }
            }),
            TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Unchanged, again.Outcome);
        Assert.Equal(first.Revision, again.Revision);
        await using var verify = fixture.NewContext();
        Assert.Equal(1, await verify.ActualCorrectionRevisions.CountAsync());
    }

    [Fact]
    public async Task SuccessorRevision_SupersedesThePendingOneWithLineageAndImmutableEvidence()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)), InProgress(StageCodes.BID, Feb(1)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(4));
        RevisionSnapshot before;
        await using (var read = fixture.NewContext())
        {
            before = RevisionSnapshot.Of(await read.ActualCorrectionRevisions.Include(item => item.Items).SingleAsync());
        }

        await using var db = fixture.NewContext();
        var second = await fixture.Service(db).SubmitAsync(
            Editor(Based(first), Row(db, StageCodes.BID, start: Feb(2))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, second.Outcome);
        Assert.Equal(first, second.SupersededRevision);
        await using var verify = fixture.NewContext();
        var revisions = await verify.ActualCorrectionRevisions.Include(item => item.Items).OrderBy(item => item.RevisionNo).ToListAsync();
        Assert.Equal(2, revisions.Count);
        var (old, current) = (revisions[0], revisions[1]);

        Assert.Equal(ActualCorrectionRevisionStatus.Superseded, old.Status);
        Assert.Equal(Lead, old.ClosedByUserId);
        Assert.Equal(current.SubmittedOn, old.ClosedOn);
        Assert.Equal(current.Id, old.SupersededByRevisionId);
        Assert.Null(old.DecisionNote);
        // Only the closure fields changed; the submitted evidence is intact.
        Assert.Equal(before, RevisionSnapshot.Of(old));

        Assert.Equal(ActualCorrectionRevisionStatus.Pending, current.Status);
        Assert.Equal(2, current.RevisionNo);
        Assert.Equal(old.Id, current.SupersedesRevisionId);
        // The editor posts the complete proposal: the FS item left at its official value is dropped.
        Assert.Equal(StageCodes.BID, Assert.Single(current.Items).StageCode);
    }

    [Fact]
    public async Task ChangedOfficialBaseline_MakesAnIdenticalProposalANewRevision()
    {
        await using var fixture = await Fixture.CreateAsync(Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(14)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.IPA, Jan(11));
        await fixture.ChangeStoredStartAsync(StageCodes.IPA, Jan(13));

        // The PO's fresh form carries the new token; the old revision's baseline no longer matches.
        var second = await SubmitAsync(fixture, Based(first), Lead, StageCodes.IPA, Jan(11));

        Assert.NotEqual(first.Id, second.Id);
        await using var verify = fixture.NewContext();
        var current = await verify.ActualCorrectionItems.SingleAsync(item => item.RevisionId == second.Id);
        Assert.Equal(Jan(13), current.BeforeActualStart);
    }

    [Fact]
    public async Task NewLeadPo_MaySupersedeTheFormerLeadsPendingRevisionOnlyFromItsBaseline()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(4));
        await fixture.ReassignLeadAsync(Other);

        await using var db = fixture.NewContext();
        var blind = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.FS, start: Jan(3))), TestActors.ProjectOfficer(Other));
        var informed = await fixture.Service(db).SubmitAsync(
            Editor(Based(first), Row(db, StageCodes.FS, start: Jan(3))), TestActors.ProjectOfficer(Other));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, blind.Outcome);
        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, informed.Outcome);
        await using var verify = fixture.NewContext();
        var old = await verify.ActualCorrectionRevisions.SingleAsync(item => item.Id == first.Id);
        Assert.Equal(Lead, old.SubmittedByUserId);
        Assert.Equal(Other, old.ClosedByUserId);
        Assert.Equal(Other, (await verify.ActualCorrectionRevisions.SingleAsync(item => item.Id == informed.Revision!.Id)).SubmittedByUserId);
    }

    [Fact]
    public async Task FailedSuccessorInsert_RollsBackTheSupersessionAndCannotBeReplayed()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(4));

        await using var db = fixture.NewContext(failItemInsert: true);
        var submission = Editor(Based(first), Row(db, StageCodes.FS, start: Jan(3)));
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service(db).SubmitAsync(submission, TestActors.ProjectOfficer(Lead)));

        Assert.Empty(db.ChangeTracker.Entries<ActualCorrectionRevision>());
        Assert.Empty(db.ChangeTracker.Entries<ActualCorrectionItem>());
        await using var verify = fixture.NewContext();
        var only = await verify.ActualCorrectionRevisions.SingleAsync();
        Assert.Equal(first.Id, only.Id);
        Assert.Equal(ActualCorrectionRevisionStatus.Pending, only.Status);
        Assert.Null(only.ClosedOn);
        Assert.Null(only.ClosedByUserId);
        Assert.Null(only.SupersededByRevisionId);
    }

    [Fact]
    public async Task BackfillEntry_CarriesForwardPendingItemsAndTheirNote()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(12)), AwaitingBackfill(StageCodes.BID, Feb(1)));
        await using var db = fixture.NewContext();
        var first = await fixture.Service(db).SubmitAsync(
            Editor(None, "Register correction", Row(db, StageCodes.IPA, start: Jan(11))),
            TestActors.ProjectOfficer(Lead));

        var second = await fixture.Service(db).SubmitAsync(
            Backfill(Based(first.Revision!), Row(db, StageCodes.BID, completed: Feb(10))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, second.Outcome);
        await using var verify = fixture.NewContext();
        var revision = await verify.ActualCorrectionRevisions.Include(item => item.Items).SingleAsync(item => item.Id == second.Revision!.Id);
        Assert.Equal(ActualCorrectionEntryPoint.Backfill, revision.EntryPoint);
        Assert.Equal("Register correction", revision.Note);
        Assert.Equal(
            new[] { (StageCodes.BID, ActualCorrectionItemKind.Backfill), (StageCodes.IPA, ActualCorrectionItemKind.Correction) },
            revision.Items.OrderBy(item => item.StageCode).Select(item => (item.StageCode, item.Kind)).ToArray());
    }

    [Fact]
    public async Task BackfillEntry_IsValidatedTogetherWithTheCarriedItems()
    {
        // BID completed on 15 Jan is before the official AON completion (20 Jan) but
        // after the pending AON correction (14 Jan), so the combined proposal is valid.
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.AON, Jan(1), Jan(20)), AwaitingBackfill(StageCodes.BID, null));
        await using var db = fixture.NewContext();
        var first = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.AON, completed: Jan(14))), TestActors.ProjectOfficer(Lead));
        var combined = await fixture.Service(db).SubmitAsync(
            Backfill(Based(first.Revision!), Row(db, StageCodes.BID, completed: Jan(15))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, combined.Outcome);
        await using var verify = fixture.NewContext();
        Assert.Equal(2, await verify.ActualCorrectionItems.CountAsync(item => item.RevisionId == combined.Revision!.Id));
    }

    [Fact]
    public async Task BackfillEntry_ConflictingWithACarriedItem_IsRefused()
    {
        // BID completed on 22 Jan is valid against the official AON completion (20 Jan)
        // but not against the pending AON correction (25 Jan).
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.AON, Jan(1), Jan(20)), AwaitingBackfill(StageCodes.BID, null));
        await using var db = fixture.NewContext();
        var first = await fixture.Service(db).SubmitAsync(
            Editor(None, Row(db, StageCodes.AON, completed: Jan(25))), TestActors.ProjectOfficer(Lead));

        var result = await fixture.Service(db).SubmitAsync(
            Backfill(Based(first.Revision!), Row(db, StageCodes.BID, completed: Jan(22))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.StartsWith("BID:", StringComparison.Ordinal));
        await using var verify = fixture.NewContext();
        Assert.Equal(1, await verify.ActualCorrectionRevisions.CountAsync());
    }

    [Fact]
    public async Task BackfillEntry_ForAStageWithoutChronologyRelief_IsRefusedAgainstOfficialFacts()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.AON, Jan(1), Jan(20)), AwaitingBackfill(StageCodes.BID, null));
        await using var db = fixture.NewContext();

        var result = await fixture.Service(db).SubmitAsync(
            Backfill(None, Row(db, StageCodes.BID, completed: Jan(15))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, result.Outcome);
        await AssertNoRevisionsAsync(fixture);
    }

    [Fact]
    public async Task BackfillEntry_WithACarriedItemWhoseOfficialFactsChanged_IsStale()
    {
        await using var fixture = await Fixture.CreateAsync(
            Completed(StageCodes.FS, Jan(1), Jan(10)), InProgress(StageCodes.IPA, Jan(14)), AwaitingBackfill(StageCodes.BID, Feb(1)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.IPA, Jan(11));
        await fixture.ChangeStoredStartAsync(StageCodes.IPA, Jan(13));

        await using var db = fixture.NewContext();
        var result = await fixture.Service(db).SubmitAsync(
            Backfill(Based(first), Row(db, StageCodes.BID, completed: Feb(10))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Stale, result.Outcome);
        Assert.Equal(ActualCorrectionStaleReason.CarriedItemsChanged, result.StaleReason);
        Assert.Equal(new[] { StageCodes.IPA }, result.StageCodes);
    }

    [Fact]
    public async Task EmptyResubmission_PointsToWithdraw()
    {
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(4));

        await using var db = fixture.NewContext();
        var result = await fixture.Service(db).SubmitAsync(
            Editor(Based(first), Row(db, StageCodes.FS, start: Jan(5))), TestActors.ProjectOfficer(Lead));

        Assert.Equal(ActualCorrectionSubmissionOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Errors!, error => error.Contains("Use Withdraw", StringComparison.Ordinal));
    }

    // SECTION: Withdrawal
    [Fact]
    public async Task Withdraw_ClosesTheExactPendingRevisionOnly()
    {
        var audit = new RecordingAudit();
        await using var fixture = await Fixture.CreateAsync(InProgress(StageCodes.FS, Jan(5)));
        var first = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(4));

        await using var db = fixture.NewContext();
        var service = fixture.Service(db, audit);
        var wrongEpoch = await service.WithdrawAsync(ProjectId, first.Id, first.SubmittedOn.AddTicks(10), TestActors.ProjectOfficer(Lead));
        var hod = await service.WithdrawAsync(ProjectId, first.Id, first.SubmittedOn, TestActors.Hod(Other));
        var withdrawn = await service.WithdrawAsync(ProjectId, first.Id, first.SubmittedOn, TestActors.ProjectOfficer(Lead), "  Entered in error ");
        var again = await service.WithdrawAsync(ProjectId, first.Id, first.SubmittedOn, TestActors.ProjectOfficer(Lead));
        var next = await SubmitAsync(fixture, None, Lead, StageCodes.FS, Jan(3));

        Assert.Equal(ActualCorrectionWithdrawalOutcome.NotActionable, wrongEpoch.Outcome);
        Assert.Equal(ActualCorrectionWithdrawalOutcome.DirectAuthority, hod.Outcome);
        Assert.Equal(ActualCorrectionWithdrawalOutcome.Withdrawn, withdrawn.Outcome);
        Assert.Equal(ActualCorrectionWithdrawalOutcome.NotActionable, again.Outcome);
        Assert.Equal(2, next.RevisionNo);
        Assert.Equal("Projects.ActualCorrectionWithdrawn", Assert.Single(audit.Entries).Action);

        await using var verify = fixture.NewContext();
        var old = await verify.ActualCorrectionRevisions.SingleAsync(item => item.Id == first.Id);
        Assert.Equal(ActualCorrectionRevisionStatus.Withdrawn, old.Status);
        Assert.Equal(Lead, old.ClosedByUserId);
        Assert.Equal("Entered in error", old.DecisionNote);
        Assert.Null(old.SupersededByRevisionId);
        Assert.Null((await verify.ActualCorrectionRevisions.SingleAsync(item => item.Id == next.Id)).SupersedesRevisionId);
        await AssertOfficialUnchangedAsync(fixture, StageCodes.FS, StageStatus.InProgress, Jan(5), null, requiresBackfill: false);
    }

    // SECTION: Canonical content hash
    [Fact]
    public void ContentHash_IsStableForEquivalentContentAndSensitiveToMeaningfulChanges()
    {
        static ActualCorrectionItem Item(string code, DateOnly? start, bool clears = false, string? note = null) =>
            new() { StageCode = code, Kind = ActualCorrectionItemKind.Correction, ProposedActualStart = start, ClearsActualStart = clears, Note = note };

        var baseline = ActualCorrectionProposalService.ComputeContentHash(new[] { Item("FS", Jan(4)), Item("IPA", Jan(11)) }, "note");

        Assert.Equal(baseline, ActualCorrectionProposalService.ComputeContentHash(new[] { Item("ipa", Jan(11), note: " "), Item("FS", Jan(4)) }, " note\r\n"));
        Assert.NotEqual(baseline, ActualCorrectionProposalService.ComputeContentHash(new[] { Item("FS", Jan(4)), Item("IPA", Jan(10)) }, "note"));
        Assert.NotEqual(baseline, ActualCorrectionProposalService.ComputeContentHash(new[] { Item("FS", Jan(4)), Item("IPA", Jan(11)) }, null));
        Assert.NotEqual(
            ActualCorrectionProposalService.ComputeContentHash(new[] { Item("FS", null) }, null),
            ActualCorrectionProposalService.ComputeContentHash(new[] { Item("FS", null, clears: true) }, null));
    }

    // SECTION: Helpers
    private static readonly ActualCorrectionBaseline None = new(ActualStageToken.BaselineVersion);

    private static ActualCorrectionBaseline Based(ActualCorrectionRevisionIdentity revision) =>
        new(ActualStageToken.BaselineVersion, revision.Id, revision.SubmittedOn);

    private static async Task<ActualCorrectionRevisionIdentity> SubmitAsync(
        Fixture fixture,
        ActualCorrectionBaseline baseline,
        string userId,
        string stageCode,
        DateOnly start)
    {
        await using var db = fixture.NewContext();
        var result = await fixture.Service(db).SubmitAsync(
            Editor(baseline, Row(db, stageCode, start: start)), TestActors.ProjectOfficer(userId));
        Assert.Equal(ActualCorrectionSubmissionOutcome.Submitted, result.Outcome);
        return result.Revision!;
    }

    private static ActualCorrectionSubmission Editor(ActualCorrectionBaseline baseline, params ActualCorrectionProposalRow[] rows) =>
        new(ProjectId, ActualChangeEntryPoint.Editor, rows, baseline);

    private static ActualCorrectionSubmission Editor(ActualCorrectionBaseline baseline, string note, params ActualCorrectionProposalRow[] rows) =>
        new(ProjectId, ActualChangeEntryPoint.Editor, rows, baseline, note);

    private static ActualCorrectionSubmission Backfill(ActualCorrectionBaseline baseline, params ActualCorrectionProposalRow[] rows) =>
        new(ProjectId, ActualChangeEntryPoint.Backfill, rows, baseline);

    // A row as the form would post it, with the token of the row as rendered now.
    private static ActualCorrectionProposalRow Row(
        ApplicationDbContext db,
        string code,
        DateOnly? start = null,
        DateOnly? completed = null,
        bool clear = false)
    {
        var stage = db.ProjectStages.AsNoTracking().Single(item => item.ProjectId == ProjectId && item.StageCode == code);
        return new ActualCorrectionProposalRow(code, start, clear, completed, ActualStageToken.Compute(stage));
    }

    private static async Task AssertNoRevisionsAsync(Fixture fixture)
    {
        await using var verify = fixture.NewContext();
        Assert.False(await verify.ActualCorrectionRevisions.AnyAsync());
        Assert.False(await verify.ActualCorrectionItems.AnyAsync());
        Assert.False(await verify.StageChangeLogs.AnyAsync());
    }

    private static async Task AssertOfficialUnchangedAsync(
        Fixture fixture,
        string code,
        StageStatus status,
        DateOnly? start,
        DateOnly? completed,
        bool requiresBackfill)
    {
        await using var verify = fixture.NewContext();
        var stage = await verify.ProjectStages.AsNoTracking().SingleAsync(item => item.StageCode == code);
        Assert.Equal(status, stage.Status);
        Assert.Equal(start, stage.ActualStart);
        Assert.Equal(completed, stage.CompletedOn);
        Assert.Equal(requiresBackfill, stage.RequiresBackfill);
        Assert.False(await verify.StageChangeLogs.AnyAsync());
    }

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

    /// <summary>The submitted evidence of a revision, excluding its closure fields.</summary>
    private sealed record RevisionSnapshot(
        int RevisionNo,
        string SubmittedByUserId,
        DateTimeOffset SubmittedOn,
        string? Note,
        string ContentHash,
        int? SupersedesRevisionId,
        string Items)
    {
        public static RevisionSnapshot Of(ActualCorrectionRevision revision) => new(
            revision.RevisionNo,
            revision.SubmittedByUserId,
            revision.SubmittedOn,
            revision.Note,
            revision.ContentHash,
            revision.SupersedesRevisionId,
            string.Join(";", revision.Items.OrderBy(item => item.StageCode).Select(item =>
                $"{item.Id}|{item.StageCode}|{item.Kind}|{item.BeforeStatus}|{item.BeforeActualStart}|{item.BeforeCompletedOn}|" +
                $"{item.BeforeRequiresBackfill}|{item.ProposedActualStart}|{item.ProposedCompletedOn}|{item.ClearsActualStart}|{item.Note}")));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ArppIpaStageAuthority? _authority;
        private readonly FakeClock _clock = FakeClock.ForIstDate(Today);

        private Fixture(SqliteConnection connection, ArppIpaStageAuthority? authority)
        {
            _connection = connection;
            _authority = authority;
        }

        public static Task<Fixture> CreateAsync(params ProjectStage[] stages) => CreateAsync(stages, null);

        public static async Task<Fixture> CreateAsync(
            ProjectStage[] stages,
            Action<Project>? project = null,
            ArppIpaStageAuthority? authority = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection, authority);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            foreach (var userId in new[] { "seed", Lead, Other })
            {
                db.Users.Add(new ApplicationUser { Id = userId, UserName = userId });
            }

            var row = new Project
            {
                Id = ProjectId,
                Name = "Actual proposal project",
                CreatedByUserId = "seed",
                LeadPoUserId = Lead,
                WorkflowVersion = ProcurementWorkflow.VersionV1
            };
            project?.Invoke(row);
            db.Projects.Add(row);
            db.ProjectStages.AddRange(stages);
            await db.SaveChangesAsync();
            return fixture;
        }

        public ApplicationDbContext NewContext(bool failItemInsert = false)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection);
            if (failItemInsert)
            {
                builder.AddInterceptors(new FailItemInsertInterceptor());
            }

            return new ApplicationDbContext(builder.Options);
        }

        public ActualCorrectionProposalService Service(ApplicationDbContext db, RecordingAudit? audit = null)
        {
            // Each submission gets a distinct epoch, as real submissions do.
            _clock.Set(_clock.UtcNow.AddSeconds(1));
            return new ActualCorrectionProposalService(db, _clock, new StubArppAuthority(_authority),
                StageWorkflowTestFactory.CreatePolicy(db), audit: audit ?? new RecordingAudit());
        }

        public async Task ChangeStoredStartAsync(string code, DateOnly start)
        {
            await using var db = NewContext();
            var stage = await db.ProjectStages.SingleAsync(item => item.StageCode == code);
            stage.ActualStart = start;
            await db.SaveChangesAsync();
        }

        public async Task ReassignLeadAsync(string leadPoUserId)
        {
            await using var db = NewContext();
            var project = await db.Projects.SingleAsync(item => item.Id == ProjectId);
            project.LeadPoUserId = leadPoUserId;
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

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    /// <summary>Fails the save that inserts proposal items, after the previous revision was closed.</summary>
    private sealed class FailItemInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<ActualCorrectionItem>().Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new DbUpdateException("Injected proposal-item failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
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
