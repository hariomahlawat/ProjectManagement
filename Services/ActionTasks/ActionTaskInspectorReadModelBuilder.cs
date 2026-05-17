using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskInspectorReadModelBuilder
{
    private readonly IActionTaskService _taskService;
    private readonly IActionTaskCollaborationService _collaborationService;
    private readonly ActionTaskQueryService _queryService;
    private readonly ActionTaskUserLookupService _userLookup;

    public ActionTaskInspectorReadModelBuilder(
        IActionTaskService taskService,
        IActionTaskCollaborationService collaborationService,
        ActionTaskQueryService queryService,
        ActionTaskUserLookupService userLookup)
    {
        _taskService = taskService;
        _collaborationService = collaborationService;
        _queryService = queryService;
        _userLookup = userLookup;
    }

    // SECTION: Selected task inspector composition keeps drawer/deep-link preparation out of the page model.
    public async Task<ActionTaskInspectorReadModel> BuildAsync(ActionTaskInspectorReadModelRequest request)
    {
        if (!request.TaskId.HasValue)
        {
            return ActionTaskInspectorReadModel.Empty;
        }

        var selectedTask = _queryService.SelectTask(request.ScopeTasks, request.TaskId);
        if (selectedTask is null)
        {
            return ActionTaskInspectorReadModel.Unavailable;
        }

        var logs = await _taskService.GetTaskLogsAsync(selectedTask.Id, request.CurrentUserId, request.CurrentRole);
        var updates = await _collaborationService.GetUpdatesAsync(selectedTask.Id, request.CurrentUserId, request.CurrentRole);
        var attachments = await _collaborationService.GetAttachmentMetadataByUpdateAsync(selectedTask.Id, request.CurrentUserId, request.CurrentRole);
        var actorNames = await _userLookup.LoadTaskActorNamesAsync(logs);
        actorNames = await _userLookup.MergeUpdateActorNamesAsync(actorNames, updates);
        var lastActivityUtc = ResolveLastActivityUtc(selectedTask, logs, updates);

        return new ActionTaskInspectorReadModel(selectedTask, logs, updates, attachments, actorNames, false, lastActivityUtc);
    }

    // SECTION: Last activity combines human updates, system history and task lifecycle timestamps for the drawer header.
    private static DateTime ResolveLastActivityUtc(ActionTaskItem task, IReadOnlyList<ActionTaskAuditLog> logs, IReadOnlyList<ActionTaskUpdate> updates)
    {
        var activityCandidates = new List<DateTime> { task.AssignedOn };
        if (task.SubmittedOn.HasValue)
        {
            activityCandidates.Add(task.SubmittedOn.Value);
        }

        if (task.ClosedOn.HasValue)
        {
            activityCandidates.Add(task.ClosedOn.Value);
        }

        activityCandidates.AddRange(logs.Select(log => log.PerformedAt));
        activityCandidates.AddRange(updates.Select(update => update.CreatedAtUtc));
        return activityCandidates.Max();
    }
}

public sealed record ActionTaskInspectorReadModelRequest(
    int? TaskId,
    IReadOnlyList<ActionTaskItem> ScopeTasks,
    string CurrentUserId,
    string CurrentRole);

public sealed record ActionTaskInspectorReadModel(
    ActionTaskItem? SelectedTask,
    IReadOnlyList<ActionTaskAuditLog> Logs,
    IReadOnlyList<ActionTaskUpdate> Updates,
    IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>> UpdateAttachments,
    IReadOnlyDictionary<string, string> ActorNames,
    bool IsUnavailable,
    DateTime? LastActivityAtUtc)
{
    public static ActionTaskInspectorReadModel Empty { get; } = new(
        null,
        Array.Empty<ActionTaskAuditLog>(),
        Array.Empty<ActionTaskUpdate>(),
        new Dictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        false,
        null);

    public static ActionTaskInspectorReadModel Unavailable { get; } = Empty with { IsUnavailable = true };
}
