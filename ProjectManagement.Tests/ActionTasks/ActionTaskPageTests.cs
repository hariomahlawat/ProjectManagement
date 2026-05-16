using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Pages.ActionTasks;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskPageTests
{
    [Fact]
    public async Task OnGet_InvalidTaskId_DoesNotThrowAndClearsSelection()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        page.TaskId = 9999;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Null(page.TaskId);
        Assert.Null(page.SelectedTask);
    }


    [Fact]
    public async Task DisplaySprintName_PreservesSprintTerminologyForGeneratedAndCustomNames()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var generated = new ActionSprint
        {
            Id = 7,
            Name = "Sprint 01 May - 15 May"
        };
        var custom = new ActionSprint
        {
            Id = 8,
            Name = "Command focus alpha"
        };

        // SECTION: Act
        var generatedName = setup.Page.DisplaySprintName(generated);
        var customName = setup.Page.DisplaySprintName(custom);

        // SECTION: Assert
        Assert.Equal("Sprint 01 May - 15 May", generatedName);
        Assert.Equal("Command focus alpha", customName);
    }

    [Fact]
    public async Task MyWorkView_StaysActiveWhenSortingRegister()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        page.SortBy = "title";
        page.SortDir = "desc";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.True(page.IsMyWorkView);
        Assert.Equal("MyWork", page.ResolvedViewMode);
    }

    [Fact]
    public async Task PlanningKanban_StatusNoOpRedirect_ResolvesToExecute()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        var task = await setup.Db.ActionTasks.SingleAsync();
        var sprint = AddSprint(setup.Db, "Kanban Sprint", ActionSprintStatus.Active);
        task.SprintId = sprint.Id;
        await setup.Db.SaveChangesAsync();
        page.ViewMode = "Planning";
        page.PlanningView = "Kanban";
        page.SelectedSprintId = sprint.Id;

        // SECTION: Act
        var result = await page.OnPostUpdateStatusAsync(task.Id, Convert.ToBase64String(task.RowVersion), task.Status, "No status change");

        // SECTION: Assert
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Planning", redirect.RouteValues![nameof(IndexModel.ViewMode)]);
        Assert.False(redirect.RouteValues.ContainsKey(nameof(IndexModel.PlanningView)));
        Assert.Equal(sprint.Id, redirect.RouteValues[nameof(IndexModel.SelectedSprintId)]);
        Assert.Equal(task.Id, redirect.RouteValues[nameof(IndexModel.TaskId)]);
    }

    [Fact]
    public async Task MyWorkPartial_RendersOpenPersonalQueueWithoutCommandNoise()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        setup.Db.ActionTasks.Add(NewTask("Closed personal task", ActionTaskStatuses.Closed));
        var otherUserTask = NewTask("Other user task", ActionTaskStatuses.Assigned);
        otherUserTask.AssignedToUserId = "user-2";
        setup.Db.ActionTasks.Add(otherUserTask);
        var backlogTask = NewTask("Backlog personal task", ActionTaskStatuses.Backlog);
        backlogTask.AssignedToUserId = string.Empty;
        backlogTask.AssignedToRole = string.Empty;
        setup.Db.ActionTasks.Add(backlogTask);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskMyWork.cshtml");

        // SECTION: Assert
        Assert.DoesNotContain("Assigned to me", html, StringComparison.Ordinal);
        Assert.Contains("Needs Action", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Tasks in Active Sprint", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Action Required", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Current Work", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Submitted / Awaiting Closure", html, StringComparison.Ordinal);
        Assert.Contains("Other Assigned Work", html, StringComparison.Ordinal);
        Assert.Contains("Open assigned tasks not already shown above.", html, StringComparison.Ordinal);
        Assert.Contains("Start Work", html, StringComparison.Ordinal);
        Assert.Contains("Details", html, StringComparison.Ordinal);
        Assert.Contains("Mine", html, StringComparison.Ordinal);
        Assert.DoesNotContain("All My Tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Closed personal task", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Backlog personal task", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Other user task", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove from Sprint", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Return to Backlog", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Reassign", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Close Task", html, StringComparison.Ordinal);
        Assert.Single(page.MyWorkAllMyTasksDisplays);
        Assert.DoesNotContain(page.MyWorkAllMyTasksDisplays, item => string.Equals(item.Task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MyWorkQueue_TaskAppearsOnlyInHighestPrioritySection()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var overlappingTask = await setup.Db.ActionTasks.SingleAsync();
        overlappingTask.Title = "Overdue in progress";
        overlappingTask.Status = ActionTaskStatuses.InProgress;
        overlappingTask.DueDate = DateTime.UtcNow.Date.AddDays(-1);
        setup.Db.ActionTasks.Add(NewTask("Later in progress", ActionTaskStatuses.InProgress));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskMyWork.cshtml");

        // SECTION: Assert
        Assert.Contains(page.MyWorkActionRequiredDisplays, item => item.Task.Title == "Overdue in progress");
        Assert.DoesNotContain(page.MyWorkCurrentWorkDisplays, item => item.Task.Title == "Overdue in progress");
        Assert.Contains(page.MyWorkCurrentWorkDisplays, item => item.Task.Title == "Later in progress");
        Assert.Equal(1, CountOccurrences(html, "Overdue in progress"));
    }

    [Fact]
    public async Task MyWorkPartial_ZeroOpenTasksRendersSingleEmptyStateOnly()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.Status = ActionTaskStatuses.Closed;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskMyWork.cshtml");

        // SECTION: Assert
        Assert.Contains("0 open assigned tasks", html, StringComparison.Ordinal);
        Assert.Contains("No open tasks assigned to you.", html, StringComparison.Ordinal);
        Assert.Contains("New assigned work will appear here.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("at-mywork-kpis", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Action Required", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Current Work", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Submitted / Awaiting Closure", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Assigned Work", html, StringComparison.Ordinal);
        Assert.DoesNotContain("at-mywork-section", html, StringComparison.Ordinal);
    }


    [Fact]
    public async Task MyWorkPartial_RendersStatusSpecificPrimaryActionsAndExclusiveSections()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var assigned = await setup.Db.ActionTasks.SingleAsync();
        assigned.Title = "Assigned future";
        assigned.Status = ActionTaskStatuses.Assigned;
        assigned.DueDate = DateTime.UtcNow.Date.AddDays(5);
        setup.Db.ActionTasks.Add(NewTask("Blocked task", ActionTaskStatuses.Blocked));
        var overdueTask = NewTask("Overdue task", ActionTaskStatuses.Assigned);
        overdueTask.DueDate = DateTime.UtcNow.Date.AddDays(-1);
        setup.Db.ActionTasks.Add(overdueTask);
        var dueTodayTask = NewTask("Due today task", ActionTaskStatuses.Assigned);
        dueTodayTask.DueDate = DateTime.UtcNow.Date;
        setup.Db.ActionTasks.Add(dueTodayTask);
        setup.Db.ActionTasks.Add(NewTask("In progress task", ActionTaskStatuses.InProgress));
        setup.Db.ActionTasks.Add(NewTask("Submitted task", ActionTaskStatuses.Submitted));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskMyWork.cshtml");

        // SECTION: Assert
        Assert.Contains(page.MyWorkActionRequiredDisplays, item => item.Task.Title == "Blocked task");
        Assert.Contains(page.MyWorkActionRequiredDisplays, item => item.Task.Title == "Overdue task");
        Assert.Contains(page.MyWorkActionRequiredDisplays, item => item.Task.Title == "Due today task");
        Assert.Contains(page.MyWorkCurrentWorkDisplays, item => item.Task.Title == "In progress task");
        Assert.Contains(page.MyWorkSubmittedAwaitingClosureDisplays, item => item.Task.Title == "Submitted task");
        Assert.Contains(page.MyWorkAllMyTasksDisplays, item => item.Task.Title == "Assigned future");
        Assert.Equal(6, page.MyWorkActionRequiredDisplays.Count + page.MyWorkCurrentWorkDisplays.Count + page.MyWorkSubmittedAwaitingClosureDisplays.Count + page.MyWorkAllMyTasksDisplays.Count);
        Assert.Contains("Start Work", html, StringComparison.Ordinal);
        Assert.Contains("Submit for Closure", html, StringComparison.Ordinal);
        Assert.Contains("Add Update", html, StringComparison.Ordinal);
        Assert.Contains("Mark In Progress", html, StringComparison.Ordinal);
        Assert.Contains("View Details", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MyWorkTaskLink_PreservesRouteStateWhenOpeningInspector()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var task = await setup.Db.ActionTasks.SingleAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskMyWork.cshtml");

        // SECTION: Assert
        Assert.Contains($"taskId={task.Id}", html, StringComparison.Ordinal);
        Assert.Contains("viewMode=MyWork", html, StringComparison.Ordinal);
        Assert.Contains($"Open task AT-{task.Id} details", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskDetailsPartial_LegacyKanbanInspectorFormsResolveToExecute()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        var task = await setup.Db.ActionTasks.SingleAsync();
        page.ViewMode = "Planning";
        page.PlanningView = "Kanban";
        page.TaskId = task.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDetails.cshtml");

        // SECTION: Assert
        Assert.Contains("name=\"PlanningTab\" value=\"Execute\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"PlanningView\" value=\"Default\"", html, StringComparison.Ordinal);
        Assert.Contains("PlanningTab=Execute", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanningView=Kanban", html, StringComparison.Ordinal);
    }


    [Fact]
    public async Task TaskDetailsPartial_BacklogInspector_UsesPlanningNoteLabels()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.Status = ActionTaskStatuses.Backlog;
        task.AssignedToUserId = string.Empty;
        task.AssignedToRole = string.Empty;
        task.SprintId = null;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.TaskId = task.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDetails.cshtml");

        // SECTION: Assert
        Assert.Contains("Add Planning Note", html, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Add planning note or clarification\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">+ Update</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"at-action-title\">Add Update</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningPlanTab_BacklogItemsHeader_UsesTargetLanguageAndDoesNotMentionOutsideSprint()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Plan";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_PlanningPlanTab.cshtml");
        var headerStart = html.IndexOf("<h2>Backlog Items</h2>", StringComparison.Ordinal);
        var headerEnd = html.IndexOf("<div class=\"at-planning-backlog-body\">", StringComparison.Ordinal);
        var backlogHeader = html[headerStart..headerEnd];

        // SECTION: Assert
        Assert.Contains("Unassigned future work awaiting sprint planning.", backlogHeader, StringComparison.Ordinal);
        Assert.Contains("past target", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<span>Overdue</span>", backlogHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("Outside Sprint", backlogHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("Assigned Tasks Outside Sprint", backlogHeader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningDueExceptionsTaskCards_PreserveExecuteRouteState()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Due Exceptions Sprint", ActionSprintStatus.Active);
        await setup.Db.SaveChangesAsync();
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.SprintId = sprint.Id;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Execute";
        page.PlanningView = "DueExceptions";
        page.SelectedSprintId = sprint.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_PlanningDueExceptionsView.cshtml");

        // SECTION: Assert
        Assert.Contains("PlanningTab=Execute", html, StringComparison.Ordinal);
        Assert.Contains("PlanningView=DueExceptions", html, StringComparison.Ordinal);
        Assert.Contains($"SelectedSprintId={sprint.Id}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_SelectedTaskLoadsWhenFilteredOutOfRegister()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        var selectedTaskId = await setup.Db.ActionTasks.Select(t => t.Id).SingleAsync();
        page.ViewMode = "Register";
        page.FilterStatus = ActionTaskStatuses.Closed;
        page.TaskId = selectedTaskId;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Empty(page.Tasks);
        Assert.NotNull(page.SelectedTask);
        Assert.Equal(selectedTaskId, page.SelectedTask!.Id);
    }

    [Fact]
    public async Task RegisterPartial_RendersDedicatedScopeColumn()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Enterprise Sprint", ActionSprintStatus.Active);
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.SprintId = sprint.Id;
        task.DueDate = new DateTime(2026, 5, 21);
        var backlogTask = NewTask("Register backlog target", ActionTaskStatuses.Backlog);
        backlogTask.AssignedToUserId = string.Empty;
        backlogTask.AssignedToRole = string.Empty;
        backlogTask.DueDate = new DateTime(2026, 5, 20);
        setup.Db.ActionTasks.Add(backlogTask);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Register";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskRegister.cshtml");

        // SECTION: Assert
        Assert.Contains("<th scope=\"col\" class=\"at-register-sprint-heading\">Scope</th>", html, StringComparison.Ordinal);
        Assert.Contains("Due / Target Date", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th scope=\"col\" class=\"at-register-sprint-heading\">Sprint</th>", html, StringComparison.Ordinal);
        Assert.Contains("at-register-sprint-cell", html, StringComparison.Ordinal);
        Assert.Contains("Enterprise Sprint", html, StringComparison.Ordinal);
        Assert.Contains("Target 20 May 2026", html, StringComparison.Ordinal);
        Assert.Contains("Due 21 May 2026", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterPartial_ActiveFiltersRemainVisibleAndSortLinksPreserveFilters()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var taskDueDate = (await setup.Db.ActionTasks.SingleAsync()).DueDate.Date;
        var page = setup.Page;
        page.ViewMode = "Register";
        page.FilterStatus = ActionTaskStatuses.Assigned;
        page.FilterPriority = "Normal";
        page.FilterAssigneeUserId = "user-1";
        page.FilterDueDate = taskDueDate;
        page.FilterSearch = "Mine";
        page.SortBy = "due";
        page.SortDir = "desc";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskRegister.cshtml");

        // SECTION: Assert
        Assert.Contains("at-register-active-filters", html, StringComparison.Ordinal);
        Assert.Contains("Status: Assigned", html, StringComparison.Ordinal);
        Assert.Contains("Priority: Normal", html, StringComparison.Ordinal);
        Assert.Contains("Responsible Person: User One", html, StringComparison.Ordinal);
        Assert.Contains($"Due: {taskDueDate:dd MMM yyyy}", html, StringComparison.Ordinal);
        Assert.Contains("Search: Mine", html, StringComparison.Ordinal);
        Assert.Contains("FilterStatus=Assigned", html, StringComparison.Ordinal);
        Assert.Contains("FilterPriority=Normal", html, StringComparison.Ordinal);
        Assert.Contains("FilterAssigneeUserId=user-1", html, StringComparison.Ordinal);
        Assert.Contains($"FilterDueDate={taskDueDate:yyyy-MM-dd}", html, StringComparison.Ordinal);
        Assert.Contains("FilterSearch=Mine", html, StringComparison.Ordinal);
        Assert.Contains("SortBy=id", html, StringComparison.Ordinal);
        Assert.Contains("class=\"at-sort-link is-active\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-sort=\"descending\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateValidationFailure_ReopensPanel()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.DirectTaskInput = new IndexModel.CreateDirectTaskInput();
        page.ModelState.AddModelError("DirectTaskInput.Title", "required");

        // SECTION: Act
        var result = await page.OnPostCreateAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateModal);
    }

    [Fact]
    public async Task CreatePastDueDate_ReopensPanelAndRejectsTask()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        var existingTaskCount = await setup.Db.ActionTasks.CountAsync();
        page.DirectTaskInput = new IndexModel.CreateDirectTaskInput
        {
            Title = "Late task",
            Description = "Should be rejected",
            AssignedToUserId = "user-1",
            DueDate = DateTime.UtcNow.Date.AddDays(-1),
            Priority = "Normal"
        };

        // SECTION: Act
        var result = await page.OnPostCreateAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateModal);
        Assert.False(page.ModelState.IsValid);
        Assert.Equal(existingTaskCount, await setup.Db.ActionTasks.CountAsync());
        Assert.Contains(page.ModelState[nameof(IndexModel.CreateDirectTaskInput.DueDate)]!.Errors, e => e.ErrorMessage == "Due date cannot be in the past.");
    }


    [Fact]
    public async Task PlanningBoard_ShowsBacklogTasksWithoutSprintAssignment()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Active Sprint", ActionSprintStatus.Active);
        await setup.Db.SaveChangesAsync();
        setup.Db.ActionTasks.Add(NewTask("Sprint task", ActionTaskStatuses.Assigned, sprint.Id));
        var trueBacklog = NewTask("Backlog item", ActionTaskStatuses.Backlog);
        trueBacklog.AssignedToUserId = string.Empty;
        trueBacklog.AssignedToRole = string.Empty;
        setup.Db.ActionTasks.Add(trueBacklog);
        setup.Db.ActionTasks.Add(NewTask("Closed backlog", ActionTaskStatuses.Closed));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.NotEmpty(page.BacklogTasks);
        Assert.All(page.BacklogTasks, task => Assert.Null(task.SprintId));
        Assert.All(page.BacklogTasks, task => Assert.True(string.IsNullOrWhiteSpace(task.AssignedToUserId)));
        Assert.Contains(page.BacklogTasks, task => task.Title == "Backlog item");
        Assert.DoesNotContain(page.BacklogTasks, task => task.Title == "Mine");
        Assert.DoesNotContain(page.BacklogTasks, task => task.Title == "Closed backlog");
        Assert.DoesNotContain(page.BacklogTasks, task => task.Title == "Sprint task");
    }

    [Fact]
    public async Task TaskScopeBadges_UseSprintBacklogOutsideSprintAndClosedLabels()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Badge Sprint", ActionSprintStatus.Active);
        var sprintTask = NewTask("Sprint scoped", ActionTaskStatuses.Assigned, sprint.Id);
        var backlogTask = NewTask("Backlog scoped", ActionTaskStatuses.Backlog);
        backlogTask.AssignedToUserId = string.Empty;
        backlogTask.AssignedToRole = string.Empty;
        var nonSprintTask = NewTask("Non sprint scoped", ActionTaskStatuses.Assigned);
        var closedTask = NewTask("Closed scoped", ActionTaskStatuses.Closed);
        setup.Db.ActionTasks.AddRange(sprintTask, backlogTask, nonSprintTask, closedTask);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Equal("Badge Sprint", page.GetSprintBadgeText(sprintTask));
        Assert.Equal("Backlog", page.GetSprintBadgeText(backlogTask));
        Assert.Equal("Outside Sprint", page.GetSprintBadgeText(nonSprintTask));
        Assert.Equal("Closed", page.GetSprintBadgeText(closedTask));
        Assert.Contains("at-scope-badge-sprint", page.GetSprintBadgeClass(sprintTask), StringComparison.Ordinal);
        Assert.Contains("at-scope-badge-backlog", page.GetSprintBadgeClass(backlogTask), StringComparison.Ordinal);
        Assert.Contains("at-scope-badge-outside-sprint", page.GetSprintBadgeClass(nonSprintTask), StringComparison.Ordinal);
        Assert.Contains("at-scope-badge-closed", page.GetSprintBadgeClass(closedTask), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningBoard_SelectsActiveSprintByDefault()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        AddSprint(setup.Db, "Older planned sprint", ActionSprintStatus.Planned, startOffsetDays: -14);
        var activeSprint = AddSprint(setup.Db, "Current active sprint", ActionSprintStatus.Active, startOffsetDays: -1);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.NotNull(page.SelectedSprint);
        Assert.Equal(activeSprint.Id, page.SelectedSprint!.Id);
        Assert.Equal(activeSprint.Id, page.SelectedSprintId);
        Assert.True(page.CanModifySelectedSprint);
    }

    [Fact]
    public async Task PlanningBoard_DoesNotOfferClosedRegisterTasksForAssignment()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        AddSprint(setup.Db, "Active Sprint", ActionSprintStatus.Active);
        setup.Db.ActionTasks.Add(NewTask("Closed backlog", ActionTaskStatuses.Closed));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.DoesNotContain(page.SprintBacklogTasks, task => task.Title == "Closed backlog");
        Assert.DoesNotContain(page.AssignableBacklogTaskDisplays, item => item.Task.Title == "Closed backlog");
        Assert.All(page.AssignableBacklogTaskDisplays, item => Assert.NotEqual(ActionTaskStatuses.Closed, item.Task.Status));
        Assert.Contains(page.Tasks, task => task.Title == "Closed backlog");
        Assert.False(page.CanAssignTaskToSprint(page.Tasks.Single(task => task.Title == "Closed backlog")));
    }

    [Fact]
    public async Task SelectedClosedSprint_IsReadOnlyInUiHelperLogic()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var closedSprint = AddSprint(setup.Db, "Closed Sprint", ActionSprintStatus.Closed, startOffsetDays: -7);
        await setup.Db.SaveChangesAsync();
        setup.Db.ActionTasks.Add(NewTask("Closed sprint task", ActionTaskStatuses.Assigned, closedSprint.Id));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.SelectedSprintId = closedSprint.Id;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.NotNull(page.SelectedSprint);
        Assert.Equal(ActionSprintStatus.Closed, page.SelectedSprint!.Status);
        Assert.False(page.CanModifySelectedSprint);
        var sprintTask = Assert.Single(page.SelectedSprintTasks);
        Assert.False(page.CanMoveTaskToBacklog(sprintTask));
    }



    [Fact]
    public async Task CreateSprintPageHandler_WithPlanningAuthority_CreatesSprint()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Command Sprint",
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(14),
            Goal = "Command focus"
        };

        // SECTION: Act
        var result = await page.OnPostCreateSprintAsync();

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        var sprint = await setup.Db.ActionSprints.SingleAsync(s => s.Name == "Command Sprint");
        Assert.Equal(ActionSprintStatus.Planned, sprint.Status);
    }

    [Fact]
    public async Task CreateSprintPageHandler_WithNonAuthority_ReturnsPageAndDoesNotCreateSprint()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.ProjectOfficer);
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Unauthorized Sprint",
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(14)
        };

        // SECTION: Act
        var result = await page.OnPostCreateSprintAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateSprintPanel);
        Assert.Empty(await setup.Db.ActionSprints.Where(s => s.Name == "Unauthorized Sprint").ToListAsync());
        Assert.False(page.ModelState.IsValid);
    }

    [Fact]
    public async Task CreateSprintPageHandler_WithInvalidDateRange_ReturnsPageError()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Invalid Sprint",
            StartDate = DateTime.UtcNow.Date.AddDays(5),
            EndDate = DateTime.UtcNow.Date
        };

        // SECTION: Act
        var result = await page.OnPostCreateSprintAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateSprintPanel);
        Assert.False(page.ModelState.IsValid);
        Assert.Empty(await setup.Db.ActionSprints.Where(s => s.Name == "Invalid Sprint").ToListAsync());
    }

    [Fact]
    public async Task UpdateSprintPageHandler_WithPlannedSprint_UpdatesDetails()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var sprint = AddSprint(setup.Db, "Planned Sprint", ActionSprintStatus.Planned);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.SprintEditInput = new IndexModel.UpdateSprintInput
        {
            SprintId = sprint.Id,
            RowVersion = Convert.ToBase64String(sprint.RowVersion),
            Name = "Updated Sprint",
            Goal = "Updated focus",
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate.AddDays(1)
        };

        // SECTION: Act
        var result = await page.OnPostUpdateSprintAsync();

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        var updated = await setup.Db.ActionSprints.SingleAsync(s => s.Id == sprint.Id);
        Assert.Equal("Updated Sprint", updated.Name);
        Assert.Equal("Updated focus", updated.Goal);
    }

    [Fact]
    public async Task UpdateSprintPageHandler_WithClosedSprint_ReturnsPageError()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var sprint = AddSprint(setup.Db, "Closed Sprint", ActionSprintStatus.Closed);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.SprintEditInput = new IndexModel.UpdateSprintInput
        {
            SprintId = sprint.Id,
            RowVersion = Convert.ToBase64String(sprint.RowVersion),
            Name = "Should Not Update",
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate
        };

        // SECTION: Act
        var result = await page.OnPostUpdateSprintAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowEditSprintPanel);
        var unchanged = await setup.Db.ActionSprints.SingleAsync(s => s.Id == sprint.Id);
        Assert.Equal("Closed Sprint", unchanged.Name);
    }

    [Fact]
    public async Task ActivateSprintPageHandler_WithPlannedSprint_ActivatesSprint()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var sprint = AddSprint(setup.Db, "Planned Sprint", ActionSprintStatus.Planned);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;

        // SECTION: Act
        var result = await page.OnPostActivateSprintAsync(sprint.Id, Convert.ToBase64String(sprint.RowVersion));

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ActionSprintStatus.Active, (await setup.Db.ActionSprints.SingleAsync(s => s.Id == sprint.Id)).Status);
    }

    [Fact]
    public async Task ActivateSprintPageHandler_WhenAnotherSprintActive_ShowsError()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        AddSprint(setup.Db, "Active Sprint", ActionSprintStatus.Active);
        var planned = AddSprint(setup.Db, "Planned Sprint", ActionSprintStatus.Planned, startOffsetDays: 20);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;

        // SECTION: Act
        var result = await page.OnPostActivateSprintAsync(planned.Id, Convert.ToBase64String(planned.RowVersion));

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ActionSprintStatus.Planned, (await setup.Db.ActionSprints.SingleAsync(s => s.Id == planned.Id)).Status);
        Assert.Contains("Only one active sprint", page.TempData["ToastError"]?.ToString());
    }

    [Fact]
    public async Task CreateNextSprintPageHandler_UsesDayAfterSourceEndDateAndBecomesCarryTarget()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var source = AddSprint(setup.Db, "Active Source", ActionSprintStatus.Active, startOffsetDays: 0);
        setup.Db.ActionTasks.Add(NewTask("Carry me", ActionTaskStatuses.Assigned, source.Id));
        await setup.Db.SaveChangesAsync();
        var expectedStart = source.EndDate.Date.AddDays(1);
        var page = setup.Page;
        page.NextSprintInput = IndexModel.CreateNextSprintInput.FromSourceSprint(source);

        // SECTION: Act
        var result = await page.OnPostCreateNextSprintAsync();

        // SECTION: Assert
        Assert.IsType<RedirectToPageResult>(result);
        var next = await setup.Db.ActionSprints.SingleAsync(s => s.Name == page.NextSprintInput.Name);
        Assert.Equal(expectedStart, next.StartDate.Date);
        page.SelectedSprintId = source.Id;
        page.ViewMode = "Planning";
        await page.OnGetAsync();
        Assert.Contains(page.ClosureTargetSprints, s => s.Id == next.Id);
    }

    [Fact]
    public async Task CreateNextSprintPageHandler_WhenStartDateOnSourceEndDate_ReopensPanelAndShowsModelError()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var source = AddSprint(setup.Db, "Active Source", ActionSprintStatus.Active, startOffsetDays: 0);
        setup.Db.ActionTasks.Add(NewTask("Carry me", ActionTaskStatuses.Assigned, source.Id));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.NextSprintInput = IndexModel.CreateNextSprintInput.FromSourceSprint(source);
        page.NextSprintInput.StartDate = source.EndDate.Date;
        page.NextSprintInput.EndDate = source.EndDate.Date.AddDays(14);

        // SECTION: Act
        var result = await page.OnPostCreateNextSprintAsync();

        // SECTION: Assert
        Assert.IsType<PageResult>(result);
        Assert.True(page.ShowCreateNextSprintPanel);
        Assert.False(page.ModelState.IsValid);
        Assert.Contains(page.ModelState[$"{nameof(page.NextSprintInput)}.{nameof(IndexModel.CreateNextSprintInput.StartDate)}"]!.Errors, e => e.ErrorMessage.Contains("later than the source sprint end date", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(await setup.Db.ActionSprints.ToListAsync(), s => s.Name == page.NextSprintInput.Name && s.Id != source.Id);
    }

    [Fact]
    public async Task SprintClosureReviewPartial_RendersCreateNextSprintFormOutsideClosureDispositionForm()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var source = AddSprint(setup.Db, "Active Source", ActionSprintStatus.Active, startOffsetDays: 0);
        setup.Db.ActionTasks.Add(NewTask("Carry me", ActionTaskStatuses.Assigned, source.Id));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.SelectedSprintId = source.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_SprintClosureReview.cshtml");

        // SECTION: Assert
        var createFormStart = html.IndexOf("at-sprint-command-form", StringComparison.Ordinal);
        var closureFormStart = html.IndexOf("at-closure-form", StringComparison.Ordinal);
        var closureFormEnd = html.IndexOf("</form>", closureFormStart, StringComparison.Ordinal);
        Assert.Contains("handler=CreateNextSprint", html, StringComparison.Ordinal);
        Assert.True(createFormStart >= 0, "Create next sprint form should render.");
        Assert.True(closureFormStart >= 0, "Closure disposition form should render.");
        Assert.True(createFormStart < closureFormStart || createFormStart > closureFormEnd, "Create next sprint form must not be nested inside the closure disposition form.");
    }


    [Fact]
    public async Task PlanningBoardPartial_NoPlanningWindows_ExposesCreateSprintForAuthorisedUsers()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var page = setup.Page;
        page.ViewMode = "Planning";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskSprints.cshtml");

        // SECTION: Assert
        Assert.Null(page.SelectedSprint);
        Assert.Contains("No sprint has been created yet", html, StringComparison.Ordinal);
        Assert.Contains("Create Sprint", html, StringComparison.Ordinal);
        Assert.Contains("Assigned Tasks Outside Sprint", html, StringComparison.Ordinal);
        Assert.Contains("Assigned work not included in any sprint.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Create " + "Planning " + "Window", html, StringComparison.Ordinal);
        Assert.Contains("handler=CreateSprint", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Select a sprint to view sprint command details.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningBoardPartial_SelectedSprint_UsesExplicitSprintActionLabels()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var sprint = AddSprint(setup.Db, "Command Sprint", ActionSprintStatus.Planned);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.SelectedSprintId = sprint.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskSprints.cshtml");

        // SECTION: Assert
        Assert.Contains("Create Sprint", html, StringComparison.Ordinal);
        Assert.Contains("Edit Sprint", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Create</summary>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</summary>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningBoardPartial_SelectedSprint_RendersSprintAndTaskBoard()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var sprint = AddSprint(setup.Db, "Execution Sprint", ActionSprintStatus.Active);
        setup.Db.ActionTasks.Add(NewTask("Sprint board task", ActionTaskStatuses.InProgress, sprint.Id));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.SelectedSprintId = sprint.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskSprints.cshtml");

        // SECTION: Assert
        Assert.NotNull(page.SelectedSprint);
        Assert.Contains("Execution Sprint", html, StringComparison.Ordinal);
        Assert.Contains("Selected Sprint Board task board", html, StringComparison.Ordinal);
        Assert.Contains("Sprint Board", html, StringComparison.Ordinal);
        Assert.Contains("Sprint board task", html, StringComparison.Ordinal);
        Assert.Contains("at-execute-status-grid", html, StringComparison.Ordinal);
        Assert.Contains("at-execute-status-section", html, StringComparison.Ordinal);
        Assert.Contains($"<h2>{ActionTaskStatuses.Assigned}</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<h2>{ActionTaskStatuses.InProgress}</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<h2>{ActionTaskStatuses.Blocked}</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<h2>{ActionTaskStatuses.Submitted}</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"<h2>{ActionTaskStatuses.Closed}</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Execution Attention", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Due / Exceptions and Kanban", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Add from Backlog", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Add Backlog Task", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Backlog Queue", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Due / Exceptions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove from Sprint", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Return to Backlog", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SprintBoardTabs_RenderOnlyPlanExecuteClose()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var page = setup.Page;
        page.ViewMode = "Planning";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_PlanningTabs.cshtml");

        // SECTION: Assert
        Assert.Contains(">Plan</span>", html, StringComparison.Ordinal);
        Assert.Contains(">Execute</span>", html, StringComparison.Ordinal);
        Assert.Contains(">Close</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Views</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Kanban", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningPlanTab_RemovesQuickOpenAndKeepsCollapsedPlanningControls()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        AddSprint(setup.Db, "Active Sprint", ActionSprintStatus.Active);
        AddSprint(setup.Db, "Planned Sprint", ActionSprintStatus.Planned, 10);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Plan";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_PlanningPlanTab.cshtml");

        // SECTION: Assert
        Assert.Contains("<h2>Sprints</h2>", html, StringComparison.Ordinal);
        Assert.Contains("at-sprint-list-heading\">Active", html, StringComparison.Ordinal);
        Assert.Contains("at-sprint-list-heading\">Planned", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Backlog Items</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Quick open", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Open Sprint", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"at-planning-backlog-filter-shell\">", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"at-planning-outside-sprint at-planning-plan-card\">", html, StringComparison.Ordinal);
        Assert.Contains("assigned tasks outside sprint", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningPlanTab_BacklogSummaryCardsRenderOnlyWhenUseful()
    {
        // SECTION: Arrange compact backlog
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var compactPage = setup.Page;
        compactPage.ViewMode = "Planning";
        compactPage.PlanningTab = "Plan";
        await compactPage.OnGetAsync();

        // SECTION: Act compact backlog
        var compactHtml = await RenderPartialAsync(compactPage, "/Pages/ActionTasks/_PlanningPlanTab.cshtml");

        // SECTION: Assert compact backlog
        Assert.Contains("backlog items · 0 high priority · 0 past target", compactHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("at-backlog-summary-grid", compactHtml, StringComparison.Ordinal);

        // SECTION: Arrange urgent backlog
        var urgent = NewTask("Urgent backlog", ActionTaskStatuses.Backlog);
        urgent.AssignedToUserId = string.Empty;
        urgent.AssignedToRole = string.Empty;
        urgent.Priority = "High";
        urgent.DueDate = DateTime.UtcNow.Date.AddDays(3);
        setup.Db.ActionTasks.Add(urgent);
        await setup.Db.SaveChangesAsync();
        await compactPage.OnGetAsync();

        // SECTION: Act urgent backlog
        var urgentHtml = await RenderPartialAsync(compactPage, "/Pages/ActionTasks/_PlanningPlanTab.cshtml");

        // SECTION: Assert urgent backlog
        Assert.Contains("at-backlog-summary-grid", urgentHtml, StringComparison.Ordinal);
        Assert.Contains("<span>High priority</span>", urgentHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanningPlanTab_BacklogAddToSprintFormIsHiddenDisclosure()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        AddSprint(setup.Db, "Planning Sprint", ActionSprintStatus.Planned);
        var backlogTask = NewTask("Plan me", ActionTaskStatuses.Backlog);
        backlogTask.AssignedToUserId = string.Empty;
        backlogTask.AssignedToRole = string.Empty;
        backlogTask.SprintId = null;
        setup.Db.ActionTasks.Add(backlogTask);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Plan";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_PlanningPlanTab.cshtml");

        // SECTION: Assert
        Assert.Contains("<details class=\"at-backlog-add-disclosure at-inline-action\">", html, StringComparison.Ordinal);
        Assert.Contains(">Add to Sprint</summary>", html, StringComparison.Ordinal);
        Assert.Contains("Responsible person", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Pull from Backlog", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SprintBoardCss_UsesResponsiveExecuteGridWithoutHorizontalScrollRequirement()
    {
        // SECTION: Arrange
        var css = File.ReadAllText(Path.Combine(GetContentRoot(), "wwwroot", "css", "action-tracker.css"));
        var cleanupStart = css.IndexOf("/* SECTION: Sprint Board cleanup", StringComparison.Ordinal);
        var cleanupCss = css[cleanupStart..];

        // SECTION: Assert
        Assert.Contains("grid-template-columns: repeat(4, minmax(0, 1fr));", cleanupCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", cleanupCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", cleanupCss, StringComparison.Ordinal);
        Assert.Contains("overflow-x: visible;", cleanupCss, StringComparison.Ordinal);
        Assert.DoesNotContain("min-width: max-content", cleanupCss, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SprintClosureReview_UsesConciseDispositionTextWithoutDuplicateHelper()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var sprint = AddSprint(setup.Db, "Close Sprint", ActionSprintStatus.Active);
        setup.Db.ActionTasks.Add(NewTask("Unfinished task", ActionTaskStatuses.Assigned, sprint.Id));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Close";
        page.SelectedSprintId = sprint.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_SprintClosureReview.cshtml");

        // SECTION: Assert
        Assert.Contains("Choose one disposition for each unfinished task. Return to Backlog removes the responsible person.", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, "Return to Backlog removes the responsible person"));
        Assert.Contains("<span>Carry Forward</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span>Keep Outside Sprint</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span>Return to Backlog</span>", html, StringComparison.Ordinal);
        Assert.Contains("Closure remarks", html, StringComparison.Ordinal);
        Assert.Contains("Summarise command decision and disposition rationale.", html, StringComparison.Ordinal);
        Assert.Contains("Close Sprint with Review", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Move to Backlog, Remove Assignee", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Keep Assigned Outside Sprint", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskDetails_KeepsMovementActionsWithAppStyledConfirmationMetadataInInspector()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var sprint = AddSprint(setup.Db, "Inspector Sprint", ActionSprintStatus.Active);
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.SprintId = sprint.Id;
        task.Status = ActionTaskStatuses.Assigned;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Execute";
        page.SelectedSprintId = sprint.Id;
        page.TaskId = task.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDetails.cshtml");

        // SECTION: Assert
        Assert.Contains("Remove from Sprint, Keep Assigned", html, StringComparison.Ordinal);
        Assert.Contains("Move to Backlog, Remove Assignee", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-confirm=""true""", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, @"data-at-confirm-cancel-label=""Cancel"""));
        Assert.Contains(@"data-at-confirm-title=""Remove task from Sprint?""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-confirm-body=""This will keep the assignee but remove the task from the selected sprint.""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-confirm-accept-label=""Remove from Sprint""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-confirm-title=""Return task to Backlog?""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-confirm-body=""This will remove the assignee and return the task to Backlog.""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-confirm-accept-label=""Return to Backlog""", html, StringComparison.Ordinal);
        Assert.DoesNotContain("This will remove the responsible person and return the work to Backlog. Continue?", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoleNames.Comdt)]
    [InlineData(RoleNames.HoD)]
    public async Task TaskDetails_MoreMenu_PlanningAuthoritiesRenderChangeDueDate(string role)
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(role);
        var sprint = AddSprint(setup.Db, "Date Sprint", ActionSprintStatus.Active);
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.SprintId = sprint.Id;
        task.Status = ActionTaskStatuses.Assigned;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Execute";
        page.SelectedSprintId = sprint.Id;
        page.TaskId = task.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDetails.cshtml");

        // SECTION: Assert
        Assert.Contains("Workflow Actions", html, StringComparison.Ordinal);
        Assert.Contains("Planning Actions", html, StringComparison.Ordinal);
        Assert.Contains("Change Due Date", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-action-panel=""change-date""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-open-action=""change-date""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskDetails_MoreMenu_NormalAssigneeHidesChangeDueDate()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Ta);
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.Status = ActionTaskStatuses.Assigned;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "MyWork";
        page.TaskId = task.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDetails.cshtml");

        // SECTION: Assert
        Assert.Contains("Workflow Actions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Planning Actions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Change Due Date", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"data-at-action-panel=""change-date""", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"data-at-open-action=""change-date""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskDetails_MoreMenu_ClosedTaskHidesChangeDateWorkflowAndPlanningActions()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.HoD);
        var sprint = AddSprint(setup.Db, "Closed Sprint", ActionSprintStatus.Active);
        var task = await setup.Db.ActionTasks.SingleAsync();
        task.SprintId = sprint.Id;
        task.Status = ActionTaskStatuses.Closed;
        task.ClosedOn = DateTime.UtcNow;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Planning";
        page.PlanningTab = "Execute";
        page.SelectedSprintId = sprint.Id;
        page.TaskId = task.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDetails.cshtml");

        // SECTION: Assert
        Assert.Contains("View only", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Workflow Actions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Planning Actions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Change Due Date", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Change Target Date", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Change Status", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove from Sprint, Keep Assigned", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Move to Backlog, Remove Assignee", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"data-at-action-panel=""change-date""", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionTasksScript_UsesAppStyledConfirmationModalWithoutBrowserConfirm()
    {
        // SECTION: Arrange
        var script = File.ReadAllText(Path.Combine(GetContentRoot(), "wwwroot", "js", "pages", "action-tasks", "index.js"));

        // SECTION: Assert
        Assert.Contains("ensureActionConfirmationModal", script, StringComparison.Ordinal);
        Assert.Contains("actionTaskConfirmModal", script, StringComparison.Ordinal);
        Assert.Contains("requestSubmit", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.confirm", script, StringComparison.Ordinal);
    }


    [Fact]
    public void TaskKanbanPartial_IsRemovedFromActionTracker()
    {
        // SECTION: Assert legacy Kanban partial is no longer a user-facing execution surface.
        var path = Path.Combine(GetContentRoot(), "Pages", "ActionTasks", "_TaskKanban.cshtml");

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DashboardPartial_TopKpis_ShowOnlyCommandExceptionCards()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = "CommandCentre";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDashboard.cshtml");
        var kpiStart = html.IndexOf("at-command-kpis", StringComparison.Ordinal);
        var kpiEnd = html.IndexOf("at-active-sprint-snapshot", StringComparison.Ordinal);
        var kpis = html[kpiStart..kpiEnd];

        // SECTION: Assert
        Assert.Contains("Overdue", kpis, StringComparison.Ordinal);
        Assert.Contains("Blocked", kpis, StringComparison.Ordinal);
        Assert.Contains("Pending Closure", kpis, StringComparison.Ordinal);
        Assert.Contains("Critical Open", kpis, StringComparison.Ordinal);
        Assert.Contains("Open Tasks", kpis, StringComparison.Ordinal);
        Assert.DoesNotContain("Carry Forward", kpis, StringComparison.Ordinal);
        Assert.DoesNotContain("Backlog", kpis, StringComparison.Ordinal);
        Assert.DoesNotContain("Sprint Tasks", kpis, StringComparison.Ordinal);
        Assert.DoesNotContain("Completed", kpis, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardPartial_NoActiveSprint_RendersCleanEmptyStateOnly()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = "CommandCentre";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDashboard.cshtml");
        var sprintStart = html.IndexOf("at-active-sprint-snapshot", StringComparison.Ordinal);
        var attentionStart = html.IndexOf("Attention Required", StringComparison.Ordinal);
        var activeSprint = html[sprintStart..attentionStart];

        // SECTION: Assert
        Assert.Contains("No active sprint is currently set.", activeSprint, StringComparison.Ordinal);
        Assert.Contains("Activate a planned sprint from Sprint Board", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("Total in active sprint", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("Completed", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("In progress", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("Overdue in sprint", activeSprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardPartial_ActiveSprintSnapshot_DoesNotShowBacklogOrEmptyExceptionPanels()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Health Sprint", ActionSprintStatus.Active);
        setup.Db.ActionTasks.Add(NewTask("Health sprint task", ActionTaskStatuses.Assigned, sprint.Id));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "CommandCentre";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDashboard.cshtml");
        var sprintStart = html.IndexOf("at-active-sprint-snapshot", StringComparison.Ordinal);
        var attentionStart = html.IndexOf("Attention Required", StringComparison.Ordinal);
        var activeSprint = html[sprintStart..attentionStart];

        // SECTION: Assert
        Assert.Contains("<h2>Active Sprint</h2>", activeSprint, StringComparison.Ordinal);
        Assert.Contains("Health Sprint", activeSprint, StringComparison.Ordinal);
        Assert.Contains("1 tasks", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("Active Sprint Exceptions", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("No active sprint exceptions.", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("Active Sprint Health", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("<h3>Backlog</h3>", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("No overdue tasks in the active sprint.", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("No blocked tasks in the active sprint.", activeSprint, StringComparison.Ordinal);
        Assert.DoesNotContain("No submitted tasks pending closure in the active sprint.", activeSprint, StringComparison.Ordinal);
    }


    [Fact]
    public async Task DashboardPartial_ActiveSprintExceptions_RenderActiveDetailsOutsideGlobalAttentionCap()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Exception Sprint", ActionSprintStatus.Active);
        for (var i = 0; i < 6; i++)
        {
            var globalOverdue = NewTask($"Global overdue {i + 1}", ActionTaskStatuses.Assigned);
            globalOverdue.DueDate = DateTime.UtcNow.Date.AddDays(-10 - i);
            setup.Db.ActionTasks.Add(globalOverdue);
        }

        var activeOverdue = NewTask("Active sprint overdue visible", ActionTaskStatuses.Assigned, sprint.Id);
        activeOverdue.DueDate = DateTime.UtcNow.Date.AddDays(-1);
        var activeBlocked = NewTask("Active sprint blocked visible", ActionTaskStatuses.Blocked, sprint.Id);
        activeBlocked.DueDate = DateTime.UtcNow.Date.AddDays(1);
        setup.Db.ActionTasks.AddRange(activeOverdue, activeBlocked);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "CommandCentre";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDashboard.cshtml");
        var sprintStart = html.IndexOf("at-active-sprint-snapshot", StringComparison.Ordinal);
        var attentionStart = html.IndexOf("Attention Required", StringComparison.Ordinal);
        var activeSprint = html[sprintStart..attentionStart];

        // SECTION: Assert
        Assert.Contains("Active Sprint Exceptions (2)", activeSprint, StringComparison.Ordinal);
        Assert.Contains("Active sprint overdue visible", activeSprint, StringComparison.Ordinal);
        Assert.Contains("Active sprint blocked visible", activeSprint, StringComparison.Ordinal);
        Assert.Contains("Overdue", activeSprint, StringComparison.Ordinal);
        Assert.Contains("Blocked", activeSprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardPartial_AttentionRequired_RendersOnlyNonEmptyDeduplicatedLists()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var pendingClosure = NewTask("Pending closure item", ActionTaskStatuses.Submitted);
        var blocked = NewTask("Blocked critical overdue item", ActionTaskStatuses.Blocked);
        blocked.Priority = "Critical";
        blocked.DueDate = DateTime.UtcNow.Date.AddDays(-2);
        var overdue = NewTask("Overdue item", ActionTaskStatuses.Assigned);
        overdue.DueDate = DateTime.UtcNow.Date.AddDays(-1);
        var critical = NewTask("Critical open item", ActionTaskStatuses.Assigned);
        critical.Priority = "Critical";
        setup.Db.ActionTasks.AddRange(pendingClosure, blocked, overdue, critical);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "CommandCentre";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDashboard.cshtml");
        var attentionStart = html.IndexOf("Attention Required", StringComparison.Ordinal);
        var attentionEnd = html.IndexOf("Recent Activity", StringComparison.Ordinal);
        var attention = attentionEnd >= 0 ? html[attentionStart..attentionEnd] : html[attentionStart..];

        // SECTION: Assert
        Assert.Contains("Pending Closure Tasks", attention, StringComparison.Ordinal);
        Assert.Contains("Blocked Tasks", attention, StringComparison.Ordinal);
        Assert.Contains("Overdue Tasks", attention, StringComparison.Ordinal);
        Assert.Contains("Critical Open Tasks", attention, StringComparison.Ordinal);
        Assert.DoesNotContain("No command attention items.", attention, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(attention, "Blocked critical overdue item"));
        Assert.DoesNotContain("Remove from Sprint", attention, StringComparison.Ordinal);
        Assert.DoesNotContain("Return to Backlog", attention, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardPartial_EmptyAttentionAndRecentActivity_DoNotRenderEmptyPanels()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = "CommandCentre";
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskDashboard.cshtml");

        // SECTION: Assert
        Assert.Contains("No command attention items.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Critical Open Tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Overdue Tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Blocked Tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending Closure Tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent Activity", html, StringComparison.Ordinal);
    }


    [Fact]
    public async Task ReportsPartial_RendersCompactFilterBarAndSprintOptions()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Decision Sprint", ActionSprintStatus.Active);
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Reports";
        page.ReportSprintId = sprint.Id;
        await page.OnGetAsync();

        // SECTION: Act
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskReports.cshtml");

        // SECTION: Assert
        Assert.DoesNotContain("<h2>Report Filters</h2>", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportBucket""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportSprintId""", html, StringComparison.Ordinal);
        Assert.Contains("Decision Sprint", html, StringComparison.Ordinal);
        Assert.Contains("Reset / Clear all", html, StringComparison.Ordinal);
        Assert.DoesNotContain("This management view summarises", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Keep reports focused", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>Bucket / Sprint</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span>Bucket</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span>Sprint</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span>Assignee</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>Responsible Person</span>", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-reports-filter-form=""true""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-reports-filter-control=""true""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-reports-date-filter=""true""", html, StringComparison.Ordinal);
        Assert.Contains(@"data-at-reports-filter-loading=""true""", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Apply filters", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selected", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsFilters_PersistStateAndFilterSections()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Filtered Report Sprint", ActionSprintStatus.Active);
        var matching = await setup.Db.ActionTasks.SingleAsync();
        matching.SprintId = sprint.Id;
        matching.Priority = "High";
        matching.Status = ActionTaskStatuses.Blocked;
        matching.AssignedOn = DateTime.UtcNow.Date.AddDays(-9);
        matching.DueDate = DateTime.UtcNow.Date.AddDays(1);
        setup.Db.ActionTasks.Add(NewTask("Outside report scope", ActionTaskStatuses.Assigned));
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Reports";
        page.ReportBucket = "Sprint";
        page.ReportSprintId = sprint.Id;
        page.ReportAssigneeUserId = "user-1";
        page.ReportFromDate = DateTime.UtcNow.Date;
        page.ReportToDate = DateTime.UtcNow.Date.AddDays(2);
        page.ReportStatus = ActionTaskStatuses.Blocked;
        page.ReportPriority = "High";

        // SECTION: Act
        await page.OnGetAsync();
        var html = await RenderPartialAsync(page, "/Pages/ActionTasks/_TaskReports.cshtml");

        // SECTION: Assert
        Assert.True(page.HasReportFilters);
        Assert.Equal(1, page.ReportFilteredTaskCount);
        Assert.Equal(1, page.StatusCounts.Single(x => x.Name == ActionTaskStatuses.Blocked).Count);
        Assert.Equal(1, page.PriorityCounts.Single(x => x.Name == "High").Count);
        Assert.Equal(1, page.BlockedAgeingBuckets.Single(x => x.Name == "8-14 days assigned").Count);
        Assert.Contains("Showing 1 of 2 tasks", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, "Showing 1 of 2 tasks"));
        Assert.DoesNotContain("Showing showing", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"method=""get""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ViewMode"" value=""Reports""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportBucket""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportSprintId""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportAssigneeUserId""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportFromDate"" value=""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportToDate"" value=""", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""ReportStatus""", html, StringComparison.Ordinal);
        Assert.Contains(@"value=""High"" selected", html, StringComparison.Ordinal);
        Assert.Contains(@"value=""user-1"" selected", html, StringComparison.Ordinal);
        Assert.Contains("Ageing and Overdue Analysis", html, StringComparison.Ordinal);
        Assert.Contains("Pending Closure", html, StringComparison.Ordinal);
        Assert.Contains("Workload by Assignee", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Responsible Person Workload", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"<th class=""text-center"">Critical</th>", html, StringComparison.Ordinal);
        Assert.Contains(@"<th class=""text-center"">Unfinished</th>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"<th class=""text-center"">Carried Forward</th>", html, StringComparison.Ordinal);
        Assert.Contains("Bucket Distribution", html, StringComparison.Ordinal);
        Assert.Contains("Backlog Ageing", html, StringComparison.Ordinal);
        Assert.Contains("Assigned Task Ageing", html, StringComparison.Ordinal);
        Assert.Contains("Sprint Performance", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Non-sprint", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportsSprintPerformance_ActiveSubmittedTaskCountsAsOpenAndUnfinished()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var sprint = AddSprint(setup.Db, "Submitted Sprint", ActionSprintStatus.Active);
        var submittedTask = await setup.Db.ActionTasks.SingleAsync();
        submittedTask.Title = "Submitted sprint task";
        submittedTask.Status = ActionTaskStatuses.Submitted;
        submittedTask.SprintId = sprint.Id;
        submittedTask.AssignedToUserId = "user-1";
        submittedTask.SubmittedOn = DateTime.UtcNow.Date;
        await setup.Db.SaveChangesAsync();
        var page = setup.Page;
        page.ViewMode = "Reports";

        // SECTION: Act
        await page.OnGetAsync();
        var performance = Assert.Single(page.SprintPerformanceRows);

        // SECTION: Assert
        Assert.Equal("Submitted Sprint", performance.Sprint);
        Assert.Equal(1, performance.Open);
        Assert.Equal(1, performance.Unfinished);
        Assert.Equal(0, performance.ClosedLate);
    }

    [Fact]
    public async Task SprintHistoryReadModel_ReturnsAuditEventsWithActorVisibility()
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync(RoleNames.Comdt);
        var page = setup.Page;
        page.SprintInput = new IndexModel.CreateSprintInput
        {
            Name = "Audited Sprint",
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(14)
        };
        await page.OnPostCreateSprintAsync();
        var sprint = await setup.Db.ActionSprints.SingleAsync(s => s.Name == "Audited Sprint");
        page.ViewMode = "Planning";
        page.SelectedSprintId = sprint.Id;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Contains(page.SprintAuditHistory, x => x.ActionType == "SprintCreated");
        Assert.Equal("User One", page.ResolveSprintActorName("user-1"));
    }

    [Theory]
    [InlineData("CommandCentre", "Command Centre")]
    [InlineData("Planning", "Sprint Board")]
    [InlineData("MyWork", "My Work")]
    [InlineData("Register", "Register")]
    [InlineData("Reports", "Reports")]
    public void WorkspaceNavigation_UsesCanonicalLabelsAndRoutes(string viewMode, string expectedLabel)
    {
        // SECTION: Arrange
        var pageMarkup = File.ReadAllText(Path.Combine(GetContentRoot(), "Pages", "ActionTasks", "Index.cshtml"));

        // SECTION: Assert
        Assert.Contains($"title=\"{expectedLabel}\"", pageMarkup, StringComparison.Ordinal);
        Assert.Contains($"asp-route-viewMode=\"{viewMode}\"", pageMarkup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Dashboard", "CommandCentre", "Default")]
    [InlineData("My Tasks", "MyWork", "Default")]
    [InlineData("Due Board", "Planning", "DueExceptions")]
    [InlineData("Backlog", "Planning", "Default")]
    [InlineData("Sprints", "Planning", "Default")]
    [InlineData("Kanban", "Planning", "Default")]
    [InlineData("TaskList", "Register", "Default")]
    [InlineData("Sprint Board", "Planning", "DueExceptions")]
    public async Task LegacyViewModeAliases_ResolveToCanonicalWorkspace(string legacyViewMode, string expectedViewMode, string expectedPlanningView)
    {
        // SECTION: Arrange
        var setup = await CreateSetupAsync();
        var page = setup.Page;
        page.ViewMode = legacyViewMode;

        // SECTION: Act
        await page.OnGetAsync();

        // SECTION: Assert
        Assert.Equal(expectedViewMode, page.ResolvedViewMode);
        Assert.Equal(expectedPlanningView, page.ResolvedPlanningView);
    }

    [Fact]
    public void AuditLogModel_DoesNotCreateShadowTaskIdRelationship()
    {
        // SECTION: Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ApplicationDbContext(options);

        // SECTION: Act
        var entityType = db.Model.FindEntityType(typeof(ActionTaskAuditLog))!;
        var taskId1 = entityType.FindProperty("TaskId1");

        // SECTION: Assert
        Assert.Null(taskId1);
        Assert.Single(entityType.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == typeof(ActionTaskItem)));
    }


    private static int CountOccurrences(string value, string searchTerm)
    {
        // SECTION: HTML assertion helper avoids regex dependencies for simple rendered-text counts.
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(searchTerm, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += searchTerm.Length;
        }

        return count;
    }

    private static async Task<string> RenderPartialAsync(IndexModel page, string viewPath)
    {
        // SECTION: Razor partial rendering helper for sprint workspace markup checks.
        await using var writer = new StringWriter();
        using var provider = BuildRazorServiceProvider();
        using var scope = provider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var viewEngine = scopedProvider.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = scopedProvider.GetRequiredService<ITempDataProvider>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scopedProvider,
            User = page.PageContext.HttpContext.User
        };
        var routeData = new RouteData();
        routeData.Values["page"] = "/ActionTasks/Index";
        var actionDescriptor = new ActionDescriptor();
        actionDescriptor.RouteValues["page"] = "/ActionTasks/Index";
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);
        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"Unable to locate {viewPath} partial view.");
        }

        var viewData = new ViewDataDictionary<IndexModel>(new EmptyModelMetadataProvider(), page.ModelState)
        {
            Model = page
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());
        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static string GetContentRoot()
    {
        // SECTION: Test content-root resolution for static markup checks and Razor partial rendering.
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static ServiceProvider BuildRazorServiceProvider()
    {
        // SECTION: Minimal MVC services required to render Razor partials in tests.
        var contentRoot = GetContentRoot();
        var webRoot = Path.Combine(contentRoot, "wwwroot");
        var environment = new TestWebHostEnvironment
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name!,
            ContentRootPath = contentRoot,
            WebRootPath = webRoot,
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot),
            WebRootFileProvider = Directory.Exists(webRoot) ? new PhysicalFileProvider(webRoot) : new NullFileProvider(),
            EnvironmentName = Environments.Development
        };
        var services = new ServiceCollection();
        var diagnosticListener = new DiagnosticListener("Microsoft.AspNetCore");
        services.AddSingleton<DiagnosticListener>(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddLogging();
        services.AddRouting();
        services.AddRazorPages();
        services.AddControllersWithViews();
        return services.BuildServiceProvider();
    }

    private static async Task<(IndexModel Page, ApplicationDbContext Db)> CreateSetupAsync(string currentRole = RoleNames.HoD)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);

        var user = new ApplicationUser
        {
            Id = "user-1", UserName = "user1@test", NormalizedUserName = "USER1@TEST", Email = "user1@test", NormalizedEmail = "USER1@TEST", SecurityStamp = Guid.NewGuid().ToString(), FullName = "User One"
        };
        db.Users.Add(user);
        db.Roles.Add(new IdentityRole { Id = RoleNames.Ta, Name = RoleNames.Ta, NormalizedName = RoleNames.Ta.ToUpperInvariant() });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = RoleNames.Ta });
        db.ActionTasks.Add(new ActionTaskItem
        {
            Title = "Mine", Description = "d", CreatedByUserId = "creator", AssignedToUserId = "user-1", CreatedByRole = RoleNames.HoD, AssignedToRole = RoleNames.Ta, DueDate = DateTime.UtcNow.AddDays(1), Priority = "Normal", AssignedOn = DateTime.UtcNow, Status = ActionTaskStatuses.Assigned
        });
        await db.SaveChangesAsync();

        var sp = new ServiceCollection().BuildServiceProvider();
        var userManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db), Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(), new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), sp, NullLogger<UserManager<ApplicationUser>>.Instance);

        // SECTION: Action task page services
        var permission = new ActionTaskPermissionService();
        var service = new ActionTaskService(db, permission);
        var collab = new StubCollabService();
        var clock = new SystemActionTrackerClock();
        var queryService = new ActionTaskQueryService(clock, new ActionTaskReportBuilder(clock));
        var workflowPolicy = new ActionTaskWorkflowPolicy(permission);
        var sprintWorkflowPolicy = new ActionSprintWorkflowPolicy();
        var sprintService = new ActionSprintService(db, permission, sprintWorkflowPolicy);
        var workspaceBuilder = new ActionTaskWorkspaceBuilder(service, sprintService, queryService);
        var userLookup = new ActionTaskUserLookupService(userManager, permission, clock);
        var page = new IndexModel(service, collab, permission, sprintService, workspaceBuilder, workflowPolicy, new ActionTaskRouteStateHelper(), new ActionTaskMyWorkQueueBuilder(clock), new ActionTaskCommandCentreSummaryBuilder(clock), new ActionTaskSprintWorkspaceSummaryBuilder(clock), new ActionTaskSprintBoardStateBuilder(sprintService, userLookup, clock), new ActionTaskInspectorReadModelBuilder(service, collab, queryService, userLookup), userLookup, clock, userManager);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Role, currentRole)
            }, "TestAuth"))
        };

        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        return (page, db);
    }


    private static ActionSprint AddSprint(ApplicationDbContext db, string name, ActionSprintStatus status, int startOffsetDays = 0)
    {
        // SECTION: Sprint test fixture creation helper.
        var sprint = new ActionSprint
        {
            Name = name,
            Goal = $"Goal for {name}",
            StartDate = DateTime.UtcNow.Date.AddDays(startOffsetDays),
            EndDate = DateTime.UtcNow.Date.AddDays(startOffsetDays + 7),
            Status = status,
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            CreatedAtUtc = DateTime.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        db.ActionSprints.Add(sprint);
        return sprint;
    }

    private static ActionTaskItem NewTask(string title, string status, int? sprintId = null)
    {
        // SECTION: Action task test fixture creation helper.
        return new ActionTaskItem
        {
            Title = title,
            Description = "d",
            CreatedByUserId = "creator",
            AssignedToUserId = "user-1",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(2),
            Priority = "Normal",
            AssignedOn = DateTime.UtcNow,
            Status = status,
            SprintId = sprintId
        };
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        // SECTION: Test host environment for Razor partial rendering.
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubCollabService : IActionTaskCollaborationService
    {
        public Task<ActionTaskUpdate> AddUpdateAsync(int taskId, string body, string updateType, string userId, string role, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>> GetAttachmentMetadataByUpdateAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>)new Dictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>());
        public Task<List<ActionTaskUpdate>> GetUpdatesAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default) => Task.FromResult(new List<ActionTaskUpdate>());
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}
