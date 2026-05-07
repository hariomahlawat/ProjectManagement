using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskWorkspaceBuilder
{
    private readonly IActionTaskService _taskService;
    private readonly ActionSprintService _sprintService;
    private readonly ActionTaskQueryService _queryService;

    public ActionTaskWorkspaceBuilder(IActionTaskService taskService, ActionSprintService sprintService, ActionTaskQueryService queryService)
    {
        _taskService = taskService;
        _sprintService = sprintService;
        _queryService = queryService;
    }

    // SECTION: Workspace composition boundary gathers task, sprint and activity inputs before read-model projection.
    public async Task<ActionTaskWorkspaceReadModel> BuildAsync(ActionTaskWorkspaceRequest request, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var tasks = await _taskService.GetTasksAsync(request.CurrentUserId, request.CurrentRole);
        var sprints = await _sprintService.GetSprintsAsync();
        var activityByTaskId = await _taskService.GetLastActivityUtcByTaskIdsAsync(tasks.Select(t => t.Id).ToArray());
        var queryRequest = new ActionTaskQueryService.ActionTaskQueryRequest(
            request.CurrentUserId,
            request.IsMyTasksView,
            request.IsTaskListView,
            request.IsBacklogView,
            request.SelectedSprintId,
            sprints,
            request.FilterStatus,
            request.FilterPriority,
            request.FilterAssigneeUserId,
            request.FilterDueDate,
            request.FilterSearch,
            request.SortBy,
            request.SortDir,
            request.ReportSprintId,
            request.ReportAssigneeUserId,
            request.ReportFromDate,
            request.ReportToDate,
            request.ReportStatus,
            request.ReportPriority);

        return new ActionTaskWorkspaceReadModel(tasks, _queryService.BuildReadModel(tasks, queryRequest, assigneeNames, activityByTaskId));
    }
}

public sealed record ActionTaskWorkspaceRequest(
    string CurrentUserId,
    string CurrentRole,
    bool IsMyTasksView,
    bool IsTaskListView,
    bool IsBacklogView,
    int? SelectedSprintId,
    string? FilterStatus,
    string? FilterPriority,
    string? FilterAssigneeUserId,
    DateTime? FilterDueDate,
    string? FilterSearch,
    string? SortBy,
    string? SortDir,
    int? ReportSprintId,
    string? ReportAssigneeUserId,
    DateTime? ReportFromDate,
    DateTime? ReportToDate,
    string? ReportStatus,
    string? ReportPriority);

public sealed record ActionTaskWorkspaceReadModel(IReadOnlyList<ActionTaskItem> SourceTasks, ActionTaskQueryService.ActionTaskReadModel ReadModel);
