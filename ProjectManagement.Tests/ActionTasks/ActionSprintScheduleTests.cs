using System;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using Xunit;

namespace ProjectManagement.Tests.ActionTasks;

public sealed class ActionSprintScheduleTests
{
    private static readonly DateTime IstToday = new(2026, 10, 23);

    [Theory]
    [InlineData("2026-10-13", "2026-10-26", "13–26 Oct 2026")]
    [InlineData("2026-11-28", "2026-12-12", "28 Nov – 12 Dec 2026")]
    [InlineData("2026-12-22", "2027-01-04", "22 Dec 2026 – 04 Jan 2027")]
    public void DateRange_NamesTheMonthAndYearOnlyWhereTheyChange(string start, string end, string expected)
    {
        var sprint = new ActionSprint { StartDate = DateTime.Parse(start), EndDate = DateTime.Parse(end) };

        Assert.Equal(expected, ActionSprintSchedule.DateRange(sprint));
    }

    [Theory]
    [InlineData(ActionSprintStatus.Active, "2026-10-26", "3 days left")]
    [InlineData(ActionSprintStatus.Active, "2026-10-24", "1 day left")]
    [InlineData(ActionSprintStatus.Active, "2026-10-23", "Ends today")]
    [InlineData(ActionSprintStatus.Active, "2026-10-22", "1 day past its end")]
    [InlineData(ActionSprintStatus.Active, "2026-10-20", "3 days past its end")]
    public void Timing_ForAnActiveSprint_CountsToItsEnd(ActionSprintStatus status, string end, string expected)
    {
        var sprint = new ActionSprint { Status = status, StartDate = new DateTime(2026, 10, 13), EndDate = DateTime.Parse(end) };

        Assert.Equal(expected, ActionSprintSchedule.Timing(sprint, IstToday));
    }

    [Theory]
    [InlineData("2026-10-27", "Starts in 4 days")]
    [InlineData("2026-10-24", "Starts tomorrow")]
    [InlineData("2026-10-23", "Starts today")]
    [InlineData("2026-10-20", "Start date has passed")]
    public void Timing_ForAPlannedSprint_CountsToItsStart(string start, string expected)
    {
        var sprint = new ActionSprint { Status = ActionSprintStatus.Planned, StartDate = DateTime.Parse(start), EndDate = new DateTime(2026, 11, 10) };

        Assert.Equal(expected, ActionSprintSchedule.Timing(sprint, IstToday));
    }

    [Fact]
    public void Timing_IsNullForAClosedSprint()
    {
        Assert.Null(ActionSprintSchedule.Timing(new ActionSprint { Status = ActionSprintStatus.Closed, EndDate = IstToday }, IstToday));
    }

    [Fact]
    public void Headers_NameTheSprint_AndTheRegisterBecomesCardsOnPhones()
    {
        var overview = ReadRepoFile("Pages", "ActionTasks", "_TaskDashboard.cshtml");
        var board = ReadRepoFile("Pages", "ActionTasks", "_PlanningCommandStrip.cshtml");
        var register = ReadRepoFile("Pages", "ActionTasks", "_TaskRegister.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "action-tracker.css").Replace("\r\n", "\n");

        Assert.Contains("<h2>@sprint.Name</h2>", overview, StringComparison.Ordinal);
        Assert.Contains("<h2>@Model.SelectedSprint.Name</h2>", board, StringComparison.Ordinal);
        foreach (var header in new[] { overview, board })
        {
            Assert.Contains("Model.SprintDateRange(", header, StringComparison.Ordinal);
            Assert.Contains("Model.SprintTiming(", header, StringComparison.Ordinal);
            Assert.Contains("<partial name=\"_SprintProgress\"", header, StringComparison.Ordinal);
        }

        foreach (var cell in new[] { "at-register-id-cell", "at-register-status-cell", "at-register-priority-cell" })
        {
            Assert.Contains($"<td class=\"{cell}\">", register, StringComparison.Ordinal);
        }

        Assert.Contains("@media (max-width: 640px) {\n    .at-register-table-scroll { overflow-x: visible; border: 0; }", css, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return System.IO.File.ReadAllText(System.IO.Path.Combine(new[] { current.FullName }.Concat(segments).ToArray()));
            }

            current = current.Parent;
        }

        throw new System.IO.DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }

    [Theory]
    [InlineData(2, 11, 18)]
    [InlineData(0, 0, 0)]
    [InlineData(5, 5, 100)]
    [InlineData(7, 5, 100)]
    public void PercentClosed_IsBounded(int closed, int total, int expected)
    {
        Assert.Equal(expected, ActionSprintSchedule.PercentClosed(closed, total));
    }
}
