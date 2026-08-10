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
        var actions = ReadRepoFile("Pages", "ActionTasks", "_TaskActionBar.cshtml");
        var panels = ReadRepoFile("Pages", "ActionTasks", "_TaskActionPanels.cshtml");

        Assert.Contains("asp-page-handler=\"AddUpdate\"", html, StringComparison.Ordinal);
        Assert.Contains("_TaskActionPanels", html, StringComparison.Ordinal);
        Assert.Contains("Start work", actions, StringComparison.Ordinal);
        Assert.Contains("Resume", actions, StringComparison.Ordinal);
        Assert.Contains("Submit for closure", actions, StringComparison.Ordinal);
        Assert.Contains("Return for action", actions, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"submit\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"return\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"accept-close\"", panels, StringComparison.Ordinal);
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

        Assert.Contains("ViewMode = Model.ResolvedViewMode", html, StringComparison.Ordinal);
        Assert.Contains("PlanningTab = planningTabRouteValue", html, StringComparison.Ordinal);
        Assert.Contains("PlanningView = planningViewRouteValue", html, StringComparison.Ordinal);
        Assert.Contains("SelectedSprintId = Model.SelectedSprintId", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskPeekCss_UsesWiderOperationalWidth()
    {
        var css = ReadRepoFile("wwwroot", "css", "action-task-peek.css");

        Assert.Contains("width: min(40rem, calc(100vw - 2rem));", css, StringComparison.Ordinal);
        Assert.Contains(".at-task-peek-body", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", css, StringComparison.Ordinal);
    }

    [Fact]
    public void FullTaskWorkspace_ProvidesDeepWorkWithoutInflatingPeek()
    {
        var html = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");
        var panels = ReadRepoFile("Pages", "ActionTasks", "_TaskActionPanels.cshtml");

        Assert.Contains("at-task-workspace-grid", html, StringComparison.Ordinal);
        Assert.Contains(">Updates<", html, StringComparison.Ordinal);
        Assert.Contains("_TaskUpdateTimeline", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage task", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Task controls", html, StringComparison.Ordinal);
        Assert.Contains("Activity history", html, StringComparison.Ordinal);
        Assert.Contains("<details", html, StringComparison.Ordinal);
        Assert.Contains("_TaskActionBar", html, StringComparison.Ordinal);
        Assert.Contains("_TaskActionPanels", html, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"change-date\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"assign-sprint\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"add-sprint\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"remove-sprint\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"backlog\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"accept-close\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"close-direct\"", panels, StringComparison.Ordinal);
    }


    [Fact]
    public void TaskActionSurface_IsSharedAndDoesNotScrollThePageToRemoteForms()
    {
        var peek = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");
        var page = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");
        var bar = ReadRepoFile("Pages", "ActionTasks", "_TaskActionBar.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "action-tasks", "task-interaction.js");

        Assert.Contains("_TaskActionBar", peek, StringComparison.Ordinal);
        Assert.Contains("_TaskActionBar", page, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-open=\"change-date\"", bar, StringComparison.Ordinal);
        Assert.Contains("Return to backlog", bar, StringComparison.Ordinal);
        Assert.Contains("Close directly", bar, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-open", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("panel.scrollIntoView", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.scrollTo", script, StringComparison.Ordinal);
    }


    [Fact]
    public void TaskActionSurface_ExposesOperationalMetadataCommandsDirectly()
    {
        var bar = ReadRepoFile("Pages", "ActionTasks", "_TaskActionBar.cshtml");
        var peek = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");
        var page = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");
        var panels = ReadRepoFile("Pages", "ActionTasks", "_TaskActionPanels.cshtml");

        Assert.Contains("Edit task", bar, StringComparison.Ordinal);
        Assert.Contains("Reassign", bar, StringComparison.Ordinal);
        Assert.Contains("Change priority", bar, StringComparison.Ordinal);
        Assert.Contains("_TaskActionPanels", peek, StringComparison.Ordinal);
        Assert.Contains("_TaskActionPanels", page, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"edit-task\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"reassign\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-v22-panel=\"priority\"", panels, StringComparison.Ordinal);
        Assert.Contains("data-at-person-picker", panels, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskTimeline_KeepsHumanRemarkCorrectionVisibleAndProgressImmutable()
    {
        var timeline = ReadRepoFile("Pages", "ActionTasks", "_TaskUpdateTimeline.cshtml");
        var model = ReadRepoFile("Pages", "ActionTasks", "TaskUpdateTimelineViewModel.cs");

        Assert.Contains("data-at-update-edit-toggle", timeline, StringComparison.Ordinal);
        Assert.Contains("data-at-update-delete-form", timeline, StringComparison.Ordinal);
        Assert.Contains("!isProgress && Model.CanEditUpdate(update)", timeline, StringComparison.Ordinal);
        Assert.Contains("!isProgress && Model.CanDeleteUpdate(update)", timeline, StringComparison.Ordinal);
        Assert.Contains("CanEditUpdate", model, StringComparison.Ordinal);
        Assert.Contains("CanDeleteUpdate", model, StringComparison.Ordinal);
        Assert.Contains("EditedUpdateIds", model, StringComparison.Ordinal);
        Assert.Contains("at-update-edited", timeline, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_RecentActivity_CountsDistinctTasksRatherThanCallingThemUpdates()
    {
        var dashboard = ReadRepoFile("Pages", "ActionTasks", "_TaskDashboard.cshtml");

        Assert.Contains(".Distinct()", dashboard, StringComparison.Ordinal);
        Assert.Contains("recentCount == 1 ? \"task\" : \"tasks\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("@recentCount updates", dashboard, StringComparison.Ordinal);
    }


    [Fact]
    public void FullTaskWorkspace_PreservesOnlyLocalCollectionReturnContext()
    {
        var page = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");
        var model = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml.cs");
        var peek = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");

        Assert.Contains(@"href=""@Model.BackUrl""", page, StringComparison.Ordinal);
        Assert.Contains(@"asp-route-returnUrl=""@Model.ReturnUrl""", page, StringComparison.Ordinal);
        Assert.Contains("returnUrl = currentCollectionUrl", peek, StringComparison.Ordinal);
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


    [Fact]
    public void TaskV24_UsesSharedPanelsSearchableAssigneePickerAndSubmittedFreeze()
    {
        var peek = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");
        var page = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");
        var panels = ReadRepoFile("Pages", "ActionTasks", "_TaskActionPanels.cshtml");
        var policy = ReadRepoFile("Services", "ActionTasks", "ActionTaskWorkflowPolicy.cs");
        var script = ReadRepoFile("wwwroot", "js", "pages", "action-tasks", "task-interaction.js");

        Assert.Contains("_TaskActionPanels", peek, StringComparison.Ordinal);
        Assert.Contains("_TaskActionPanels", page, StringComparison.Ordinal);
        Assert.Contains("data-at-person-picker", panels, StringComparison.Ordinal);
        Assert.Contains("initPersonPickers", script, StringComparison.Ordinal);
        Assert.Contains("var metadataMutable = !isClosed && !isSubmitted", policy, StringComparison.Ordinal);
        Assert.Contains("CanAcceptAndClose: isSubmitted", policy, StringComparison.Ordinal);
        Assert.Contains("CanReturnForAction: isSubmitted", policy, StringComparison.Ordinal);
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
