using System;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public interface IActionTaskNotificationService
{
    Task NotifyTaskAssignedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifyProgressUpdatedAsync(ActionTaskItem task, ActionTaskUpdate? update, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifyStatusChangedAsync(ActionTaskItem task, string previousStatus, string newStatus, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifyTaskBlockedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifySubmittedForClosureAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifyTaskClosedAsync(ActionTaskItem task, string actorUserId, bool closedByCommandAuthority, CancellationToken cancellationToken = default);

    Task NotifyDueDateChangedAsync(ActionTaskItem task, DateTime oldDate, DateTime newDate, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifyMovedToBacklogAsync(ActionTaskItem task, string? previousAssigneeUserId, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifyRemovedFromSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default);

    Task NotifyAddedToSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default);
}
