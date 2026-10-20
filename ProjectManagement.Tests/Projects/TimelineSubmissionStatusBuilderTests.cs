using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

/// <summary>PO Timeline discovery E.1: one status line per category.</summary>
public sealed class TimelineSubmissionStatusBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingToReport_ProducesNoLines()
    {
        var status = TimelineSubmissionStatusBuilder.Build(new TimelineSubmissionStatusInput
        {
            Now = Now,
            Plan = new PlanEditorStateVm(),
            IncludeActualCorrections = true
        });

        Assert.False(status.HasLines);
        Assert.Same(TimelineSubmissionStatusVm.Empty, status);
    }

    [Fact]
    public void PlanAwaitingApproval_IsShownWithTheSubmissionDateAndOpensThePlanner()
    {
        var status = Build(plan: new PlanEditorStateVm
        {
            HasPendingSubmission = true,
            PendingOwnedByCurrentUser = true,
            PendingSubmittedOn = new DateTimeOffset(2026, 10, 14, 5, 0, 0, TimeSpan.Zero)
        });

        var entry = Single(status, TimelineSubmissionStatusVm.PlannedDatesCategory);
        Assert.Equal(TimelineSubmissionState.Pending, entry.State);
        Assert.Equal("Awaiting HoD approval (submitted 14 Oct 2026).", entry.Text);
        Assert.Equal("#offcanvasPlanEdit", entry.OffcanvasTarget);
        Assert.True(status.Shows(TimelineSubmissionStatusVm.PlannedDatesCategory, TimelineSubmissionState.Pending));
    }

    [Fact]
    public void AnotherUsersPendingPlan_IsNotTheViewersItem()
    {
        var status = Build(plan: new PlanEditorStateVm { HasPendingSubmission = true, PendingOwnedByCurrentUser = false });

        Assert.False(status.HasLines);
    }

    [Fact]
    public void ReturnedPlan_CarriesTheReviewersNote_UntilTheWindowCloses()
    {
        var recent = Build(plan: new PlanEditorStateVm
        {
            HasMyDraft = true,
            RejectedOn = Now.AddDays(-4),
            RejectionNote = "  Allow two more weeks.  "
        });
        var entry = Single(recent, TimelineSubmissionStatusVm.PlannedDatesCategory);
        Assert.Equal(TimelineSubmissionState.Returned, entry.State);
        Assert.Equal("Returned on 16 Oct 2026.", entry.Text);
        Assert.Equal("Allow two more weeks.", entry.ReviewerNote);
        Assert.Equal("Review", entry.ActionLabel);

        var old = Build(plan: new PlanEditorStateVm { RejectedOn = Now.AddDays(-31), RejectionNote = "Old" });
        Assert.False(old.HasLines);
    }

    [Fact]
    public void PendingActualCorrection_OutranksAnEarlierRejection()
    {
        var status = Build(
            includeActuals: true,
            pendingActual: new ActualProposalSummaryVm(5, 2, Now.AddDays(-1), null, true, new[] { StageCodes.IPA }),
            latestActual: new ActualCorrectionRevision { SubmittedByUserId = "po", Status = ActualCorrectionRevisionStatus.Rejected, ClosedOn = Now.AddDays(-3) });

        var entry = Single(status, TimelineSubmissionStatusVm.ActualDatesCategory);
        Assert.Equal(TimelineSubmissionState.Pending, entry.State);
        Assert.Equal("Awaiting HoD or Admin approval (submitted 19 Oct 2026).", entry.Text);
        Assert.Equal("#offcanvasActualDates", entry.OffcanvasTarget);
    }

    [Fact]
    public void ActualCorrection_ReturnedAndApprovedStatesUseTheirOwnWindows()
    {
        var returned = Single(Build(includeActuals: true, latestActual: new ActualCorrectionRevision
        {
            SubmittedByUserId = "po",
            Status = ActualCorrectionRevisionStatus.Rejected,
            ClosedOn = Now.AddDays(-4),
            DecisionNote = "IPA start conflicts with SOW."
        }), TimelineSubmissionStatusVm.ActualDatesCategory);
        Assert.Equal(TimelineSubmissionState.Returned, returned.State);
        Assert.Equal("Correction returned on 16 Oct 2026.", returned.Text);
        Assert.Equal("IPA start conflicts with SOW.", returned.ReviewerNote);

        var approved = Single(Build(includeActuals: true, latestActual: new ActualCorrectionRevision
        {
            SubmittedByUserId = "po",
            Status = ActualCorrectionRevisionStatus.Approved,
            ClosedOn = Now.AddDays(-2)
        }), TimelineSubmissionStatusVm.ActualDatesCategory);
        Assert.Equal(TimelineSubmissionState.Approved, approved.State);
        Assert.Null(approved.ActionLabel);

        Assert.False(Build(includeActuals: true, latestActual: new ActualCorrectionRevision
        {
            SubmittedByUserId = "po",
            Status = ActualCorrectionRevisionStatus.Approved,
            ClosedOn = Now.AddDays(-8)
        }).HasLines);
        Assert.False(Build(includeActuals: true, latestActual: new ActualCorrectionRevision
        {
            SubmittedByUserId = "po",
            Status = ActualCorrectionRevisionStatus.Withdrawn,
            ClosedOn = Now.AddDays(-1)
        }).HasLines);
    }

    [Fact]
    public void ActualCorrections_AreHiddenFromUsersWhoCannotSubmitThem()
    {
        var status = Build(
            includeActuals: false,
            pendingActual: new ActualProposalSummaryVm(5, 2, Now.AddDays(-1), null, false, new[] { StageCodes.IPA }));

        Assert.False(status.HasLines);
    }

    [Fact]
    public void StageUpdates_ListReturnedFirstThenPendingThenApproved()
    {
        var status = Build(stageRequests: new[]
        {
            Outcome(StageCodes.FS, "Feasibility Study", "Completed", "Approved", Now.AddDays(-1)),
            Outcome(StageCodes.SOW, "SOW Vetting", "Completed", "Pending", null),
            Outcome(StageCodes.TEC, "Technical Evaluation", "Completed", "Rejected", Now.AddDays(-3), "Attach trial report first.")
        });

        var line = status.Lines.Single(item => item.Category == TimelineSubmissionStatusVm.StageUpdatesCategory);
        Assert.Equal(
            new[] { TimelineSubmissionState.Returned, TimelineSubmissionState.Pending, TimelineSubmissionState.Approved },
            line.Entries.Select(entry => entry.State).ToArray());
        Assert.Equal("Completion of Technical Evaluation returned on 17 Oct 2026.", line.Entries[0].Text);
        Assert.Equal("Attach trial report first.", line.Entries[0].ReviewerNote);
        Assert.Equal(StageCodes.TEC, line.Entries[0].StageCode);
        Assert.StartsWith("Completion of SOW Vetting awaiting HoD approval", line.Entries[1].Text, StringComparison.Ordinal);
        Assert.Equal("Completion of Feasibility Study approved on 19 Oct 2026.", line.Entries[2].Text);
        Assert.Equal(0, line.HiddenCount);
    }

    [Fact]
    public void ReturnedStageUpdate_IsDroppedOnceTheStageAlreadyHasTheRequestedStatus()
    {
        var status = Build(
            stageRequests: new[] { Outcome(StageCodes.TEC, "Technical Evaluation", "Completed", "Rejected", Now.AddDays(-3)) },
            stageStatuses: new Dictionary<string, StageStatus>(StringComparer.OrdinalIgnoreCase)
            {
                [StageCodes.TEC] = StageStatus.Completed
            });

        Assert.False(status.HasLines);
    }

    [Fact]
    public void StageUpdates_BeyondTheLimitAreCounted()
    {
        var requests = new[] { StageCodes.FS, StageCodes.IPA, StageCodes.SOW, StageCodes.AON, StageCodes.BID }
            .Select(code => Outcome(code, code, "Completed", "Rejected", Now.AddDays(-2)))
            .ToArray();

        var line = Build(stageRequests: requests).Lines.Single();

        Assert.Equal(TimelineSubmissionStatusBuilder.MaxStageEntries, line.Entries.Count);
        Assert.Equal(2, line.HiddenCount);
    }

    [Fact]
    public void ActualCorrections_SubmittedByAPreviousPo_AreNotListedAsTheViewers()
    {
        Assert.False(Build(includeActuals: true,
            pendingActual: new ActualProposalSummaryVm(5, 2, Now.AddDays(-1), null, false, new[] { StageCodes.IPA })).HasLines);
        Assert.False(Build(includeActuals: true, latestActual: new ActualCorrectionRevision
        {
            SubmittedByUserId = "previous-po",
            Status = ActualCorrectionRevisionStatus.Rejected,
            ClosedOn = Now.AddDays(-2),
            DecisionNote = "Not yours"
        }).HasLines);
    }

    [Theory]
    [InlineData("InProgress", StageStatus.Completed)]
    [InlineData("InProgress", StageStatus.Skipped)]
    [InlineData("Blocked", StageStatus.Completed)]
    [InlineData("Completed", StageStatus.Completed)]
    public void ReturnedStageUpdate_IsDroppedOnceTheStageHasMovedPastIt(string requested, StageStatus current)
    {
        var status = Build(
            stageRequests: new[] { Outcome(StageCodes.TEC, "Technical Evaluation", requested, "Rejected", Now.AddDays(-3)) },
            stageStatuses: new Dictionary<string, StageStatus>(StringComparer.OrdinalIgnoreCase) { [StageCodes.TEC] = current });

        Assert.False(status.HasLines);
    }

    [Fact]
    public void ReturnedCompletion_StaysWhileTheStageIsStillInProgress()
    {
        var status = Build(
            stageRequests: new[] { Outcome(StageCodes.TEC, "Technical Evaluation", "Completed", "Rejected", Now.AddDays(-3)) },
            stageStatuses: new Dictionary<string, StageStatus>(StringComparer.OrdinalIgnoreCase) { [StageCodes.TEC] = StageStatus.InProgress });

        Assert.True(status.Shows(TimelineSubmissionStatusVm.StageUpdatesCategory, TimelineSubmissionState.Returned));
    }

    [Fact]
    public void OwnPendingStageUpdates_AreCountedEvenWhenNotAllAreListed()
    {
        var requests = new[] { StageCodes.FS, StageCodes.IPA, StageCodes.SOW, StageCodes.AON }
            .Select(code => Outcome(code, code, "Completed", "Pending", null))
            .ToArray();

        var status = Build(stageRequests: requests);

        Assert.Equal(4, status.OwnPendingStageUpdateCount);
        Assert.Equal(TimelineSubmissionStatusBuilder.MaxStageEntries, status.Lines.Single().Entries.Count);
    }

    [Theory]
    [InlineData("Completed", "Completion")]
    [InlineData("InProgress", "Start")]
    [InlineData("Blocked", "Blocked status")]
    [InlineData("Skipped", "Skip")]
    [InlineData("NotStarted", "Reopening")]
    [InlineData("Unknown", "Update")]
    public void StageRequestNouns_UseWordsNotEnumValues(string requested, string expected)
    {
        Assert.Equal(expected, TimelineSubmissionStatusBuilder.ActionNoun(requested));
    }

    private static TimelineSubmissionStatusVm Build(
        PlanEditorStateVm? plan = null,
        bool includeActuals = false,
        ActualProposalSummaryVm? pendingActual = null,
        ActualCorrectionRevision? latestActual = null,
        IReadOnlyList<StageRequestOutcomeVm>? stageRequests = null,
        IReadOnlyDictionary<string, StageStatus>? stageStatuses = null) =>
        TimelineSubmissionStatusBuilder.Build(new TimelineSubmissionStatusInput
        {
            Now = Now,
            CurrentUserId = "po",
            Plan = plan,
            IncludeActualCorrections = includeActuals,
            PendingActualCorrection = pendingActual,
            LatestActualCorrection = latestActual,
            StageRequests = stageRequests ?? Array.Empty<StageRequestOutcomeVm>(),
            CurrentStageStatuses = stageStatuses ?? new Dictionary<string, StageStatus>(StringComparer.OrdinalIgnoreCase)
        });

    private static TimelineSubmissionEntryVm Single(TimelineSubmissionStatusVm status, string category) =>
        Assert.Single(Assert.Single(status.Lines, line => line.Category == category).Entries);

    private static StageRequestOutcomeVm Outcome(
        string code, string name, string requested, string decision, DateTimeOffset? decidedOn, string? note = null) =>
        new(code, name, requested, decision, Now.AddDays(-5), decidedOn, note);
}
