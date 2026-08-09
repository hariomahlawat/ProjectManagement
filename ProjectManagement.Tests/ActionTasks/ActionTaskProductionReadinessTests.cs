using ProjectManagement.Infrastructure;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskProductionReadinessTests
{
    [Fact]
    public void TimeFmtToIst_DisplaysUtcTimestampInIndianStandardTime()
    {
        var utc = new DateTime(2026, 4, 28, 2, 8, 0, DateTimeKind.Utc);

        var formatted = TimeFmt.ToIst(utc);

        Assert.Equal("28 Apr 2026, 07:38 IST", formatted);
    }

    [Fact]
    public void TaskPeek_IsCollaborationFirstAndDeliberatelyLightweight()
    {
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains("at-task-peek", html, StringComparison.Ordinal);
        Assert.Contains("data-at-v2-remark-composer", html, StringComparison.Ordinal);
        Assert.Contains("ActionTaskUpdateTypes.Comment", html, StringComparison.Ordinal);
        Assert.Contains("ActionTaskUpdateTypes.Conference", html, StringComparison.Ordinal);
        Assert.Contains("Take = 3", html, StringComparison.Ordinal);
        Assert.Contains("Open full task", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Change status", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Manage Task", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Activity history", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TaskPeek_KeepsWorkflowCommandsSeparateFromRemarkComposer()
    {
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains("asp-page-handler=\"AddUpdate\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Submit\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ReturnForAction\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Close\"", html, StringComparison.Ordinal);
        Assert.Contains("Start work", html, StringComparison.Ordinal);
        Assert.Contains("Resume", html, StringComparison.Ordinal);
        Assert.Contains("Submit for closure", html, StringComparison.Ordinal);
        Assert.Contains("Return for action", html, StringComparison.Ordinal);
        Assert.DoesNotContain("NewStatus", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskPeek_RendersAttachmentsInsideOriginatingUpdates()
    {
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");
        var timeline = ReadRepoFile("Pages", "ActionTasks", "_TaskUpdateTimeline.cshtml");

        Assert.Contains("_TaskUpdateTimeline", html, StringComparison.Ordinal);
        Assert.Contains("Attachments.TryGetValue(update.Id", timeline, StringComparison.Ordinal);
        Assert.Contains("at-v2-update-files", timeline, StringComparison.Ordinal);
        Assert.Contains("at-v2-update-file", timeline, StringComparison.Ordinal);
        Assert.DoesNotContain("<h3>Attachments</h3>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TaskPeek_PreservesCurrentCollectionContextForPostActions()
    {
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains("name=\"ViewMode\" value=\"@Model.ResolvedViewMode\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"PlanningTab\" value=\"@planningTabRouteValue\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"PlanningView\" value=\"@planningViewRouteValue\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"SelectedSprintId\" value=\"@Model.SelectedSprintId\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskPeekCss_UsesCompactOverlayWidth()
    {
        var css = ReadRepoFile("wwwroot", "css", "action-task-peek.css");

        Assert.Contains("width: min(32rem, calc(100vw - 2rem));", css, StringComparison.Ordinal);
        Assert.Contains(".at-task-peek-body", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", css, StringComparison.Ordinal);
    }

    [Fact]
    public void FullTaskWorkspace_ProvidesDeepWorkWithoutInflatingPeek()
    {
        var html = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");

        Assert.Contains("at-task-workspace-grid", html, StringComparison.Ordinal);
        Assert.Contains("Discussion &amp; progress", html, StringComparison.Ordinal);
        Assert.Contains("_TaskUpdateTimeline", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage task", html, StringComparison.Ordinal);
        Assert.Contains("Task controls", html, StringComparison.Ordinal);
        Assert.Contains("Activity history", html, StringComparison.Ordinal);
        Assert.Contains("<details", html, StringComparison.Ordinal);
        Assert.Contains("data-at-v2-inline-toggle=\"date\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ChangeDate\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"AssignBacklogToSprint\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"AssignOutsideToSprint\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"RemoveFromSprint\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"MoveToBacklog\"", html, StringComparison.Ordinal);
        Assert.Contains("data-at-v2-open=\"close\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FullTaskWorkspace_PreservesOnlyLocalCollectionReturnContext()
    {
        var page = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");
        var model = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml.cs");
        var peek = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains(@"href=""@Model.BackUrl""", page, StringComparison.Ordinal);
        Assert.Contains(@"asp-route-returnUrl=""@Model.ReturnUrl""", page, StringComparison.Ordinal);
        Assert.Contains("asp-route-returnUrl", peek, StringComparison.Ordinal);
        Assert.Contains("Url.IsLocalUrl(ReturnUrl)", model, StringComparison.Ordinal);
    }

    [Fact]
    public void FullTaskWorkspace_ResolvesAssignmentRoleServerSide()
    {
        var page = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");
        var model = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml.cs");

        Assert.DoesNotContain("responsibleRole", page, StringComparison.Ordinal);
        Assert.Contains("LoadAssignableUsersAsync(CurrentRole)", model, StringComparison.Ordinal);
        Assert.Contains("responsible.Role", model, StringComparison.Ordinal);
    }

    [Fact]
    public void MyWorkQuickLinks_OpenTheRequestedLowFrictionIntent()
    {
        var html = ReadRepoFile("Pages", "ActionTasks", "_TaskMyWorkQueueRows.cshtml");

        Assert.Contains("asp-route-taskIntent=\"remark\"", html, StringComparison.Ordinal);
        Assert.Contains("asp-route-taskIntent=\"submit\"", html, StringComparison.Ordinal);
        Assert.Contains("Add remark", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Add update<", html, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePathParts)
    {
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
