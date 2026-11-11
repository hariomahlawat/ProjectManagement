using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services.ItProjects;
using Xunit;

namespace ProjectManagement.Tests.ItProjects;

public class ItProjectStageRulesTests
{
    [Fact]
    public void Catalogue_HasNineStagesInOrderWithBenchmarkingAndPncOptional()
    {
        Assert.Equal(
            new[] { "PPP", "AON", "BID", "TEC", "COB", "BM", "PNC", "SO", "DLV" },
            ItProjectStages.Codes);
        Assert.Equal(Enumerable.Range(1, 9), ItProjectStages.All.Select(stage => stage.SortOrder));
        Assert.Equal(new[] { "BM", "PNC" }, ItProjectStages.All.Where(stage => stage.IsOptional).Select(stage => stage.Code));
        Assert.Equal(new[] { "PPP", "SO" }, ItProjectStages.All.Where(stage => stage.CarriesCost).Select(stage => stage.Code));
        Assert.Equal("Delivery and ATP", ItProjectStages.LabelOf("DLV"));
        Assert.Equal("IT ARPPP / IPA", ItProjectStages.LabelOf("PPP"));
    }

    [Fact]
    public void NewStageRows_StartOpenWithOptionalStagesSkipped()
    {
        var rows = ItProjectStages.NewStageRows();

        Assert.Equal(9, rows.Count);
        Assert.All(rows, row => Assert.False(row.IsCompleted));
        Assert.All(rows, row => Assert.Null(row.CompletedOn));
        Assert.Equal(new[] { "BM", "PNC" }, rows.Where(row => row.IsSkipped).Select(row => row.StageCode));
    }

    [Fact]
    public void CompletingALaterStage_CompletesEveryEarlierNonSkippedStage()
    {
        var result = ItProjectStageRules.Apply(Input(S("TEC", true)));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC" }, Completed(result));
        Assert.All(result.Stages.Where(stage => stage.StageCode != "TEC" && stage.IsCompleted), stage => Assert.Null(stage.CompletedOn));
        Assert.True(result.Stages.Single(stage => stage.StageCode == "BM").IsSkipped);
        Assert.False(result.Stages.Single(stage => stage.StageCode == "BM").IsCompleted);
    }

    [Fact]
    public void ReopeningAStage_ReopensEveryLaterStageAndClearsTheirDates()
    {
        // The form posts COB unticked while SO is still ticked and dated: the highest ticked stage
        // wins, so COB stays complete. Reopen means the client untickes the later ones too.
        var withLaterTicked = ItProjectStageRules.Apply(Input(S("COB"), S("SO", true, new DateOnly(2026, 3, 4))));
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC", "COB", "SO" }, Completed(withLaterTicked));

        var reopened = ItProjectStageRules.Apply(Input(S("BID", true, new DateOnly(2025, 9, 29)), S("COB"), S("SO")));
        Assert.Equal(new[] { "PPP", "AON", "BID" }, Completed(reopened));
        Assert.Null(reopened.Stages.Single(stage => stage.StageCode == "SO").CompletedOn);
    }

