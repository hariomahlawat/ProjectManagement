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

        Assert.Contains("at-manage-task-panel", html, StringComparison.Ordinal);
        Assert.Contains("Manage Task", html, StringComparison.Ordinal);
        Assert.Contains("Planning", html, StringComparison.Ordinal);
        Assert.Contains("Danger Zone", html, StringComparison.Ordinal);
        Assert.DoesNotContain("at-action-more-menu", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<summary class=\"btn btn-outline-secondary btn-sm\">More</summary>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteBoardCss_UsesFixedOverlayDrawerWithoutThirdGridColumn()
    {
        // SECTION: Assert Sprint Board Execute uses the shared fixed overlay drawer instead of reserving a third grid column.
        var actionTrackerCss = ReadRepoFile("wwwroot", "css", "action-tracker.css");
        var detailsCss = ReadRepoFile("wwwroot", "css", "action-task-details-redesign.css");
        var css = actionTrackerCss + Environment.NewLine + detailsCss;

        Assert.Contains("--at-task-drawer-width: min(32rem, calc(100vw - 2rem));", css, StringComparison.Ordinal);
        Assert.Contains(".at-shell.has-selection,", css, StringComparison.Ordinal);
        Assert.Contains(".at-shell.is-planning-board.has-selection", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 88px minmax(0, 1fr);", css, StringComparison.Ordinal);
        Assert.Contains("width: var(--at-task-drawer-width);", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--at-planning-drawer-width", css, StringComparison.Ordinal);
        Assert.DoesNotContain("minmax(320px, 30vw)", css, StringComparison.Ordinal);
    }


    [Fact]
    public void TaskDetailsPartial_PreservesCurrentRouteContextForDrawerActions()
    {
        // SECTION: Assert drawer action forms preserve current workspace context instead of forcing users back to Planning Execute.
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains("name=\"ViewMode\" value=\"@Model.ResolvedViewMode\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"PlanningTab\" value=\"@Model.ResolvedPlanningTab\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"PlanningView\" value=\"@Model.ResolvedPlanningView\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"ViewMode\" value=\"Planning\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"PlanningTab\" value=\"Execute\"", html, StringComparison.Ordinal);
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
