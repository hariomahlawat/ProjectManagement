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
    private readonly ActionTaskUserLookupService _userLookup;

    public ActionTaskInspectorReadModelBuilder(
        IActionTaskService taskService,
        IActionTaskCollaborationService collaborationService,
        ActionTaskQueryService queryService,
        ActionTaskUserLookupService userLookup)
    {
        _taskService = taskService;
        _collaborationService = collaborationService;
        _ = queryService; // Retained in the constructor to preserve the existing DI contract during the V2 migration.
        _userLookup = userLookup;
    }

    // SECTION: Selected task composition is task-centric rather than list-filter-centric.
    // A direct notification/deep link must remain usable even when the source list is filtered.
    public async Task<ActionTaskInspectorReadModel> BuildAsync(ActionTaskInspectorReadModelRequest request)
    {
        if (!request.TaskId.HasValue)
        {
            return ActionTaskInspectorReadModel.Empty;
        }

        return await BuildAsync(request.TaskId.Value, request.CurrentUserId, request.CurrentRole);
    }

    // SECTION: Shared task-detail composition used by both the Peek drawer and full workspace.
    public async Task<ActionTaskInspectorReadModel> BuildAsync(int taskId, string currentUserId, string currentRole)
    {
        var selectedTask = await _taskService.GetTaskAsync(taskId);
        if (selectedTask is null)
        {
            return ActionTaskInspectorReadModel.Unavailable;
        }

        try
        {
            // These reads enforce the authoritative task-thread/log visibility rules.
            var logs = await _taskService.GetTaskLogsAsync(selectedTask.Id, currentUserId, currentRole);
            var updates = await _collaborationService.GetUpdatesAsync(selectedTask.Id, currentUserId, currentRole);
            var attachments = await _collaborationService.GetAttachmentMetadataByUpdateAsync(selectedTask.Id, currentUserId, currentRole);
            var actorNames = await _userLookup.LoadTaskActorNamesAsync(logs);
            actorNames = await _userLookup.MergeTaskAuditReferencedUserNamesAsync(actorNames, logs);
            actorNames = await _userLookup.MergeUpdateActorNamesAsync(actorNames, updates);
            var lastActivityUtc = ResolveLastActivityUtc(selectedTask, logs, updates);

            return new ActionTaskInspectorReadModel(selectedTask, logs, updates, attachments, actorNames, false, lastActivityUtc);
        }
        catch (InvalidOperationException)
        {
            return ActionTaskInspectorReadModel.Unavailable;
        }
    }

    // SECTION: Last activity combines human updates, system history and task lifecycle timestamps.
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
