using ProjectManagement.Infrastructure;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskProductionReadinessTests
{
    [Fact]
    public void TimeFmtToIst_DisplaysUtcTimestampInIndianStandardTime()
    {
        // SECTION: Assert production timestamp rendering uses IST with a stable invariant format.
        var utc = new DateTime(2026, 4, 28, 2, 8, 0, DateTimeKind.Utc);

        var formatted = TimeFmt.ToIst(utc);

        Assert.Equal("28 Apr 2026, 07:38 IST", formatted);
    }

    [Fact]
    public void TaskDetailsPartial_UsesIstTimestampsButKeepsDueDateDateOnly()
    {
        // SECTION: Assert Action Tracker inspector does not render audit timestamps as raw UTC strings.
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains("TimeFmt.ToIst", html, StringComparison.Ordinal);
        Assert.Contains("SelectedTask.DueDate.ToString(\"dd MMM yyyy\")", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString(\"dd MMM yyyy, HH:mm\")", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskDetailsPartial_RendersInlineMoreActionsWithoutFloatingDropdownMarkup()
    {
        // SECTION: Assert secondary actions are a stable inline panel grouped by workflow, planning, and danger actions.
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains("at-more-actions-panel", html, StringComparison.Ordinal);
        Assert.Contains("Workflow Actions", html, StringComparison.Ordinal);
        Assert.Contains("Planning Actions", html, StringComparison.Ordinal);
        Assert.Contains("Danger Zone", html, StringComparison.Ordinal);
        Assert.DoesNotContain("at-action-more-menu", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<summary class=\"btn btn-outline-secondary btn-sm\">More</summary>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteBoardCss_UsesSharedDrawerWidthReservation()
    {
        // SECTION: Assert Sprint Board Execute containers reserve the fixed inspector width while selected.
        var css = ReadRepoFile("wwwroot", "css", "action-tracker.css");

        Assert.Contains("--at-planning-drawer-width: 34rem;", css, StringComparison.Ordinal);
        Assert.Contains(".at-shell.is-planning-board.has-selection .at-planning-execute-panel", css, StringComparison.Ordinal);
        Assert.Contains(".at-shell.is-planning-board.has-selection .at-execute-status-board", css, StringComparison.Ordinal);
        Assert.Contains(".at-shell.is-planning-board.has-selection .at-execute-status-grid", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskDetailsPartial_HidesMutationMoreActionsForClosedTasks()
    {
        // SECTION: Assert closed tasks remain view-only by gating date/status/planning mutation groups.
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains("var hasWorkflowActions = !selectedTaskIsClosed", html, StringComparison.Ordinal);
        Assert.Contains("var hasPlanningActions = !selectedTaskIsClosed", html, StringComparison.Ordinal);
        Assert.Contains("var hasDangerActions = !selectedTaskIsClosed", html, StringComparison.Ordinal);
        Assert.Contains("View only", html, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePathParts)
    {
        // SECTION: Source assertions locate files from either repository-root or test-output working directories.
        var relativePath = Path.Combine(relativePathParts);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file {relativePath}.", relativePath);
    }
}