    [Fact]
    public void ADateImpliesCompletion()
    {
        var result = ItProjectStageRules.Apply(Input(S("AON", date: new DateOnly(2026, 6, 19))));

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "PPP", "AON" }, Completed(result));
        Assert.Equal(new DateOnly(2026, 6, 19), result.Stages.Single(stage => stage.StageCode == "AON").CompletedOn);
    }

    [Fact]
    public void DatesMustNotGoBackwards()
    {
        var result = ItProjectStageRules.Apply(Input(
            S("PPP", true, new DateOnly(2026, 6, 1)),
            S("BID", true, new DateOnly(2026, 5, 1))));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Bid cannot be completed before IT ARPPP / IPA", error);
        Assert.Contains("01 Jun 2026", error);
    }

    [Fact]
    public void DatesOnlyNeedToBeOrderedAmongDatedStages()
    {
        var result = ItProjectStageRules.Apply(Input(
            S("PPP", true, new DateOnly(2026, 1, 1)),
            S("AON", true),
            S("BID", true, new DateOnly(2026, 1, 1)),
            S("TEC", true, new DateOnly(2026, 2, 1))));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void OnlyBenchmarkingAndPncMayBeSkipped()
    {
        var result = ItProjectStageRules.Apply(Input(S("TEC", skipped: true)));

        Assert.False(result.IsValid);
        Assert.Contains("TEC cannot be skipped.", result.Errors);
    }

    [Fact]
    public void IncludingAnOptionalStageInsertsItIntoTheChain()
    {
        var notYetReached = ItProjectStageRules.Apply(Input(S("COB", true), S("BM", skipped: false)));
        Assert.True(notYetReached.IsValid);
        var bm = notYetReached.Stages.Single(stage => stage.StageCode == "BM");
        Assert.False(bm.IsSkipped);
        Assert.False(bm.IsCompleted);

        var alreadyPassed = ItProjectStageRules.Apply(Input(S("SO", true), S("PNC", skipped: false)));
        Assert.True(alreadyPassed.IsValid);
        var pnc = alreadyPassed.Stages.Single(stage => stage.StageCode == "PNC");
        Assert.False(pnc.IsSkipped);
        Assert.True(pnc.IsCompleted);
        Assert.Null(pnc.CompletedOn);
    }

    [Fact]
    public void SkippedStageIsNeitherCompleteNorDated()
    {
        var result = ItProjectStageRules.Apply(Input(S("PNC", true, new DateOnly(2026, 3, 4), skipped: true), S("SO", true)));

        Assert.True(result.IsValid);
        var pnc = result.Stages.Single(stage => stage.StageCode == "PNC");
        Assert.True(pnc.IsSkipped);
        Assert.False(pnc.IsCompleted);
        Assert.Null(pnc.CompletedOn);
    }

    [Fact]
    public void SoCost_IsOptionalRoundedAndKeptWhetherOrNotSoIsComplete()
    {
        var open = ItProjectStageRules.Apply(Input(S("SO", cost: 46.805m)));
        Assert.True(open.IsValid);
        var so = open.Stages.Single(stage => stage.StageCode == "SO");
        Assert.False(so.IsCompleted);
        Assert.Equal(46.81m, so.CostLakhs);

        // The IPA cost sits on the first stage, kept whether or not that stage is complete.
        var ipa = ItProjectStageRules.Apply(Input(S("PPP", cost: 92.555m)));
        Assert.True(ipa.IsValid);
        Assert.Equal(92.56m, ipa.Stages.Single(stage => stage.StageCode == "PPP").CostLakhs);
        Assert.Contains("The IT ARPPP / IPA cost cannot be negative.", ItProjectStageRules.Apply(Input(S("PPP", true, cost: -1m))).Errors);

        var negative = ItProjectStageRules.Apply(Input(S("SO", true, cost: -1m)));
        Assert.Contains("The SO cost cannot be negative.", negative.Errors);

        var elsewhere = ItProjectStageRules.Apply(Input(S("BID", true, cost: 5m)));
        Assert.Contains("Only the IT ARPPP / IPA and SO stages carry a cost (Bid).", elsewhere.Errors);
    }

    [Fact]
    public void MissingDuplicateOrUnknownStagesAreRejected()
    {
        var missing = ItProjectStageRules.Apply(Input().Where(stage => stage.StageCode != "COB").ToList());
        Assert.Contains("Stage COB is missing.", missing.Errors);

        var duplicate = ItProjectStageRules.Apply(Input().Append(new ItProjectStageInput("BID", false, null, false, null)).ToList());
        Assert.Contains("Stage Bid was posted twice.", duplicate.Errors);

        var unknown = ItProjectStageRules.Apply(Input().Append(new ItProjectStageInput("XYZ", false, null, false, null)).ToList());
        Assert.Contains("Unknown stage 'XYZ'.", unknown.Errors);
    }

    [Fact]
    public void Reopen_OpensTheStageAndEveryLaterOneSoApplyDoesNotReCompleteIt()
    {
        // What the editor posts after a preview normalised it: every stage up to SO ticked.
        var posted = Input(S("PPP", true), S("AON", true), S("BID", true, new DateOnly(2025, 9, 29)), S("TEC", true), S("COB", true), S("SO", true, new DateOnly(2026, 3, 4), cost: 46.8m));

        var reopened = ItProjectStageRules.Reopen(posted, "COB");
        var result = ItProjectStageRules.Apply(reopened);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "PPP", "AON", "BID", "TEC" }, Completed(result));
        Assert.Null(result.Stages.Single(stage => stage.StageCode == "SO").CompletedOn);
        Assert.Equal(46.8m, result.Stages.Single(stage => stage.StageCode == "SO").CostLakhs); // the cost is not progress
        Assert.Equal(new DateOnly(2025, 9, 29), result.Stages.Single(stage => stage.StageCode == "BID").CompletedOn);

        Assert.Equal(posted, ItProjectStageRules.Reopen(posted, "XYZ"));
    }

    [Fact]
    public void Diff_NamesTheStagesASaveWouldCompleteOrReopen()
    {
        var current = ItProjectStages.NewStageRows();
        current.Single(stage => stage.StageCode == "PPP").IsCompleted = true;
        current.Single(stage => stage.StageCode == "AON").IsCompleted = true;

        var next = ItProjectStageRules.Apply(Input(S("TEC", true))).Stages;
        var (completed, reopened) = ItProjectStageRules.Diff(current, next);
        Assert.Equal(new[] { "BID", "TEC" }, completed);
        Assert.Empty(reopened);

        var back = ItProjectStageRules.Apply(Input()).Stages;
        (completed, reopened) = ItProjectStageRules.Diff(current, back);
        Assert.Empty(completed);
        Assert.Equal(new[] { "PPP", "AON" }, reopened);
    }

    [Fact]
    public void Status_IsDerivedFromTheStageRowsAndTheDroppedMarker()
    {
        var rows = ItProjectStages.NewStageRows();
        Assert.Equal(ItProjectStatusKind.NotStarted, ItProjectStatus.Resolve(false, rows).Kind);
        Assert.Equal("PPP", ItProjectStatus.Resolve(false, rows).CurrentStageCode);

        foreach (var code in new[] { "PPP", "AON", "BID" })
        {
            rows.Single(stage => stage.StageCode == code).IsCompleted = true;
        }

        var inProgress = ItProjectStatus.Resolve(false, rows);
        Assert.Equal(ItProjectStatusKind.InProgress, inProgress.Kind);
        Assert.Equal("TEC", inProgress.CurrentStageCode);
        Assert.Equal("In progress · TEC", inProgress.Label);

        var dropped = ItProjectStatus.Resolve(true, rows);
        Assert.Equal(ItProjectStatusKind.Dropped, dropped.Kind);
        Assert.Equal("TEC", dropped.CurrentStageCode);

        foreach (var row in rows.Where(stage => !stage.IsSkipped))
        {
            row.IsCompleted = true;
        }

        var completed = ItProjectStatus.Resolve(false, rows);
        Assert.Equal(ItProjectStatusKind.Completed, completed.Kind);
        Assert.Null(completed.CurrentStageCode);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static IReadOnlyList<string> Completed(ItProjectStageRulesResult result) =>
        result.Stages.Where(stage => stage.IsCompleted).OrderBy(stage => stage.SortOrder).Select(stage => stage.StageCode).ToList();

    internal sealed record Override(string Code, bool Completed, DateOnly? Date, bool? Skipped, decimal? Cost);

    /// <summary>One posted stage. Skipped defaults to the catalogue default for that stage.</summary>
    internal static Override S(string code, bool completed = false, DateOnly? date = null, bool? skipped = null, decimal? cost = null) =>
        new(code, completed, date, skipped, cost);

    /// <summary>All nine stages at their defaults, with the named ones overridden.</summary>
    internal static List<ItProjectStageInput> Input(params Override[] overrides)
    {
        var map = ItProjectStages.All.ToDictionary(
            stage => stage.Code,
            stage => new ItProjectStageInput(stage.Code, false, null, stage.IsOptional, null),
            StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides)
        {
            map[o.Code] = new ItProjectStageInput(o.Code, o.Completed, o.Date, o.Skipped ?? ItProjectStages.IsOptional(o.Code), o.Cost);
        }

        return ItProjectStages.Codes.Select(code => map[code]).ToList();
    }
}
