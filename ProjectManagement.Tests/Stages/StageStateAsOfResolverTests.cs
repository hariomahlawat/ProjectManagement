using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>"Where was the project on 31 March": the evidence order (dates, predecessors, change log, the stages after) and Unknown when nothing says.</summary>
public sealed class StageStateAsOfResolverTests
{
    private static readonly DateOnly YearEnd = new(2026, 3, 31);

    // FS → SOW → IPA → AON → BID → TEC/BM (parallel) → COB → SO
    private static readonly string[] Order = { StageCodes.FS, StageCodes.SOW, StageCodes.IPA, StageCodes.AON, StageCodes.BID, StageCodes.TEC, StageCodes.BM, StageCodes.COB, StageCodes.SO };

    [Fact]
    public void ADatedCompletion_DecidesOnItsOwn()
    {
        var states = Resolve(
            Fact(StageCodes.FS, StageStatus.Completed, completedOn: new(2026, 1, 10)),
            Fact(StageCodes.SOW, StageStatus.Completed, start: new(2026, 1, 11), completedOn: new(2026, 4, 5)));

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.FS]);
        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.SOW]); // started before, finished after
    }

    [Fact]
    public void AnUndatedStage_IsFinishedWhenALaterStageHadBegun()
    {
        var states = Resolve(
            Fact(StageCodes.FS, StageStatus.Completed),
            Fact(StageCodes.SOW, StageStatus.Completed),
            Fact(StageCodes.IPA, StageStatus.Completed, completedOn: new(2025, 10, 14)));

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.FS]);
        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.SOW]);
    }

    [Fact]
    public void BackToBack_TheNextStagesStartEndsTheUndatedOne()
    {
        // IPA is undated; AoN (whose only predecessor is IPA) started on 14 May, so IPA ended on 13 May.
        var states = Resolve(
            Fact(StageCodes.FS, StageStatus.Completed, completedOn: new(2025, 9, 1)),
            Fact(StageCodes.SOW, StageStatus.Completed, completedOn: new(2025, 10, 1)),
            Fact(StageCodes.IPA, StageStatus.Completed),
            Fact(StageCodes.AON, StageStatus.InProgress, start: new(2026, 5, 14)));

        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.IPA]);
        Assert.Equal(StageStateAsOf.NotStarted, states[StageCodes.AON]);
    }

    [Fact]
    public void TheChangeLog_SettlesWhatTheDatesLeaveOpen()
    {
        var states = Resolve(
            new[]
            {
                Fact(StageCodes.FS, StageStatus.Completed, completedOn: new(2025, 9, 1)),
                Fact(StageCodes.SOW, StageStatus.Completed, completedOn: new(2025, 10, 1)),
                Fact(StageCodes.IPA, StageStatus.Completed, completedOn: new(2025, 11, 1)),
                Fact(StageCodes.AON, StageStatus.Completed, completedOn: new(2026, 2, 24)),
                Fact(StageCodes.BID, StageStatus.Completed),
                Fact(StageCodes.TEC, StageStatus.Completed),
                Fact(StageCodes.BM, StageStatus.Completed)
            },
            new[]
            {
                // Bid was recorded In progress on 24 Feb and completed (undated) only on 15 Apr.
                new StageLogFact(StageCodes.BID, "InProgress", new(2026, 2, 24)),
                new StageLogFact(StageCodes.BID, "Completed", new(2026, 4, 15)),
                // Bid was still recorded open after the date, so BM (which needs Bid) was not finished.
                new StageLogFact(StageCodes.BID, "InProgress", new(2026, 4, 2))
            });

        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.BID]);
        Assert.Equal(StageStateAsOf.NotStarted, states[StageCodes.BM]);
    }

    [Fact]
    public void ACompletedRecordedOnOrBeforeTheDate_IsFinished()
    {
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.Completed) },
            new[] { new StageLogFact(StageCodes.FS, "Completed", new(2026, 3, 31)) });

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.FS]);
    }

    [Fact]
    public void NothingRecorded_IsUnknown_AndThePositionIsARange()
    {
        var facts = new[]
        {
            Fact(StageCodes.FS, StageStatus.Completed, completedOn: new(2026, 3, 30)),
            Fact(StageCodes.SOW, StageStatus.Completed),
            Fact(StageCodes.IPA, StageStatus.NotStarted)
        };
        var states = Resolve(facts);
        Assert.Equal(StageStateAsOf.Unknown, states[StageCodes.SOW]);

        var position = StageStateAsOfResolver.ResolvePosition(Snapshots(facts), states);
        Assert.False(position.IsKnown);
        Assert.Equal(new[] { StageCodes.SOW, StageCodes.IPA }, position.Candidates.Select(stage => stage.Code).ToArray());
        Assert.Equal($"{StageCodes.DisplayNameOf(StageCodes.SOW)} or {StageCodes.DisplayNameOf(StageCodes.IPA)}", position.StageName);
    }

    [Fact]
    public void AStageCannotBeFinishedWhileOneItNeedsWasNot()
    {
        var states = Resolve(
            Fact(StageCodes.FS, StageStatus.Completed, completedOn: new(2026, 4, 10)),
            Fact(StageCodes.SOW, StageStatus.Completed));

        Assert.Equal(StageStateAsOf.NotStarted, states[StageCodes.SOW]);
    }

    [Fact]
    public void ParallelBranches_ABeganOneDoesNotFinishTheOther()
    {
        // BM had begun by the date, but BM does not follow TEC: TEC stays as its own record says.
        var states = Resolve(
            Fact(StageCodes.BID, StageStatus.Completed, completedOn: new(2026, 1, 10)),
            Fact(StageCodes.TEC, StageStatus.Completed),
            Fact(StageCodes.BM, StageStatus.InProgress, start: new(2026, 2, 1)));

        Assert.Equal(StageStateAsOf.Unknown, states[StageCodes.TEC]);
        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.BM]);
    }

    [Fact]
    public void AKnownPosition_IsTheFirstStageNotFinished()
    {
        var facts = new[]
        {
            Fact(StageCodes.FS, StageStatus.Completed, completedOn: new(2025, 9, 1)),
            Fact(StageCodes.SOW, StageStatus.Completed, completedOn: new(2025, 10, 1)),
            Fact(StageCodes.IPA, StageStatus.Completed, completedOn: new(2025, 10, 14)),
            Fact(StageCodes.AON, StageStatus.Completed, completedOn: new(2026, 6, 1))
        };

        var position = StageStateAsOfResolver.ResolvePosition(Snapshots(facts), Resolve(facts));

        Assert.True(position.IsKnown);
        Assert.Equal(StageCodes.AON, position.StageCode);
    }

    [Fact]
    public void AStageReopenedAfterTheDate_KeepsItsEarlierCompletion()
    {
        // Completed on 10 Feb, reopened on 15 May (the reopen clears the stage's own date).
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.InProgress, start: new(2026, 1, 5)) },
            new[]
            {
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 2, 10), FromStatus: "InProgress", ToCompletedOn: new(2026, 2, 10)),
                new StageLogFact(StageCodes.FS, "InProgress", new(2026, 5, 15), FromStatus: "Completed", FromCompletedOn: new(2026, 2, 10))
            });

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.FS]);
    }

    [Fact]
    public void AReopenRecordedOnlyAfterTheDate_StillShowsTheCompletionItCleared()
    {
        // No change recorded before the date; the reopen afterwards says it had been completed on 10 Feb.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.NotStarted) },
            new[] { new StageLogFact(StageCodes.FS, "NotStarted", new(2026, 5, 15), FromStatus: "Completed", FromCompletedOn: new(2026, 2, 10)) });

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.FS]);
    }

    [Fact]
    public void ACorrectedStartDate_ReplacesTheFirstOne()
    {
        // Started on 3 Feb, the start corrected to 4 May after the date: not started on 31 March.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.InProgress, start: new(2026, 5, 4)) },
            new[] { new StageLogFact(StageCodes.FS, "InProgress", new(2026, 6, 1), FromStatus: "InProgress", Sequence: 1, FromActualStart: new(2026, 2, 3), ToActualStart: new(2026, 5, 4)) });

        Assert.Equal(StageStateAsOf.NotStarted, states[StageCodes.FS]);
    }

    [Fact]
    public void AResetBeforeTheDate_EndsTheEarlierStart()
    {
        // Started in January, reset to Not started on 20 Feb: not started on 31 March.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.NotStarted) },
            new[]
            {
                new StageLogFact(StageCodes.FS, "InProgress", new(2026, 1, 10), FromStatus: "NotStarted", Sequence: 1, ToActualStart: new(2026, 1, 10)),
                new StageLogFact(StageCodes.FS, "NotStarted", new(2026, 2, 20), FromStatus: "InProgress", Sequence: 2, FromActualStart: new(2026, 1, 10))
            });

        Assert.Equal(StageStateAsOf.NotStarted, states[StageCodes.FS]);
    }

    [Fact]
    public void ACorrectedCompletionDate_ReplacesTheFirstOne()
    {
        // Recorded completed on 10 Feb, the date later corrected to 20 May (no reopen): not finished on 31 March.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.Completed, start: new(2026, 1, 5), completedOn: new(2026, 5, 20)) },
            new[]
            {
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 2, 10), FromStatus: "InProgress", ToCompletedOn: new(2026, 2, 10), Sequence: 1),
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 6, 2), FromStatus: "Completed", FromCompletedOn: new(2026, 2, 10), ToCompletedOn: new(2026, 5, 20), Sequence: 2)
            });

        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.FS]);
    }

    [Fact]
    public void AStartDateClearedByAReset_StillCounts()
    {
        // Completed in April with a February start, then reset to NotStarted in May (start cleared): in progress on 31 March.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.NotStarted) },
            new[]
            {
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 4, 10), FromStatus: "NotStarted", ToCompletedOn: new(2026, 4, 9), Sequence: 1, ToActualStart: new(2026, 2, 3)),
                new StageLogFact(StageCodes.FS, "NotStarted", new(2026, 5, 2), FromStatus: "Completed", FromCompletedOn: new(2026, 4, 9), Sequence: 2, FromActualStart: new(2026, 2, 3))
            });

        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.FS]);
    }

    [Fact]
    public void ASecondCompletionAfterAReopen_KeepsTheFirstOne()
    {
        // Completed on 10 Feb, reopened on 15 Apr, completed again on 20 May (today's date): finished on 31 March.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.Completed, start: new(2026, 1, 5), completedOn: new(2026, 5, 20)) },
            new[]
            {
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 2, 10), FromStatus: "InProgress", ToCompletedOn: new(2026, 2, 10), Sequence: 1),
                new StageLogFact(StageCodes.FS, "InProgress", new(2026, 4, 15), FromStatus: "Completed", FromCompletedOn: new(2026, 2, 10), Sequence: 2),
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 5, 20), FromStatus: "InProgress", ToCompletedOn: new(2026, 5, 20), Sequence: 3)
            });

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.FS]);
    }

    [Fact]
    public void ACompletionAfterTheDate_WithNoEarlierOne_IsNotFinished()
    {
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.Completed, start: new(2026, 1, 5), completedOn: new(2026, 5, 20)) },
            new[] { new StageLogFact(StageCodes.FS, "Completed", new(2026, 5, 20), FromStatus: "InProgress", ToCompletedOn: new(2026, 5, 20)) });

        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.FS]);
    }

    [Fact]
    public void TheLastChangeRecordedBeforeTheDate_IsTheStatusOnIt()
    {
        // Undated today; recorded In progress on 1 Mar and completed (undated) on 20 Apr.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.Completed) },
            new[]
            {
                new StageLogFact(StageCodes.FS, "InProgress", new(2026, 3, 1)),
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 4, 20))
            });

        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.FS]);
    }

    [Fact]
    public void TwoChangesOnTheDate_TheLaterOneDecides()
    {
        // Completed and then reopened on 31 March, listed out of order: the reopen (later) decides.
        var states = Resolve(
            new[] { Fact(StageCodes.FS, StageStatus.InProgress, start: new(2026, 1, 5)) },
            new[]
            {
                new StageLogFact(StageCodes.FS, "InProgress", new(2026, 3, 31), FromStatus: "Completed", Sequence: 2),
                new StageLogFact(StageCodes.FS, "Completed", new(2026, 3, 31), FromStatus: "InProgress", Sequence: 1)
            });

        Assert.Equal(StageStateAsOf.InProgress, states[StageCodes.FS]);
    }

    [Fact]
    public void ADatedLaterStage_OutweighsAnOlderOpenLog()
    {
        // AoN was recorded In progress in January, then force-completed without a date when Bid's February
        // completion was entered in April: AoN was finished by 31 March.
        var states = Resolve(
            new[]
            {
                Fact(StageCodes.IPA, StageStatus.Completed, completedOn: new(2025, 12, 1)),
                Fact(StageCodes.AON, StageStatus.Completed),
                Fact(StageCodes.BID, StageStatus.Completed, completedOn: new(2026, 2, 20))
            },
            new[] { new StageLogFact(StageCodes.AON, "InProgress", new(2026, 1, 15)) });

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.AON]);
    }

    [Fact]
    public void BackToBack_LooksPastSkippedStages()
    {
        // COB undated; PNC and EAS skipped; SO started on 1 April: COB ended on 31 March.
        var states = StageStateAsOfResolver.Resolve(
            new[]
            {
                Fact(StageCodes.TEC, StageStatus.Completed, completedOn: new(2026, 1, 5)),
                Fact(StageCodes.BM, StageStatus.Completed, completedOn: new(2026, 1, 6)),
                Fact(StageCodes.COB, StageStatus.Completed),
                Fact(StageCodes.PNC, StageStatus.Skipped),
                Fact(StageCodes.EAS, StageStatus.Skipped),
                Fact(StageCodes.SO, StageStatus.InProgress, start: new(2026, 4, 1))
            },
            Array.Empty<StageLogFact>(),
            WorkflowWithPncAndEas(),
            YearEnd);

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.COB]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParallelFollowers_TheEarliestStartEndsTheStage(bool tecFirst)
    {
        // Bid is undated; TEC started on 10 April and BM on 1 April: Bid had ended by 31 March, whatever the row order.
        var tec = Fact(StageCodes.TEC, StageStatus.InProgress, start: new(2026, 4, 10));
        var bm = Fact(StageCodes.BM, StageStatus.InProgress, start: new(2026, 4, 1));
        var states = Resolve(
            Fact(StageCodes.AON, StageStatus.Completed, completedOn: new(2026, 1, 10)),
            Fact(StageCodes.BID, StageStatus.Completed),
            tecFirst ? tec : bm,
            tecFirst ? bm : tec);

        Assert.Equal(StageStateAsOf.Finished, states[StageCodes.BID]);
    }

    [Fact]
    public void ParallelStages_GiveTheRangeEitherWayRound()
    {
        // TEC not started on the date, BM completed undated: either could be the stage then.
        var facts = new[]
        {
            Fact(StageCodes.FS, StageStatus.Completed, completedOn: new(2025, 6, 1)),
            Fact(StageCodes.SOW, StageStatus.Completed, completedOn: new(2025, 7, 1)),
            Fact(StageCodes.IPA, StageStatus.Completed, completedOn: new(2025, 8, 1)),
            Fact(StageCodes.AON, StageStatus.Completed, completedOn: new(2025, 9, 1)),
            Fact(StageCodes.BID, StageStatus.Completed, completedOn: new(2025, 10, 1)),
            Fact(StageCodes.TEC, StageStatus.NotStarted),
            Fact(StageCodes.BM, StageStatus.Completed)
        };
        var states = Resolve(facts);
        Assert.Equal(StageStateAsOf.Unknown, states[StageCodes.BM]);

        var position = StageStateAsOfResolver.ResolvePosition(Snapshots(facts), states);

        Assert.False(position.IsKnown);
        Assert.Equal(new[] { StageCodes.TEC, StageCodes.BM }, position.Candidates.Select(stage => stage.Code).ToArray());
    }

    [Fact]
    public void AProjectReactivatedAfterTheDate_IsReadFromTheEventBeforeIt()
    {
        static ProjectPositionAsOfService.LifecycleEvent Event(long id, string action, string recordedUtc, DateOnly? date = null, int? year = null) =>
            new(id, action, DateTime.Parse(recordedUtc), 1, date, year, null);

        // Completed on 10 Feb, reactivated on 20 May: completed on 31 March.
        Assert.Equal(LifecycleAsOf.Completed, ProjectPositionAsOfService.LifecycleBeforeReactivation(new[]
        {
            Event(1, "Project.LifecycleCompletionEndorsed", "2026-02-10T06:00:00", date: new DateOnly(2026, 2, 10)),
            Event(2, "Project.LifecycleReactivated", "2026-05-20T06:00:00")
        }, YearEnd));
        // Cancelled on 5 Jan, reactivated in June: cancelled on 31 March.
        Assert.Equal(LifecycleAsOf.Cancelled, ProjectPositionAsOfService.LifecycleBeforeReactivation(new[]
        {
            Event(1, "Project.LifecycleCancelled", "2026-01-05T06:00:00", date: new DateOnly(2026, 1, 5)),
            Event(2, "Project.LifecycleReactivated", "2026-06-01T06:00:00")
        }, YearEnd));
        // Completed (year only, 2026) and reactivated later: not known.
        Assert.Equal(LifecycleAsOf.NotKnown, ProjectPositionAsOfService.LifecycleBeforeReactivation(new[]
        {
            Event(1, "Project.LifecycleCompleted", "2026-04-02T06:00:00", year: 2026),
            Event(2, "Project.LifecycleReactivated", "2026-06-01T06:00:00")
        }, YearEnd));
        // No event before the reactivation: not known.
        Assert.Equal(LifecycleAsOf.NotKnown, ProjectPositionAsOfService.LifecycleBeforeReactivation(new[]
        {
            Event(2, "Project.LifecycleReactivated", "2026-06-01T06:00:00")
        }, YearEnd));
        // Reactivated before the date: today's fields decide.
        Assert.Null(ProjectPositionAsOfService.LifecycleBeforeReactivation(new[]
        {
            Event(1, "Project.LifecycleCompleted", "2025-11-02T06:00:00", year: 2025),
            Event(2, "Project.LifecycleReactivated", "2026-01-10T06:00:00")
        }, YearEnd));
    }

    [Fact]
    public void ALifecycleEvent_ReadsItsProjectAndDates()
    {
        var parsed = ProjectPositionAsOfService.LifecycleEvent.Parse(7, "Project.LifecycleCancelled", new DateTime(2026, 5, 13), "{\"ProjectId\":\"182\",\"CancelledOn\":\"2026-05-13\",\"Reason\":\"x\"}");
        Assert.NotNull(parsed);
        Assert.Equal(182, parsed!.ProjectId);
        Assert.Equal(new DateOnly(2026, 5, 13), parsed.Date);
        Assert.Null(ProjectPositionAsOfService.LifecycleEvent.Parse(8, "Project.LifecycleCompleted", DateTime.UtcNow, "not json"));
    }

    [Theory]
    [InlineData(ProjectLifecycleStatus.Completed, "2026-02-10", null, null, null, LifecycleAsOf.Completed)]
    [InlineData(ProjectLifecycleStatus.Completed, "2026-04-10", null, null, null, LifecycleAsOf.Active)]
    [InlineData(ProjectLifecycleStatus.Completed, null, 2024, null, null, LifecycleAsOf.Completed)]   // the whole year lies before
    [InlineData(ProjectLifecycleStatus.Completed, null, 2026, null, null, LifecycleAsOf.NotKnown)]    // year only, straddles the date
    [InlineData(ProjectLifecycleStatus.Completed, null, 2026, (short)2, null, LifecycleAsOf.Completed)]
    [InlineData(ProjectLifecycleStatus.Completed, null, 2026, (short)5, null, LifecycleAsOf.Active)]
    [InlineData(ProjectLifecycleStatus.Completed, null, null, null, null, LifecycleAsOf.NotKnown)]
    [InlineData(ProjectLifecycleStatus.Cancelled, null, null, null, "2026-01-05", LifecycleAsOf.Cancelled)]
    [InlineData(ProjectLifecycleStatus.Cancelled, null, null, null, "2026-06-05", LifecycleAsOf.Active)]
    [InlineData(ProjectLifecycleStatus.Cancelled, null, null, null, null, LifecycleAsOf.NotKnown)]
    [InlineData(ProjectLifecycleStatus.Active, null, null, null, null, LifecycleAsOf.Active)]
    public void Lifecycle_OnTheDate(ProjectLifecycleStatus status, string? completedOn, int? year, short? month, string? cancelledOn, LifecycleAsOf expected)
    {
        Assert.Equal(expected, ProjectPositionAsOfService.LifecycleOn(
            status,
            completedOn is null ? null : DateOnly.Parse(completedOn),
            year,
            month,
            cancelledOn is null ? null : DateOnly.Parse(cancelledOn),
            YearEnd));
    }

    // SECTION: Fixtures
    private static IReadOnlyDictionary<string, StageStateAsOf> Resolve(params StageFactNow[] facts) =>
        Resolve(facts, Array.Empty<StageLogFact>());

    private static IReadOnlyDictionary<string, StageStateAsOf> Resolve(IReadOnlyList<StageFactNow> facts, IReadOnlyList<StageLogFact> logs) =>
        StageStateAsOfResolver.Resolve(facts, logs, Workflow(), YearEnd);

    private static StageFactNow Fact(string code, StageStatus status, DateOnly? start = null, DateOnly? completedOn = null) =>
        new(code, status, start, completedOn);

    private static ProjectStageWorkflowSnapshot Workflow()
    {
        var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.SOW] = new[] { StageCodes.FS },
            [StageCodes.IPA] = new[] { StageCodes.SOW },
            [StageCodes.AON] = new[] { StageCodes.IPA },
            [StageCodes.BID] = new[] { StageCodes.AON },
            [StageCodes.TEC] = new[] { StageCodes.BID },
            [StageCodes.BM] = new[] { StageCodes.BID },
            [StageCodes.COB] = new[] { StageCodes.TEC, StageCodes.BM },
            [StageCodes.SO] = new[] { StageCodes.COB }
        };
        return new ProjectStageWorkflowSnapshot(
            "test",
            Order.Select(code => new WorkflowStageDefinition(code, StageCodes.DisplayNameOf(code))).ToList(),
            dependencies,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            pncApplicable: false);
    }

    // As SDD-2.0: PNC and EAS both follow COB (EAS also needs PNC when it applies), then SO; a skipped
    // PNC and EAS sit between COB and SO.
    private static ProjectStageWorkflowSnapshot WorkflowWithPncAndEas()
    {
        string[] order = { StageCodes.TEC, StageCodes.BM, StageCodes.COB, StageCodes.PNC, StageCodes.EAS, StageCodes.SO };
        var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.COB] = new[] { StageCodes.TEC, StageCodes.BM },
            [StageCodes.PNC] = new[] { StageCodes.COB },
            [StageCodes.EAS] = new[] { StageCodes.COB },
            [StageCodes.SO] = new[] { StageCodes.EAS }
        };
        return new ProjectStageWorkflowSnapshot(
            "test-pnc",
            order.Select(code => new WorkflowStageDefinition(code, StageCodes.DisplayNameOf(code))).ToList(),
            dependencies,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            pncApplicable: true);
    }

    private static IReadOnlyList<ProjectLifecycleStageSnapshot> Snapshots(IEnumerable<StageFactNow> facts)
    {
        var byCode = facts.ToDictionary(fact => fact.Code, StringComparer.OrdinalIgnoreCase);
        return Order
            .Select((code, index) => new ProjectLifecycleStageSnapshot(
                code,
                StageCodes.DisplayNameOf(code),
                byCode.TryGetValue(code, out var fact) ? fact.Status : StageStatus.NotStarted,
                index,
                byCode.TryGetValue(code, out var dated) ? dated.CompletedOn : null,
                byCode.ContainsKey(code)))
            .ToList();
    }
    // END SECTION
}
