using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

public sealed class ActualTimelineValidationServiceTests
{
    private static readonly DateOnly Today = new(2026, 3, 31);
    private static readonly IWorkflowStageMetadataProvider Metadata = new WorkflowStageMetadataProvider();

    // SECTION: Whole-lifecycle final state (rule 8 / cold-review F07)
    [Fact]
    public void PredecessorCorrection_PastUntouchedSuccessor_IsRejectedOnTheSuccessor()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                InProgress(StageCodes.IPA, Jan(11))
            },
            Change(StageCodes.FS, completedOn: Jan(20)));

        var error = Assert.Single(result.Errors);
        Assert.Equal(StageCodes.IPA, error.StageCode);
        Assert.Equal(StageCodes.FS, error.RelatedStageCode);
        Assert.Equal(ActualValidationRule.Chronology, error.Rule);
        Assert.Contains("11 Jan 2026", error.Message, StringComparison.Ordinal);
        Assert.Contains("20 Jan 2026", error.Message, StringComparison.Ordinal);
        Assert.Contains("Same-day commencement is permitted", error.Message, StringComparison.Ordinal);
        Assert.Contains(StageCodes.IPA, result.AffectedStageCodes);
    }

    [Fact]
    public void CoordinatedCorrection_OfPredecessorAndSuccessor_IsAcceptedAgainstTheFinalState()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                InProgress(StageCodes.IPA, Jan(11))
            },
            Change(StageCodes.FS, completedOn: Jan(20)),
            Change(StageCodes.IPA, actualStart: Jan(20)));

        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Equal(2, result.Changes.Count);
        var ipa = result.Final.Single(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(Jan(20), ipa.ActualStart);
        Assert.Equal(StageStatus.InProgress, ipa.Status);
    }

    [Fact]
    public void ChangedSuccessor_BeforePredecessorCompletion_UsesTheExistingEditorMessage()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                InProgress(StageCodes.IPA, Jan(11))
            },
            Change(StageCodes.IPA, actualStart: Jan(9)));

        var error = Assert.Single(result.Errors);
        Assert.Equal(StageCodes.IPA, error.StageCode);
        Assert.Contains("Start date must be on or after 10 Jan 2026", error.Message, StringComparison.Ordinal);
        Assert.Contains(Name(StageCodes.FS), error.Message, StringComparison.Ordinal);
        Assert.Contains("Same-day commencement is permitted", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameDayCommencement_IsValid_AndNextDaySuggestionIsNotAMinimum()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                InProgress(StageCodes.IPA, Jan(11))
            },
            Change(StageCodes.IPA, actualStart: Jan(10)));

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CompletedStageWithoutStart_IsBoundedByItsCompletion()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                Completed(StageCodes.IPA, null, Jan(15))
            },
            Change(StageCodes.IPA, completedOn: Jan(9)));

        var error = Assert.Single(result.Errors);
        Assert.Contains("Completion date must be on or after 10 Jan 2026", error.Message, StringComparison.Ordinal);
    }

    // SECTION: Skipped ancestry and convergence (K-11)
    [Fact]
    public void SkippedPredecessor_IsTransparent_AndItsAncestryBindsTheSuccessor()
    {
        var official = new[]
        {
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, StageStatus.Skipped),
            InProgress(StageCodes.SOW, Jan(12))
        };

        var rejected = Evaluate(official, Change(StageCodes.SOW, actualStart: Jan(9)));
        var error = Assert.Single(rejected.Errors);
        Assert.Equal(StageCodes.FS, error.RelatedStageCode);

        var accepted = Evaluate(official, Change(StageCodes.SOW, actualStart: Jan(10)));
        Assert.True(accepted.IsValid);
    }

    [Fact]
    public void Convergence_DatedBranchStillBinds_WhenAnotherRequiredBranchIsUndated()
    {
        var official = new[]
        {
            Completed(StageCodes.BID, Feb(1), Feb(5)),
            Completed(StageCodes.TEC, Feb(6), Feb(20)),
            CompletedAwaitingBackfill(StageCodes.BM),
            InProgress(StageCodes.COB, Feb(25))
        };

        var result = Evaluate(official, Change(StageCodes.COB, actualStart: Feb(15)));

        var error = Assert.Single(result.Errors);
        Assert.Equal(StageCodes.COB, error.StageCode);
        Assert.Equal(StageCodes.TEC, error.RelatedStageCode);
        Assert.Contains("20 Feb 2026", error.Message, StringComparison.Ordinal);

        // The suggestion API keeps its documented semantics: no suggestion while
        // any required branch is undated (used by reopen prefill, rule 10).
        var suggestion = StageDateSuggestionResolver.Resolve(
            V1(),
            StageCodes.COB,
            code => official.FirstOrDefault(row => row.StageCode == code) is { } match
                ? new StageChronologyState(match.Status, match.CompletedOn)
                : null);
        Assert.False(suggestion.HasStartBoundary);
    }

    [Fact]
    public void Convergence_LatestRequiredBranchControls()
    {
        var official = new[]
        {
            Completed(StageCodes.BID, Feb(1), Feb(5)),
            Completed(StageCodes.TEC, Feb(6), Feb(10)),
            Completed(StageCodes.BM, Feb(6), Feb(20)),
            InProgress(StageCodes.COB, Feb(25))
        };

        var rejected = Evaluate(official, Change(StageCodes.COB, actualStart: Feb(15)));
        var error = Assert.Single(rejected.Errors);
        Assert.Equal(StageCodes.BM, error.RelatedStageCode);

        Assert.True(Evaluate(official, Change(StageCodes.COB, actualStart: Feb(20))).IsValid);
    }

    [Fact]
    public void ApplicablePncBranch_BindsEas_OnlyWhilePncIsApplicable()
    {
        var official = new[]
        {
            Completed(StageCodes.COB, Feb(1), Feb(10)),
            Completed(StageCodes.PNC, Feb(11), Feb(25)),
            InProgress(StageCodes.EAS, Feb(26))
        };
        var change = Change(StageCodes.EAS, actualStart: Feb(20));

        var applicable = ActualTimelineValidationService.Evaluate(
            V1(pncApplicable: true), null, Today, Candidate(official, change));
        Assert.Equal(StageCodes.PNC, Assert.Single(applicable.Errors).RelatedStageCode);

        var notApplicable = ActualTimelineValidationService.Evaluate(
            V1(pncApplicable: false), null, Today, Candidate(official, change));
        Assert.True(notApplicable.IsValid);
    }

    [Fact]
    public void DependencyCycle_InConfiguration_DoesNotLoop()
    {
        var workflow = new ProjectStageWorkflowSnapshot(
            ProcurementWorkflow.VersionV2,
            new[] { new WorkflowStageDefinition("A", "Stage A"), new WorkflowStageDefinition("B", "Stage B") },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = new[] { "B" },
                ["B"] = new[] { "A" }
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            pncApplicable: false);

        var result = ActualTimelineValidationService.Evaluate(
            workflow,
            null,
            Today,
            Candidate(
                new[] { Stage("A", StageStatus.Skipped), InProgress("B", Jan(5)) },
                Change("B", actualStart: Jan(6))));

        Assert.True(result.IsValid);
    }

    // SECTION: Historical data compatibility
    [Fact]
    public void PreExistingViolation_DoesNotBlockAnUnrelatedCorrection_ButIsReported()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                Completed(StageCodes.BID, Feb(1), Feb(5)),
                Completed(StageCodes.TEC, Feb(6), Feb(10)),
                Completed(StageCodes.BM, Feb(6), Feb(8)),
                InProgress(StageCodes.COB, Feb(7))
            },
            Change(StageCodes.FS, actualStart: Jan(2)));

        Assert.True(result.IsValid);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(StageCodes.COB, warning.StageCode);
        Assert.Equal(StageCodes.TEC, warning.RelatedStageCode);
    }

    [Fact]
    public void PreExistingViolation_BlocksWhenTheCorrectionChangesThatSameRelationship()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.BID, Feb(1), Feb(5)),
                Completed(StageCodes.TEC, Feb(6), Feb(10)),
                Completed(StageCodes.BM, Feb(6), Feb(8)),
                InProgress(StageCodes.COB, Feb(7))
            },
            Change(StageCodes.TEC, completedOn: Feb(12)));

        var error = Assert.Single(result.Errors);
        Assert.Equal(StageCodes.COB, error.StageCode);
        Assert.Contains("Correct COB in the same update", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnchangedInvalidRows_AreNotRevalidatedOrRepaired()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(20), Jan(10)),
                InProgress(StageCodes.IPA, Jan(21))
            },
            Change(StageCodes.IPA, actualStart: Jan(22)));

        Assert.True(result.IsValid);
        Assert.Equal(Jan(20), result.Final.Single(stage => stage.StageCode == StageCodes.FS).ActualStart);
    }

    // SECTION: Status/date matrix and input semantics
    [Fact]
    public void InProgress_CannotClearStart_OrCarryACompletionDate()
    {
        var official = new[] { InProgress(StageCodes.FS, Jan(5)) };

        var clear = Assert.Single(Evaluate(official, Change(StageCodes.FS, clearStart: true)).Errors);
        Assert.Equal(ActualValidationRule.StartRequired, clear.Rule);

        var complete = Assert.Single(Evaluate(official, Change(StageCodes.FS, completedOn: Jan(9))).Errors);
        Assert.Equal(ActualValidationRule.CompletionNotAllowed, complete.Rule);
        Assert.Contains("stage-transition", complete.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StageStatus.NotStarted)]
    [InlineData(StageStatus.Skipped)]
    [InlineData(StageStatus.Blocked)]
    public void NonExecutingStatuses_AreNotEditable(StageStatus status)
    {
        var error = Assert.Single(Evaluate(
            new[] { Stage(StageCodes.FS, status) },
            Change(StageCodes.FS, actualStart: Jan(5))).Errors);

        Assert.Equal(ActualValidationRule.StatusNotEditable, error.Rule);
    }

    [Fact]
    public void BlankFields_PreserveRecordedDates_AndProduceNoChange()
    {
        var result = Evaluate(new[] { Completed(StageCodes.FS, Jan(1), Jan(10)) }, Change(StageCodes.FS));

        Assert.True(result.IsValid);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void CompletedStage_MayExplicitlyClearItsStart_ButNotSupplyAndClearTogether()
    {
        var official = new[] { Completed(StageCodes.FS, Jan(1), Jan(10)) };

        var cleared = Evaluate(official, Change(StageCodes.FS, clearStart: true));
        Assert.True(cleared.IsValid);
        Assert.Null(Assert.Single(cleared.Changes).Final.ActualStart);

        var conflicting = Assert.Single(Evaluate(official, Change(StageCodes.FS, actualStart: Jan(2), clearStart: true)).Errors);
        Assert.Equal(ActualValidationRule.ConflictingStartIntent, conflicting.Rule);
    }

    [Fact]
    public void StartAfterCompletion_IsRejected_EvenWhenOneSideIsUnchanged()
    {
        var error = Assert.Single(Evaluate(
            new[] { Completed(StageCodes.FS, Jan(1), Jan(10)) },
            Change(StageCodes.FS, actualStart: Jan(11))).Errors);

        Assert.Equal(ActualValidationRule.StartAfterCompletion, error.Rule);
    }

    [Fact]
    public void FutureDates_AreRejected_TodayIsAccepted()
    {
        var official = new[] { Completed(StageCodes.FS, Mar(1), Mar(10)) };

        Assert.True(Evaluate(official, Change(StageCodes.FS, completedOn: Today)).IsValid);
        Assert.Equal(ActualValidationRule.FutureCompletion,
            Assert.Single(Evaluate(official, Change(StageCodes.FS, completedOn: Today.AddDays(1))).Errors).Rule);
        Assert.Contains(Evaluate(official, Change(StageCodes.FS, actualStart: Today.AddDays(1))).Errors,
            error => error.Rule == ActualValidationRule.FutureStart);
    }

    [Fact]
    public void DuplicateRows_LastSubmissionWins_CaseInsensitively()
    {
        var result = Evaluate(
            new[] { Completed(StageCodes.FS, Jan(1), Jan(10)) },
            Change("fs", actualStart: Jan(2)),
            Change("FS", actualStart: Jan(3)));

        Assert.Equal(Jan(3), Assert.Single(result.Changes).Final.ActualStart);
    }

    [Fact]
    public void UnknownStage_IsReported()
    {
        var error = Assert.Single(Evaluate(
            new[] { Completed(StageCodes.FS, Jan(1), Jan(10)) },
            Change("NOPE", actualStart: Jan(2))).Errors);

        Assert.Equal(ActualValidationRule.UnknownStage, error.Rule);
    }

    // SECTION: Published ARPP IPA authority (rule 17)
    [Fact]
    public void PublishedArpp_ControlsIpaCompletion_ButStartRemainsCorrectable()
    {
        var authority = Authority(Jan(25));
        var official = new[]
        {
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12))
        };

        var completion = Assert.Single(ActualTimelineValidationService.Evaluate(
            V1(), authority, Today, Candidate(official, Change(StageCodes.IPA, completedOn: Jan(20)))).Errors);
        Assert.Equal(ActualValidationRule.PublishedArppCompletion, completion.Rule);

        var start = ActualTimelineValidationService.Evaluate(
            V1(), authority, Today, Candidate(official, Change(StageCodes.IPA, actualStart: Jan(11))));
        Assert.True(start.IsValid);
        var ipa = Assert.Single(start.Changes).Final;
        Assert.Equal(StageStatus.Completed, ipa.Status);
        Assert.Equal(Jan(25), ipa.CompletedOn);
        Assert.False(ipa.RequiresBackfill);
    }

    [Fact]
    public void PublishedArpp_StartAfterIssueDate_IsRejected()
    {
        var error = Assert.Single(ActualTimelineValidationService.Evaluate(
            V1(), Authority(Jan(25)), Today,
            Candidate(new[] { Completed(StageCodes.IPA, Jan(5), Jan(25)) },
                Change(StageCodes.IPA, actualStart: Jan(26)))).Errors);

        Assert.Equal(ActualValidationRule.StartAfterCompletion, error.Rule);
    }

    [Fact]
    public void PublishedArpp_IssueDateBoundsSuccessors_EvenWhenTheStoredRowIsStale()
    {
        var result = ActualTimelineValidationService.Evaluate(
            V1(), Authority(Jan(25)), Today,
            Candidate(
                new[]
                {
                    Completed(StageCodes.IPA, null, Jan(10)),
                    InProgress(StageCodes.SOW, Jan(26))
                },
                Change(StageCodes.SOW, actualStart: Jan(20))));

        var error = Assert.Single(result.Errors);
        Assert.Equal(StageCodes.IPA, error.RelatedStageCode);
        Assert.Contains("25 Jan 2026", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PredecessorCorrection_PastPublishedIpaCompletion_PointsToTheArppRecord()
    {
        var error = Assert.Single(ActualTimelineValidationService.Evaluate(
            V1(), Authority(Jan(15)), Today,
            Candidate(
                new[]
                {
                    Completed(StageCodes.FS, Jan(1), Jan(10)),
                    Completed(StageCodes.IPA, null, Jan(15))
                },
                Change(StageCodes.FS, completedOn: Jan(20)))).Errors);

        Assert.Equal(StageCodes.IPA, error.StageCode);
        Assert.Contains("published ARPP record", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutPublishedArpp_IpaCompletionIsEditableUnderNormalRules()
    {
        var result = Evaluate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                Completed(StageCodes.IPA, Jan(11), Jan(20))
            },
            Change(StageCodes.IPA, completedOn: Jan(22)));

        Assert.True(result.IsValid);
        Assert.Equal(Jan(22), Assert.Single(result.Changes).Final.CompletedOn);
    }

    // SECTION: Mandatory completion backfill
    [Fact]
    public void Backfill_SuppliesTheMissingCompletion_AndClearsTheFlag()
    {
        var result = EvaluateBackfill(
            new[] { CompletedAwaitingBackfill(StageCodes.FS, start: Jan(1)) },
            Change(StageCodes.FS, completedOn: Jan(10)));

        Assert.True(result.IsValid);
        var change = Assert.Single(result.Changes);
        Assert.True(change.IsBackfill);
        Assert.Equal(Jan(10), change.Final.CompletedOn);
        Assert.False(change.Final.RequiresBackfill);
        Assert.Equal(StageStatus.Completed, change.Final.Status);
    }

    [Fact]
    public void Backfill_OfAStageThatNoLongerNeedsIt_IsReportedAsNotAwaitingBackfill()
    {
        var error = Assert.Single(EvaluateBackfill(
            new[] { Completed(StageCodes.FS, Jan(1), Jan(10)) },
            Change(StageCodes.FS, completedOn: Jan(11))).Errors);

        Assert.Equal(ActualValidationRule.NotAwaitingBackfill, error.Rule);
    }

    [Fact]
    public void Backfill_RequiresACompletionDate()
    {
        var error = Assert.Single(EvaluateBackfill(
            new[] { CompletedAwaitingBackfill(StageCodes.FS) },
            Change(StageCodes.FS, actualStart: Jan(1))).Errors);

        Assert.Equal(ActualValidationRule.CompletionRequired, error.Rule);
    }

    [Fact]
    public void Backfill_OfPublishedIpa_IsRejected()
    {
        var error = Assert.Single(ActualTimelineValidationService.Evaluate(
            V1(), Authority(Jan(25)), Today,
            Candidate(
                new[] { CompletedAwaitingBackfill(StageCodes.IPA) },
                ActualChangeEntryPoint.Backfill,
                Change(StageCodes.IPA, completedOn: Jan(20)))).Errors);

        Assert.Equal(ActualValidationRule.PublishedArppCompletion, error.Rule);
        Assert.Equal(ArppManagedIpaStageException.UserMessage, error.Message);
    }

    [Fact]
    public void Backfill_IsValidatedAgainstUntouchedSuccessors()
    {
        var error = Assert.Single(EvaluateBackfill(
            new[]
            {
                CompletedAwaitingBackfill(StageCodes.FS, start: Jan(1)),
                InProgress(StageCodes.IPA, Jan(11))
            },
            Change(StageCodes.FS, completedOn: Jan(20))).Errors);

        Assert.Equal(StageCodes.IPA, error.StageCode);
        Assert.Equal(StageCodes.FS, error.RelatedStageCode);
    }

    // SECTION: Branch-boundary resolver
    [Fact]
    public void ResolveBranchBoundaries_ReturnsEveryDatedBranchThroughSkippedAncestry()
    {
        var states = new Dictionary<string, StageChronologyState>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.TEC] = new(StageStatus.Skipped, null),
            [StageCodes.BID] = new(StageStatus.Completed, Feb(5)),
            [StageCodes.BM] = new(StageStatus.Completed, Feb(9))
        };

        var bounds = StageDateSuggestionResolver.ResolveBranchBoundaries(
            V1(), StageCodes.COB, code => states.TryGetValue(code, out var state) ? state : null);

        Assert.Equal(new[] { StageCodes.BM, StageCodes.BID }, bounds.Select(bound => bound.SourceStageCode));
        Assert.Equal(1, bounds.Single(bound => bound.SourceStageCode == StageCodes.BID).SkippedStageCount);
    }

    // SECTION: Service boundary (real workflow policy, IST date, ARPP lookup)
    [Theory]
    [InlineData(10, 0, 30, true)]
    [InlineData(9, 23, 59, false)]
    public async Task ValidateAsync_UsesTheIstOperationalDate(int day, int hour, int minute, bool accepted)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var service = new ActualTimelineValidationService(
            StageWorkflowTestFactory.CreatePolicy(db),
            new StubArppAuthority(null),
            FakeClock.ForIstDate(2026, 3, day, hour, minute));

        var result = await service.ValidateAsync(Candidate(
            new[] { Completed(StageCodes.FS, Mar(1), Mar(5)) },
            Change(StageCodes.FS, completedOn: Mar(10))));

        Assert.Equal(accepted, result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_UsesTheProjectWorkflowAndPublishedArppAuthority()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);
        var service = new ActualTimelineValidationService(
            StageWorkflowTestFactory.CreatePolicy(db),
            new StubArppAuthority(Authority(Jan(25))),
            FakeClock.ForIstDate(Today));

        var result = await service.ValidateAsync(Candidate(
            new[]
            {
                Completed(StageCodes.FS, Jan(1), Jan(10)),
                Completed(StageCodes.IPA, null, Jan(25)),
                InProgress(StageCodes.SOW, Jan(26))
            },
            Change(StageCodes.FS, completedOn: Jan(12)),
            Change(StageCodes.SOW, actualStart: Jan(24))));

        var error = Assert.Single(result.Errors);
        Assert.Equal(StageCodes.SOW, error.StageCode);
        Assert.Equal(StageCodes.IPA, error.RelatedStageCode);
    }

    // SECTION: Helpers
    private static ActualTimelineValidationResult Evaluate(
        IReadOnlyList<ActualStageState> official,
        params ActualStageChange[] changes) =>
        ActualTimelineValidationService.Evaluate(V1(), null, Today, Candidate(official, changes));

    private static ActualTimelineValidationResult EvaluateBackfill(
        IReadOnlyList<ActualStageState> official,
        params ActualStageChange[] changes) =>
        ActualTimelineValidationService.Evaluate(
            V1(), null, Today, Candidate(official, ActualChangeEntryPoint.Backfill, changes));

    private static ActualTimelineCandidate Candidate(
        IReadOnlyList<ActualStageState> official,
        params ActualStageChange[] changes) =>
        new(1, official, changes);

    private static ActualTimelineCandidate Candidate(
        IReadOnlyList<ActualStageState> official,
        ActualChangeEntryPoint entryPoint,
        params ActualStageChange[] changes) =>
        new(1, official, changes, entryPoint);

    private static ActualStageChange Change(
        string code,
        DateOnly? actualStart = null,
        DateOnly? completedOn = null,
        bool clearStart = false) =>
        new(code, actualStart, clearStart, completedOn);

    // The SDD-1.0 fallback topology used by ProjectStageWorkflowPolicy.
    private static ProjectStageWorkflowSnapshot V1(bool pncApplicable = true)
    {
        var dependencies = new (string Stage, string Predecessor)[]
        {
            (StageCodes.IPA, StageCodes.FS),
            (StageCodes.SOW, StageCodes.IPA),
            (StageCodes.AON, StageCodes.SOW),
            (StageCodes.BID, StageCodes.AON),
            (StageCodes.TEC, StageCodes.BID),
            (StageCodes.BM, StageCodes.BID),
            (StageCodes.COB, StageCodes.TEC),
            (StageCodes.COB, StageCodes.BM),
            (StageCodes.PNC, StageCodes.COB),
            (StageCodes.EAS, StageCodes.COB),
            (StageCodes.SO, StageCodes.EAS)
        };

        return new ProjectStageWorkflowSnapshot(
            ProcurementWorkflow.VersionV1,
            Metadata.GetStages(ProcurementWorkflow.VersionV1),
            dependencies
                .GroupBy(item => item.Stage, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.Select(item => item.Predecessor).ToArray(),
                    StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StageCodes.PNC, StageCodes.TOT },
            pncApplicable);
    }

    private static string Name(string code) => Metadata.GetDisplayName(ProcurementWorkflow.VersionV1, code);

    private static ArppIpaStageAuthority Authority(DateOnly issueDate) =>
        new(1, 2025, 7, ArppIssueKind.Original, 0, "ARPP 2025-26", issueDate, 70, "1", "PPP-1");

    private static ActualStageState Stage(string code, StageStatus status) => new(code, status, null, null, false);

    private static ActualStageState InProgress(string code, DateOnly start) =>
        new(code, StageStatus.InProgress, start, null, false);

    private static ActualStageState Completed(string code, DateOnly? start, DateOnly completedOn) =>
        new(code, StageStatus.Completed, start, completedOn, false);

    private static ActualStageState CompletedAwaitingBackfill(string code, DateOnly? start = null) =>
        new(code, StageStatus.Completed, start, null, true);

    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static DateOnly Feb(int day) => new(2026, 2, day);

    private static DateOnly Mar(int day) => new(2026, 3, day);

    private static async Task<ApplicationDbContext> CreateDbAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Actual validation project",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class StubArppAuthority : IArppIpaStageAuthorityService
    {
        private readonly ArppIpaStageAuthority? _authority;

        public StubArppAuthority(ArppIpaStageAuthority? authority) => _authority = authority;

        public Task<ArppIpaStageAuthority?> ResolveAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_authority);

        public Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ArppIpaStageAuthority>>(
                _authority is null
                    ? new Dictionary<int, ArppIpaStageAuthority>()
                    : new Dictionary<int, ArppIpaStageAuthority> { [_authority.ProjectId] = _authority });

        public Task<bool> IsManagedAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_authority is not null);

        public Task EnsureManualLifecycleMutationAllowedAsync(
            int projectId,
            string stageCode,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
