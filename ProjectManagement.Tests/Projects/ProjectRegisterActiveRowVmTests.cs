using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectRegisterActiveRowVmTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Fact]
    public void AttentionOrder_BlockedThenMostLateThenNearestPdcThenNoPdcThenFinished()
    {
        var finished = Facts(1, ProjectRegisterStageState.Finished);
        var noPdcLong = Facts(2, days: 90);
        var noPdcShort = Facts(3, days: 10);
        var onPlanSoon = Facts(4, pdc: Today.AddDays(3), days: 20);
        var onPlanLater = Facts(5, pdc: Today.AddDays(30), days: 20);
        var lateLittle = Facts(6, pdc: Today.AddDays(-5), days: 40);
        var lateMuch = Facts(7, pdc: Today.AddDays(-60), days: 90);
        var blocked = Facts(8, ProjectRegisterStageState.Blocked, days: 15);

        var ordered = new[] { finished, noPdcLong, noPdcShort, onPlanSoon, onPlanLater, lateLittle, lateMuch, blocked }
            .OrderBy(facts => facts, ProjectRegisterActiveFacts.AttentionOrder)
            .Select(facts => facts.ProjectId);

        Assert.Equal(new[] { 8, 7, 6, 4, 5, 2, 3, 1 }, ordered);
    }

    [Fact]
    public void AttentionOrder_FinishedSortsLastEvenWithAPastPdc()
    {
        var finishedPastPdc = Facts(1, ProjectRegisterStageState.Finished, pdc: Today.AddDays(-90), days: 100);
        var late = Facts(2, pdc: Today.AddDays(-5), days: 40);
        var noPdc = Facts(3, days: 10);

        var ordered = new[] { finishedPastPdc, late, noPdc }
            .OrderBy(facts => facts, ProjectRegisterActiveFacts.AttentionOrder)
            .Select(facts => facts.ProjectId);

        Assert.Equal(new[] { 2, 3, 1 }, ordered);

        var row = ProjectRegisterActiveRowVm.Create(finishedPastPdc);
        Assert.Equal("All stages complete", row.StateText);
        Assert.Equal("Complete", row.TimingText);
        Assert.Equal("ok", row.TimingTone);
        Assert.Null(row.BarFillPercent);
    }

    [Fact]
    public void AttentionOrder_ProjectsWithoutFactsGoLast()
    {
        var ordered = new ProjectRegisterActiveFacts?[] { null, Facts(1, days: 5) }
            .OrderBy(facts => facts, ProjectRegisterActiveFacts.AttentionOrder)
            .ToList();

        Assert.Equal(1, ordered[0]!.ProjectId);
        Assert.Null(ordered[1]);
    }

    [Fact]
    public void Row_LateInProgress()
    {
        var row = ProjectRegisterActiveRowVm.Create(Facts(1, pdc: Today.AddDays(-51), days: 82));

        Assert.Equal("Stage 2 of 4", row.TrackerLabel);
        Assert.Equal("In progress · since 1 Aug 2026", row.StateText);
        Assert.Equal("82 d", row.AgeText);
        Assert.Equal("51 d late", row.TimingText);
        Assert.Equal("late", row.TimingTone);
        Assert.Equal("1 Sep 2026", row.PdcText);
        Assert.Equal("Planned 31 d", row.PlannedText);
        Assert.Equal(100, row.BarFillPercent);
        Assert.Equal(38, row.BarMarkPercent);
        Assert.Equal(new[] { "done", "current", "upcoming", "upcoming" }, row.Marks.Select(mark => mark.Modifier));
    }

    [Fact]
    public void Row_WaitingToStart_NamesThePreviousStage()
    {
        var row = ProjectRegisterActiveRowVm.Create(Facts(1, ProjectRegisterStageState.NotStarted, days: 51));

        Assert.Equal("Not started · First done 1 Sep 2026", row.StateText);
        Assert.Equal("waiting", row.StateTone);
        Assert.Equal("51 d waiting", row.AgeText);
        Assert.Equal("No PDC", row.TimingText);
        Assert.Null(row.BarFillPercent);
        Assert.Equal("Not set", row.PdcText);
        Assert.Equal("waiting", row.Marks[1].Modifier);
    }

    [Fact]
    public void Row_BlockedUsesTheBlockedTone()
    {
        var row = ProjectRegisterActiveRowVm.Create(Facts(1, ProjectRegisterStageState.Blocked, pdc: Today.AddDays(4), days: 40));

        Assert.Equal("Blocked · since 12 Sep 2026", row.StateText);
        Assert.Equal("blocked", row.StateTone);
        Assert.Equal("4 d left", row.TimingText);
        Assert.Equal("blocked", row.TimingTone);
        Assert.Equal("blocked", row.Marks[1].Modifier);
    }

    [Theory]
    [InlineData(0, "Today", false)]
    [InlineData(1, "Yesterday", false)]
    [InlineData(30, "30 d ago", false)]
    [InlineData(31, "31 d ago", true)]
    public void Row_UpdatedText(int daysSinceUpdate, string expected, bool stale)
    {
        var row = ProjectRegisterActiveRowVm.Create(Facts(1, days: 5, daysSinceUpdate: daysSinceUpdate));

        Assert.Equal(expected, row.UpdatedText);
        Assert.Equal(stale, row.IsStale);
    }

    [Theory]
    [InlineData("K Srinath", "KS")]
    [InlineData("Hari Om Ahlawat", "HO")]
    [InlineData("  ", "?")]
    public void Initials(string name, string expected)
        => Assert.Equal(expected, ProjectRegisterActiveRowVm.Initials(name));

    // Four stages; the current one is the second, unless every stage is finished.
    private static ProjectRegisterActiveFacts Facts(
        int id,
        ProjectRegisterStageState state = ProjectRegisterStageState.InProgress,
        DateOnly? pdc = null,
        int? days = null,
        int daysSinceUpdate = 2)
    {
        var finished = state == ProjectRegisterStageState.Finished;
        var currentMark = state switch
        {
            ProjectRegisterStageState.Blocked => ProjectRegisterStageMarkState.Blocked,
            ProjectRegisterStageState.NotStarted => ProjectRegisterStageMarkState.Waiting,
            ProjectRegisterStageState.Finished => ProjectRegisterStageMarkState.Done,
            _ => ProjectRegisterStageMarkState.Current
        };
        var stages = new[]
        {
            new ProjectRegisterStageMark("S1", "First", ProjectRegisterStageMarkState.Done),
            new ProjectRegisterStageMark("S2", "Second", currentMark),
            new ProjectRegisterStageMark("S3", "Third", finished ? ProjectRegisterStageMarkState.Done : ProjectRegisterStageMarkState.Upcoming),
            new ProjectRegisterStageMark("S4", "Fourth", finished ? ProjectRegisterStageMarkState.Done : ProjectRegisterStageMarkState.Upcoming)
        };

        DateOnly? ageFrom = days is int d ? Today.AddDays(-d) : null;
        int? planned = pdc is { } due && ageFrom is { } from && due >= from ? due.DayNumber - from.DayNumber : null;
        int? late = pdc is { } p && p < Today ? Today.DayNumber - p.DayNumber : null;
        int? left = pdc is { } q && q >= Today ? q.DayNumber - Today.DayNumber : null;

        return new ProjectRegisterActiveFacts(
            id,
            stages,
            finished ? 3 : 1,
            finished ? "S4" : "S2",
            finished ? "Fourth" : "Second",
            state,
            ageFrom,
            days,
            pdc,
            planned,
            late,
            left,
            IsProlonged: false,
            IsApproachingProlonged: false,
            Today.AddDays(-daysSinceUpdate),
            daysSinceUpdate);
    }
}
