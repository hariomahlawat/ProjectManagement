using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public class ActionTaskService : IActionTaskService
{
    private const int MaxClosureRemarksLength = 2000;

    private readonly ApplicationDbContext _context;
    private readonly ActionTaskPermissionService _permission;
    private readonly IActionTrackerClock _clock;

    public ActionTaskService(ApplicationDbContext context, ActionTaskPermissionService permission, IActionTrackerClock clock)
    {
        _context = context;
        _permission = permission;
        _clock = clock;
    }

    // SECTION: Task read APIs
    public async Task<List<ActionTaskItem>> GetTasksAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        var query = _context.ActionTasks
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!_permission.CanViewAll(role))
        {
            query = query.Where(x => x.AssignedToUserId == userId);
        }

        return await query
            .OrderByDescending(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<ActionTaskItem?> GetTaskAsync(int taskId, CancellationToken cancellationToken = default)
        => _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken);

    public async Task<List<ActionTaskAuditLog>> GetTaskLogsAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskAsync(taskId, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanViewLogs(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to view logs for this task.");
        }

        return await _context.ActionTaskAuditLogs
            .AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.PerformedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }


    public async Task<Dictionary<int, DateTime?>> GetLastActivityUtcByTaskIdsAsync(IReadOnlyCollection<int> taskIds, CancellationToken cancellationToken = default)
    {
        if (taskIds.Count == 0)
        {
            return new Dictionary<int, DateTime?>();
        }

        var updateActivity = await _context.ActionTaskUpdates
            .AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskId))
            .GroupBy(x => x.TaskId)
            .Select(g => new { TaskId = g.Key, LastUpdateUtc = g.Max(x => x.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.TaskId, x => (DateTime?)x.LastUpdateUtc, cancellationToken);

        var auditActivity = await _context.ActionTaskAuditLogs
            .AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskId))
            .GroupBy(x => x.TaskId)
            .Select(g => new { TaskId = g.Key, LastAuditUtc = g.Max(x => x.PerformedAt) })
            .ToDictionaryAsync(x => x.TaskId, x => (DateTime?)x.LastAuditUtc, cancellationToken);

        var result = new Dictionary<int, DateTime?>(taskIds.Count);
        foreach (var taskId in taskIds)
        {
            updateActivity.TryGetValue(taskId, out var lastUpdateUtc);
            auditActivity.TryGetValue(taskId, out var lastAuditUtc);
            result[taskId] = GetLatestActivityUtc(lastUpdateUtc, lastAuditUtc);
        }

        return result;
    }


    // SECTION: Last activity helpers
    private static DateTime? GetLatestActivityUtc(DateTime? lastUpdateUtc, DateTime? lastAuditUtc)
    {
        if (lastUpdateUtc is null)
        {
            return lastAuditUtc;
        }

        if (lastAuditUtc is null)
        {
            return lastUpdateUtc;
        }

        return lastUpdateUtc > lastAuditUtc ? lastUpdateUtc : lastAuditUtc;
    }

    // SECTION: Task mutation APIs
    public async Task<ActionTaskItem> CreateTaskAsync(ActionTaskItem task, CancellationToken cancellationToken = default)
    {
        // SECTION: Validation
        if (string.IsNullOrWhiteSpace(task.AssignedToUserId) || string.IsNullOrWhiteSpace(task.AssignedToRole))
        {
            throw new InvalidOperationException("Direct tasks require a responsible person.");
        }

        // SECTION: State Mutation
        task.AssignedOn = _clock.UtcNow;
        task.Status = ActionTaskStatuses.Assigned;
        task.SprintId = null;
        task.SubmittedOn = null;
        task.ClosedOn = null;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);

        _context.ActionTasks.Add(task);

        // SECTION: Audit Enqueue
        _context.ActionTaskAuditLogs.Add(Log(
            action: "TaskCreated",
            userId: task.CreatedByUserId,
            role: task.CreatedByRole,
            oldValue: null,
            newValue: task.Status,
            remarks: null,
            task: task));

        // SECTION: Persistence
        await _context.SaveChangesAsync(cancellationToken);

        return task;
    }

    public async Task<ActionTaskItem> CreateBacklogItemAsync(ActionTaskItem task, CancellationToken cancellationToken = default)
    {
        // SECTION: Backlog creation enforces backlog items state rather than generic assigned-task defaults.
        task.AssignedOn = _clock.UtcNow;
        task.Status = ActionTaskStatuses.Backlog;
        task.SprintId = null;
        task.AssignedToUserId = string.Empty;
        task.AssignedToRole = string.Empty;
        task.SubmittedOn = null;
        task.ClosedOn = null;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);

        _context.ActionTasks.Add(task);

        // SECTION: Audit Enqueue
        _context.ActionTaskAuditLogs.Add(Log(
            action: "BacklogItemCreated",
            userId: task.CreatedByUserId,
            role: task.CreatedByRole,
            oldValue: null,
            newValue: task.Status,
            remarks: null,
            task: task));

        // SECTION: Persistence
        await _context.SaveChangesAsync(cancellationToken);

        return task;
    }

    public async Task UpdateStatusAsync(int taskId, byte[] rowVersion, string status, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        // SECTION: Validation
        var task = await GetTaskAsync(taskId, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanUpdateTask(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to update this task.");
        }

        if (!ActionTaskStatuses.All.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid status transition.");
        }

        // SECTION: Enforce dedicated workflow actions for non-generic status changes.
        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Backlog items can only be changed through planning actions.");
        }

        if (string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use the submit for closure action to submit a task.");
        }

        if (string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use the close action to close a task.");
        }

        // SECTION: Ignore no-op updates and avoid redundant audit logs.
        if (string.Equals(task.Status, status, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // SECTION: Enforce lifecycle transitions
        if (!ActionTaskStatusWorkflow.IsAllowedTransition(task.Status, status))
        {
            throw new InvalidOperationException($"Invalid status transition from {task.Status} to {status}.");
        }

        // SECTION: Remarks validation for key transitions
        ActionTaskStatusWorkflow.ValidateRemarksForStatusTransition(task.Status, status, remarks);

        // SECTION: Concurrency token validation
        _context.Entry(task).Property(x => x.RowVersion).OriginalValue = rowVersion;

        // SECTION: State Mutation
        var oldStatus = task.Status;
        task.Status = status;
        if (string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            task.SubmittedOn = _clock.UtcNow;
        }
        if (string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            task.ClosedOn = _clock.UtcNow;
        }
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);

        // SECTION: Audit Enqueue
        _context.ActionTaskAuditLogs.Add(Log(taskId, "StatusUpdated", userId, role, oldStatus, task.Status, remarks));

        // SECTION: Persistence
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ActionTaskConcurrencyException("This task was updated by another user. Please reload the task details and try again.");
        }
    }

    public async Task SubmitTaskAsync(int taskId, byte[] rowVersion, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        // SECTION: Validation
        var task = await GetTaskAsync(taskId, cancellationToken) ?? throw new InvalidOperationException("Task not found.");

        // SECTION: Ownership and role authorization checks
        if (!string.Equals(task.AssignedToUserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the assigned user can submit this task.");
        }

        if (!_permission.CanSubmit(role))
        {
            throw new InvalidOperationException("You are not authorized to submit this task.");
        }

        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Backlog items cannot be submitted.");
        }

        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Closed tasks cannot be submitted.");
        }

        if (!ActionTaskStatusWorkflow.CanSubmitFromStatus(task.Status))
        {
            throw new InvalidOperationException("Only assigned, in-progress, or blocked tasks can be submitted.");
        }

        // SECTION: Remarks validation for submit action
        if (IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when submitting a task.");
        }

        // SECTION: Concurrency token validation
        _context.Entry(task).Property(x => x.RowVersion).OriginalValue = rowVersion;

        // SECTION: State Mutation
        var oldStatus = task.Status;
        task.Status = ActionTaskStatuses.Submitted;
        task.SubmittedOn = _clock.UtcNow;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);

        // SECTION: Audit Enqueue
        _context.ActionTaskAuditLogs.Add(Log(taskId, "Submitted", userId, role, oldStatus, task.Status, remarks));

        // SECTION: Persistence
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ActionTaskConcurrencyException("This task was updated by another user. Please reload the task details and try again.");
        }
    }

    public async Task CloseTaskAsync(int taskId, byte[] rowVersion, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        // SECTION: Submitted-task closure is retained as a compatibility wrapper over the command closure path.
        await CloseTaskDirectlyAsync(taskId, rowVersion, remarks ?? string.Empty, userId, role, cancellationToken);
    }

    public async Task CloseTaskDirectlyAsync(int taskId, byte[] rowVersion, string closureRemarks, string closedByUserId, string role, CancellationToken cancellationToken = default)
    {
        // SECTION: Validation
        var task = await GetTaskAsync(taskId, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanCloseTaskDirectly(task, role))
        {
            throw new InvalidOperationException(string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
                ? "This task is already closed."
                : "You are not authorised to close this task.");
        }

        // SECTION: Remarks validation for direct command closure
        var trimmedRemarks = closureRemarks?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedRemarks))
        {
            throw new InvalidOperationException("Closure remarks are required when closing a task.");
        }

        if (trimmedRemarks.Length > MaxClosureRemarksLength)
        {
            throw new InvalidOperationException($"Closure remarks cannot exceed {MaxClosureRemarksLength} characters.");
        }

        // SECTION: Concurrency token validation
        _context.Entry(task).Property(x => x.RowVersion).OriginalValue = rowVersion;

        // SECTION: State Mutation
        var oldStatus = task.Status;
        var closedAtUtc = _clock.UtcNow;
        task.Status = ActionTaskStatuses.Closed;
        task.ClosedOn = closedAtUtc;
        task.ClosedByUserId = closedByUserId;
        task.ClosureRemarks = trimmedRemarks;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);

        // SECTION: Audit Enqueue
        _context.ActionTaskAuditLogs.Add(Log(taskId, "TaskClosedByCommandAuthority", closedByUserId, role, oldStatus, task.Status, trimmedRemarks));

        // SECTION: Persistence
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ActionTaskConcurrencyException("This task was updated by another user. Please reload the task details and try again.");
        }
    }

    public async Task UpdateTaskDateAsync(int taskId, byte[] rowVersion, DateTime newDate, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        // SECTION: Date-change authorization and task validation
        if (!_permission.CanChangeTaskDate(role))
        {
            throw new InvalidOperationException("You are not authorized to change task dates.");
        }

        var task = await GetTaskAsync(taskId, cancellationToken) ?? throw new InvalidOperationException("Task not found.");

        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Closed tasks cannot have their dates changed.");
        }

        var normalizedNewDate = newDate.Date;
        if (normalizedNewDate < _clock.IstToday)
        {
            throw new InvalidOperationException("Task date cannot be in the past.");
        }

        if (task.DueDate.Date == normalizedNewDate)
        {
            throw new InvalidOperationException("No date change applied because the selected date is already current.");
        }

        // SECTION: Concurrency token validation
        _context.Entry(task).Property(x => x.RowVersion).OriginalValue = rowVersion;

        // SECTION: Date mutation and bucket invariant validation
        var oldDate = task.DueDate.Date;
        task.DueDate = normalizedNewDate;
        ActionTaskBucketInvariantValidator.ValidateTaskBucketInvariant(task);

        // SECTION: Audit Enqueue
        var auditAction = string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase)
            ? "TargetDateChanged"
            : "DueDateChanged";
        _context.ActionTaskAuditLogs.Add(Log(taskId, auditAction, userId, role, oldDate.ToString("yyyy-MM-dd"), normalizedNewDate.ToString("yyyy-MM-dd"), remarks));

        // SECTION: Persistence
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ActionTaskConcurrencyException("This task was updated by another user. Please reload the task details and try again.");
        }
    }

    // SECTION: Audit logging
    private ActionTaskAuditLog Log(int taskId, string action, string userId, string role, string? oldValue, string? newValue, string? remarks)
    {
        return new ActionTaskAuditLog
        {
            TaskId = taskId,
            ActionType = action,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = _clock.UtcNow,
            OldValue = oldValue,
            NewValue = newValue,
            Remarks = remarks
        };
    }

    private ActionTaskAuditLog Log(string action, string userId, string role, string? oldValue, string? newValue, string? remarks, ActionTaskItem task)
    {
        return new ActionTaskAuditLog
        {
            Task = task,
            ActionType = action,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = _clock.UtcNow,
            OldValue = oldValue,
            NewValue = newValue,
            Remarks = remarks
        };
    }

    // SECTION: Shared blank-value guard for required workflow remarks.
    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}
