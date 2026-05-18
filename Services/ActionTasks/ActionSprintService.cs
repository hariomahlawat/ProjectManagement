using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public class ActionSprintService
{
    // SECTION: Audit persistence limits
    private const int AuditRemarksMaxLength = 2000;

    private readonly ApplicationDbContext _context;
    private readonly ActionTaskPermissionService _permission;
    private readonly ActionSprintWorkflowPolicy _workflow;
    private readonly IActionTrackerClock _clock;
    private readonly IActionTaskNotificationService? _notifications;

    public ActionSprintService(
        ApplicationDbContext context,
        ActionTaskPermissionService permission,
        ActionSprintWorkflowPolicy workflow,
        IActionTrackerClock clock,
        IActionTaskNotificationService? notifications = null)
    {
        _context = context;
        _permission = permission;
        _workflow = workflow;
        _clock = clock;
        _notifications = notifications;
    }


    // SECTION: Sprint read APIs
    public async Task<List<ActionSprint>> GetSprintsAsync(CancellationToken cancellationToken = default)
        => await _context.ActionSprints
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.Status == ActionSprintStatus.Active)
            .ThenByDescending(x => x.StartDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<ActionSprint?> GetActiveSprintAsync(CancellationToken cancellationToken = default)
        => await _context.ActionSprints
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == ActionSprintStatus.Active)
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<List<ActionSprintAuditLog>> GetSprintAuditHistoryAsync(int sprintId, CancellationToken cancellationToken = default)
        => await _context.ActionSprintAuditLogs
            .AsNoTracking()
            .Where(x => x.SprintId == sprintId)
            .Where(x => x.ActionType == "SprintCreated" || x.ActionType == "SprintUpdated" || x.ActionType == "SprintActivated" || x.ActionType == "SprintClosed")
            .OrderByDescending(x => x.PerformedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

    // SECTION: Sprint mutation APIs
    public async Task<ActionSprint> CreateSprintAsync(ActionSprint sprint, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanCreateSprint(role);
        _workflow.ValidateDateRange(sprint.StartDate, sprint.EndDate);

        var performedAt = _clock.UtcNow;
        sprint.Status = ActionSprintStatus.Planned;
        sprint.RowVersion = NewSprintRowVersion();
        sprint.CreatedByUserId = userId;
        sprint.CreatedByRole = role;
        sprint.CreatedAtUtc = performedAt;
        sprint.StartDate = sprint.StartDate.Date;
        sprint.EndDate = sprint.EndDate.Date;

        _context.ActionSprints.Add(sprint);
        await _context.SaveChangesAsync(cancellationToken);

        AddSprintAudit(sprint.Id, "SprintCreated", userId, role, performedAt, null, DescribeSprint(sprint), $"Created sprint: {sprint.Name}");
        await _context.SaveChangesAsync(cancellationToken);
        return sprint;
    }

    public async Task<ActionSprint> UpdateSprintAsync(int sprintId, byte[] rowVersion, string name, string? goal, DateTime startDate, DateTime endDate, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanEditSprint(role);
        _workflow.ValidateDateRange(startDate, endDate);

        EnsureSprintRowVersionSupplied(rowVersion);

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanUpdate(sprint);
        ApplySprintRowVersion(sprint, rowVersion);

        var performedAt = _clock.UtcNow;
        var oldValue = DescribeSprint(sprint);

        sprint.Name = name;
        sprint.Goal = goal;
        sprint.StartDate = startDate.Date;
        sprint.EndDate = endDate.Date;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        AddSprintAudit(sprint.Id, "SprintUpdated", userId, role, performedAt, oldValue, DescribeSprint(sprint), $"Updated sprint: {sprint.Name}");
        await SaveSprintChangesAsync(cancellationToken);
        return sprint;
    }

    public async Task<ActionSprint> ActivateSprintAsync(int sprintId, byte[] rowVersion, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanActivateSprint(role);

        EnsureSprintRowVersionSupplied(rowVersion);

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanActivate(sprint);
        ApplySprintRowVersion(sprint, rowVersion);
        _workflow.ValidateDateRange(sprint.StartDate, sprint.EndDate);

        var activeSprintExists = await _context.ActionSprints
            .AnyAsync(x => x.Id != sprintId && !x.IsDeleted && x.Status == ActionSprintStatus.Active, cancellationToken);
        if (activeSprintExists)
        {
            throw new InvalidOperationException("Only one active sprint is allowed.");
        }

        var performedAt = _clock.UtcNow;
        var oldStatus = sprint.Status.ToString();

        sprint.Status = ActionSprintStatus.Active;
        sprint.ActivatedAtUtc = performedAt;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        AddSprintAudit(sprint.Id, "SprintActivated", userId, role, performedAt, oldStatus, sprint.Status.ToString(), $"Activated sprint: {sprint.Name}");
        await SaveSprintChangesAsync(cancellationToken);
        return sprint;
    }

    public async Task<ActionSprint> CloseSprintAsync(int sprintId, byte[] rowVersion, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanCloseSprint(role);

        EnsureSprintRowVersionSupplied(rowVersion);

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanClose(sprint);
        ApplySprintRowVersion(sprint, rowVersion);

        var unfinishedTaskCount = await CountUnfinishedSprintTasksAsync(sprint.Id, cancellationToken);
        if (unfinishedTaskCount > 0)
        {
            throw new InvalidOperationException("Sprint contains unfinished tasks. Use the closure review to carry forward, remove from sprint while keeping assignee, or move unfinished tasks to backlog before closing.");
        }

        var performedAt = _clock.UtcNow;
        var oldStatus = sprint.Status.ToString();

        sprint.Status = ActionSprintStatus.Closed;
        sprint.ClosedAtUtc = performedAt;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        AddSprintAudit(sprint.Id, "SprintClosed", userId, role, performedAt, oldStatus, sprint.Status.ToString(), $"Closed sprint: {sprint.Name}");
        await SaveSprintChangesAsync(cancellationToken);
        return sprint;
    }

    public Task<ActionSprint> CloseSprintWithDispositionAsync(int sprintId, byte[] rowVersion, IReadOnlyCollection<int> carryForwardTaskIds, int? targetSprintId, IReadOnlyCollection<int> backlogTaskIds, string remarks, string userId, string role, CancellationToken cancellationToken = default)
        => CloseSprintWithDispositionAsync(sprintId, rowVersion, carryForwardTaskIds, targetSprintId, Array.Empty<int>(), backlogTaskIds, remarks, userId, role, cancellationToken);

    public async Task<ActionSprint> CloseSprintWithDispositionAsync(int sprintId, byte[] rowVersion, IReadOnlyCollection<int> carryForwardTaskIds, int? targetSprintId, IReadOnlyCollection<int> outsideSprintTaskIds, IReadOnlyCollection<int> backlogTaskIds, string remarks, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanCloseSprint(role);
        EnsureSprintRowVersionSupplied(rowVersion);
        if (string.IsNullOrWhiteSpace(remarks))
        {
            throw new InvalidOperationException("Closure remarks are required when closing a sprint through closure review.");
        }

        var carryIds = (carryForwardTaskIds ?? Array.Empty<int>()).Distinct().ToList();
        var outsideIds = (outsideSprintTaskIds ?? Array.Empty<int>()).Distinct().ToList();
        var backIds = (backlogTaskIds ?? Array.Empty<int>()).Distinct().ToList();
        var duplicateDispositionIds = carryIds.Concat(outsideIds).Concat(backIds)
            .GroupBy(id => id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateDispositionIds.Count > 0)
        {
            throw new InvalidOperationException("Each unfinished task can have only one closure disposition.");
        }

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanClose(sprint);
        ApplySprintRowVersion(sprint, rowVersion);

        ActionSprint? targetSprint = null;
        if (carryIds.Count > 0)
        {
            if (!targetSprintId.HasValue)
            {
                throw new InvalidOperationException("Select a target sprint for carry-forward tasks.");
            }

            targetSprint = await GetSprintForUpdateAsync(targetSprintId.Value, cancellationToken);
            _workflow.EnsureCanCarryForward(sprint, targetSprint);
        }

        var unfinishedTasks = await _context.ActionTasks
            .Where(t => !t.IsDeleted && t.SprintId == sprint.Id && t.Status != ActionTaskStatuses.Closed)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var dispositionedIds = carryIds.Concat(outsideIds).Concat(backIds).Distinct().ToHashSet();
        var missingDispositionIds = unfinishedTasks.Select(t => t.Id).Where(id => !dispositionedIds.Contains(id)).ToList();
        if (missingDispositionIds.Count > 0)
        {
            throw new InvalidOperationException("All unfinished tasks must be carried forward, removed from sprint while assigned, or moved to backlog before the sprint can close.");
        }

        var invalidDispositionIds = dispositionedIds.Except(unfinishedTasks.Select(t => t.Id)).ToList();
        if (invalidDispositionIds.Count > 0)
        {
            throw new InvalidOperationException("Closure disposition includes tasks that are not unfinished tasks in this sprint.");
        }

        var unsafeAssignedDispositionIds = unfinishedTasks
            .Where(t => (carryIds.Contains(t.Id) || outsideIds.Contains(t.Id)) && ActionTaskCategorization.ResolveBucket(t) == ActionTaskBucket.Invalid)
            .Select(t => t.Id)
            .ToList();
        if (unsafeAssignedDispositionIds.Count > 0)
        {
            throw new InvalidOperationException("Tasks with invalid sprint assignment must be returned to backlog or corrected before sprint closure.");
        }

        var performedAt = _clock.UtcNow;
        foreach (var task in unfinishedTasks.Where(t => carryIds.Contains(t.Id)))
        {
            var oldValue = DescribeTaskBucket(task);
            task.SprintId = targetSprint!.Id;
            ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
            AddTaskSprintAudit(task.Id, "TaskCarriedForward", userId, role, oldValue, DescribeTaskBucket(task), $"Carried forward from sprint {sprint.Name} to {targetSprint.Name}. Closure remarks: {remarks.Trim()}", performedAt);
        }

        foreach (var task in unfinishedTasks.Where(t => outsideIds.Contains(t.Id)))
        {
            var oldValue = DescribeTaskBucket(task);
            task.SprintId = null;
            ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
            AddTaskSprintAudit(task.Id, "TaskRemovedFromSprintKeepAssigned", userId, role, oldValue, DescribeTaskBucket(task), $"Removed from sprint {sprint.Name} and kept assigned during closure. Closure remarks: {remarks.Trim()}", performedAt);
        }

        foreach (var task in unfinishedTasks.Where(t => backIds.Contains(t.Id)))
        {
            var oldValue = DescribeTaskBucket(task);
            task.SprintId = null;
            task.AssignedToUserId = string.Empty;
            task.AssignedToRole = string.Empty;
            task.Status = ActionTaskStatuses.Backlog;
            task.SubmittedOn = null;
            task.ClosedOn = null;
            ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
            AddTaskSprintAudit(task.Id, "TaskMovedToBacklogRemoveAssignee", userId, role, oldValue, DescribeTaskBucket(task), $"Moved from sprint {sprint.Name} to backlog and removed assignee during closure. Closure remarks: {remarks.Trim()}", performedAt);
        }

        var oldStatus = sprint.Status.ToString();
        sprint.Status = ActionSprintStatus.Closed;
        sprint.ClosedAtUtc = performedAt;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        var detail = $"Closed sprint: {sprint.Name}. Carried forward: {carryIds.Count}. Removed from sprint, kept assigned: {outsideIds.Count}. Moved to backlog, assignee removed: {backIds.Count}. Remarks: {remarks.Trim()}";
        AddSprintAudit(sprint.Id, "SprintClosed", userId, role, performedAt, oldStatus, sprint.Status.ToString(), detail);
        await SaveSprintChangesAsync(cancellationToken);
        return sprint;
    }

    // SECTION: Sprint task assignment APIs
    public async Task<ActionTaskItem> AssignBacklogItemToSprintAsync(int taskId, int sprintId, string responsibleUserId, string responsibleRole, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanAssignTaskToSprint(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        EnsureTaskIsNotClosed(task, "Closed tasks cannot be assigned to a sprint.");
        if (!ActionTaskCategorization.IsBacklogTask(task))
        {
            throw new InvalidOperationException("Only backlog items can be assigned to sprint through this action.");
        }

        if (string.IsNullOrWhiteSpace(responsibleUserId) || string.IsNullOrWhiteSpace(responsibleRole))
        {
            throw new InvalidOperationException("Select a responsible person before adding a backlog item to a sprint.");
        }

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanAcceptTask(sprint);

        var oldValue = DescribeTaskBucket(task);
        task.AssignedToUserId = responsibleUserId;
        task.AssignedToRole = responsibleRole;
        task.AssignedOn = _clock.UtcNow;
        task.SprintId = sprint.Id;
        task.Status = ActionTaskStatuses.Assigned;
        task.SubmittedOn = null;
        task.ClosedOn = null;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
        AddTaskSprintAudit(task.Id, "TaskAssignedToSprint", userId, role, oldValue, DescribeTaskBucket(task), $"Assigned to sprint: {sprint.Name}; responsible person selected.");

        await _context.SaveChangesAsync(cancellationToken);

        // SECTION: In-app notification after successful persistence
        if (_notifications is not null)
        {
            await _notifications.NotifyTaskAssignedAsync(task, userId, cancellationToken);
        }

        return task;
    }

    public async Task<ActionTaskItem> AssignOutsideSprintTaskToSprintAsync(int taskId, int sprintId, string userId, string role, CancellationToken cancellationToken = default)
    {
        // SECTION: Outside Sprint movement reuses the existing responsible person and only selects a target sprint.
        EnsureCanAssignTaskToSprint(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        EnsureTaskIsNotClosed(task, "Closed tasks cannot be assigned to a sprint.");
        if (!ActionTaskCategorization.IsOutsideSprintTask(task))
        {
            throw new InvalidOperationException("Only assigned tasks outside sprint can be added to sprint through this action.");
        }

        if (string.IsNullOrWhiteSpace(task.AssignedToUserId) || string.IsNullOrWhiteSpace(task.AssignedToRole))
        {
            throw new InvalidOperationException("Outside Sprint tasks must have a responsible person before adding to a sprint.");
        }

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanAcceptTask(sprint);

        var oldValue = DescribeTaskBucket(task);
        task.SprintId = sprint.Id;
        task.ClosedOn = null;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
        AddTaskSprintAudit(task.Id, "OutsideSprintTaskAssignedToSprint", userId, role, oldValue, DescribeTaskBucket(task), $"Added Outside Sprint task to sprint: {sprint.Name}; responsible person retained.");

        await _context.SaveChangesAsync(cancellationToken);

        // SECTION: In-app notification after successful persistence
        if (_notifications is not null)
        {
            await _notifications.NotifyAddedToSprintAsync(task, userId, cancellationToken);
        }

        return task;
    }

    public async Task<ActionTaskItem> RemoveTaskFromSprintKeepAssignedAsync(int taskId, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        EnsureCanMoveTaskToBacklog(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        EnsureTaskIsNotClosed(task, "Closed tasks cannot be moved between sprint buckets.");
        if (!ActionTaskCategorization.IsSprintTask(task))
        {
            throw new InvalidOperationException("Only sprint tasks can be removed from sprint.");
        }

        await EnsureCurrentSprintCanChangeAsync(task, cancellationToken);

        var oldValue = DescribeTaskBucket(task);
        task.SprintId = null;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
        AddTaskSprintAudit(task.Id, "TaskRemovedFromSprintKeepAssigned", userId, role, oldValue, DescribeTaskBucket(task), AppendRemark("Removed from sprint and kept assigned as Outside Sprint work.", remarks));

        await _context.SaveChangesAsync(cancellationToken);

        // SECTION: In-app notification after successful persistence
        if (_notifications is not null)
        {
            await _notifications.NotifyRemovedFromSprintAsync(task, userId, cancellationToken);
        }

        return task;
    }

    public Task<ActionTaskItem> MoveTaskToBacklogAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
        => MoveTaskToBacklogRemoveAssigneeAsync(taskId, userId, role, null, cancellationToken);

    public async Task<ActionTaskItem> MoveTaskToBacklogRemoveAssigneeAsync(int taskId, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        EnsureCanMoveTaskToBacklog(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        EnsureTaskIsNotClosed(task, "Closed tasks cannot be moved between sprint buckets.");
        if (ActionTaskCategorization.IsBacklogTask(task))
        {
            throw new InvalidOperationException("Task is already in backlog.");
        }

        await EnsureCurrentSprintCanChangeAsync(task, cancellationToken);

        var oldValue = DescribeTaskBucket(task);
        var previousAssigneeUserId = task.AssignedToUserId;
        task.SprintId = null;
        task.AssignedToUserId = string.Empty;
        task.AssignedToRole = string.Empty;
        task.Status = ActionTaskStatuses.Backlog;
        task.SubmittedOn = null;
        task.ClosedOn = null;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);
        AddTaskSprintAudit(task.Id, "TaskMovedToBacklogRemoveAssignee", userId, role, oldValue, DescribeTaskBucket(task), AppendRemark("Moved to backlog and removed assignee.", remarks));

        await _context.SaveChangesAsync(cancellationToken);

        // SECTION: In-app notification after successful persistence
        if (_notifications is not null)
        {
            await _notifications.NotifyMovedToBacklogAsync(task, previousAssigneeUserId, userId, cancellationToken);
        }

        return task;
    }

    // SECTION: Sprint bucket mutation helpers
    private async Task EnsureCurrentSprintCanChangeAsync(ActionTaskItem task, CancellationToken cancellationToken)
    {
        if (task.SprintId.HasValue)
        {
            var currentSprint = await GetSprintForUpdateAsync(task.SprintId.Value, cancellationToken);
            _workflow.EnsureCanUpdate(currentSprint);
        }
    }

    private static string DescribeTaskBucket(ActionTaskItem task)
        => $"Bucket={ActionTaskBucketClassifier.ResolveBucket(task)}; Status={task.Status}; SprintId={task.SprintId?.ToString() ?? "none"}; Assignee={(string.IsNullOrWhiteSpace(task.AssignedToUserId) ? "none" : task.AssignedToUserId)}";

    private static string AppendRemark(string systemMessage, string? remarks)
    {
        // SECTION: Keep planning audit messages readable while retaining the required human reason.
        return string.IsNullOrWhiteSpace(remarks)
            ? systemMessage
            : $"{systemMessage} Reason: {remarks.Trim()}";
    }

    // SECTION: Authorization helpers
    private async Task<int> CountUnfinishedSprintTasksAsync(int sprintId, CancellationToken cancellationToken)
        => await _context.ActionTasks.CountAsync(t => !t.IsDeleted && t.SprintId == sprintId && t.Status != ActionTaskStatuses.Closed, cancellationToken);

    private void EnsureCanCreateSprint(string role)
    {
        if (!_permission.CanCreateSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to create sprints.");
        }
    }

    private void EnsureCanEditSprint(string role)
    {
        if (!_permission.CanEditSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to update sprints.");
        }
    }

    private void EnsureCanActivateSprint(string role)
    {
        if (!_permission.CanActivateSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to activate sprints.");
        }
    }

    private void EnsureCanCloseSprint(string role)
    {
        if (!_permission.CanCloseSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to close sprints.");
        }
    }

    private void EnsureCanAssignTaskToSprint(string role)
    {
        if (!_permission.CanAssignTaskToSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to assign tasks to sprints.");
        }
    }

    private void EnsureCanMoveTaskToBacklog(string role)
    {
        if (!_permission.CanMoveTaskToBacklog(role))
        {
            throw new InvalidOperationException("You are not authorized to move tasks to backlog.");
        }
    }

    // SECTION: Entity loading helpers
    private async Task<ActionSprint> GetSprintForUpdateAsync(int sprintId, CancellationToken cancellationToken)
        => await _context.ActionSprints.FirstOrDefaultAsync(x => x.Id == sprintId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("Sprint not found.");

    private async Task<ActionTaskItem> GetTaskForUpdateAsync(int taskId, CancellationToken cancellationToken)
        => await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("Task not found.");

    private static void EnsureTaskIsNotClosed(ActionTaskItem task, string message)
    {
        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    // SECTION: Sprint concurrency helpers
    private static void EnsureSprintRowVersionSupplied(byte[] rowVersion)
    {
        if (rowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException("Sprint row version is required for this operation.");
        }
    }

    private void ApplySprintRowVersion(ActionSprint sprint, byte[] rowVersion)
    {
        _context.Entry(sprint).Property(x => x.RowVersion).OriginalValue = rowVersion;
    }

    private async Task SaveSprintChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ActionTaskConcurrencyException("This sprint was updated by another user. Please reload the sprint details and try again.");
        }
    }

    // SECTION: Audit helpers
    private void AddTaskSprintAudit(int taskId, string actionType, string userId, string role, string? oldValue, string? newValue, string remarks, DateTime? performedAt = null)
    {
        _context.ActionTaskAuditLogs.Add(new ActionTaskAuditLog
        {
            TaskId = taskId,
            ActionType = actionType,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = performedAt ?? _clock.UtcNow,
            OldValue = oldValue,
            NewValue = newValue,
            Remarks = TrimAuditRemarks(remarks)
        });
    }

    private void AddSprintAudit(int sprintId, string actionType, string userId, string role, DateTime performedAt, string? oldValue, string? newValue, string remarks)
    {
        _context.ActionSprintAuditLogs.Add(new ActionSprintAuditLog
        {
            SprintId = sprintId,
            ActionType = actionType,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = performedAt,
            OldValue = oldValue,
            NewValue = newValue,
            Remarks = TrimAuditRemarks(remarks)
        });
    }

    private static byte[] NewSprintRowVersion() => Guid.NewGuid().ToByteArray();

    private static string TrimAuditRemarks(string remarks)
    {
        if (remarks.Length <= AuditRemarksMaxLength)
        {
            return remarks;
        }

        return remarks[..AuditRemarksMaxLength];
    }

    private static string DescribeSprint(ActionSprint sprint)
        => $"Name={sprint.Name}; Goal={sprint.Goal}; StartDate={sprint.StartDate:yyyy-MM-dd}; EndDate={sprint.EndDate:yyyy-MM-dd}; Status={sprint.Status}";
}
