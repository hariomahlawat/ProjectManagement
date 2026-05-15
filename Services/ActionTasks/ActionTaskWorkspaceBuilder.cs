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

    // SECTION: Workspace composition boundary uses the caller-provided task snapshot before read-model projection.
    public async Task<ActionTaskWorkspaceReadModel> BuildAsync(ActionTaskWorkspaceRequest request, IReadOnlyList<ActionTaskItem> sourceTasks, IReadOnlyDictionary<string, string> assigneeNames)
    {
        var sprints = await _sprintService.GetSprintsAsync();
        var activityByTaskId = await _taskService.GetLastActivityUtcByTaskIdsAsync(sourceTasks.Select(t => t.Id).ToArray());
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
            request.ReportPriority,
            request.FilterBucket);

        return new ActionTaskWorkspaceReadModel(sourceTasks, _queryService.BuildReadModel(sourceTasks, queryRequest, assigneeNames, activityByTaskId));
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
    string? FilterBucket,
    int? ReportSprintId,
    string? ReportAssigneeUserId,
    DateTime? ReportFromDate,
    DateTime? ReportToDate,
    string? ReportStatus,
    string? ReportPriority);

public sealed record ActionTaskWorkspaceReadModel(IReadOnlyList<ActionTaskItem> SourceTasks, ActionTaskQueryService.ActionTaskReadModel ReadModel);
