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
    private readonly ApplicationDbContext _context;
    private readonly ActionTaskPermissionService _permission;

    public ActionTaskService(ApplicationDbContext context, ActionTaskPermissionService permission)
    {
        _context = context;
        _permission = permission;
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

    // SECTION: Task mutation APIs
    public async Task<ActionTaskItem> CreateTaskAsync(ActionTaskItem task, CancellationToken cancellationToken = default)
    {
        task.AssignedOn = DateTime.UtcNow;
        task.Status = ActionTaskStatuses.Assigned;

        _context.ActionTasks.Add(task);
        await _context.SaveChangesAsync(cancellationToken);

        await Log(task.Id, "TaskCreated", task.CreatedByUserId, task.CreatedByRole, null, task.Status, null, cancellationToken);

        return task;
    }

    public async Task UpdateStatusAsync(int taskId, string status, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskAsync(taskId, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!_permission.CanUpdateTask(role, userId, task.AssignedToUserId))
        {
            throw new InvalidOperationException("You are not authorized to update this task.");
        }

        if (!ActionTaskStatuses.All.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid status transition.");
        }

        // SECTION: Enforce dedicated close action
        if (string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use the close action to close a task.");
        }

        // SECTION: Enforce lifecycle transitions
        if (!IsAllowedTransition(task.Status, status))
        {
            throw new InvalidOperationException($"Invalid status transition from {task.Status} to {status}.");
        }

        // SECTION: Remarks validation for key transitions
        ValidateRemarksForStatusTransition(task.Status, status, remarks);

        var oldStatus = task.Status;
        task.Status = status;
        if (string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            task.SubmittedOn = DateTime.UtcNow;
        }
        if (string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            task.ClosedOn = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await Log(taskId, "StatusUpdated", userId, role, oldStatus, task.Status, remarks, cancellationToken);
    }

    public async Task SubmitTaskAsync(int taskId, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
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

        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Closed tasks cannot be submitted.");
        }

        // SECTION: Remarks validation for submit action
        if (IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when submitting a task.");
        }

        var oldStatus = task.Status;
        task.Status = ActionTaskStatuses.Submitted;
        task.SubmittedOn = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await Log(taskId, "Submitted", userId, role, oldStatus, task.Status, remarks, cancellationToken);
    }

    public async Task CloseTaskAsync(int taskId, string userId, string role, string? remarks = null, CancellationToken cancellationToken = default)
    {
        if (!_permission.CanClose(role))
        {
            throw new InvalidOperationException("You are not authorized to close this task.");
        }

        var task = await GetTaskAsync(taskId, cancellationToken) ?? throw new InvalidOperationException("Task not found.");
        if (!string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only submitted tasks can be closed.");
        }

        // SECTION: Remarks validation for close action
        if (IsBlank(remarks))
        {
            throw new InvalidOperationException("Closure remarks are required.");
        }

        var oldStatus = task.Status;
        task.Status = ActionTaskStatuses.Closed;
        task.ClosedOn = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await Log(taskId, "Closed", userId, role, oldStatus, task.Status, remarks, cancellationToken);
    }

    // SECTION: Audit logging
    private async Task Log(int taskId, string action, string userId, string role, string? oldValue, string? newValue, string? remarks, CancellationToken cancellationToken)
    {
        _context.ActionTaskAuditLogs.Add(new ActionTaskAuditLog
        {
            TaskId = taskId,
            ActionType = action,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = DateTime.UtcNow,
            OldValue = oldValue,
            NewValue = newValue,
            Remarks = remarks
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    // SECTION: Remarks rule helpers
    private static void ValidateRemarksForStatusTransition(string currentStatus, string nextStatus, string? remarks)
    {
        if (string.Equals(nextStatus, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase) && IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when marking a task as blocked.");
        }

        if (string.Equals(nextStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase) && IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when submitting a task.");
        }

        if (string.Equals(currentStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(nextStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when returning a submitted task for further action.");
        }
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    // SECTION: Workflow transition guard
    private static bool IsAllowedTransition(string currentStatus, string nextStatus)
    {
        if (string.Equals(currentStatus, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(nextStatus, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return currentStatus switch
        {
            ActionTaskStatuses.Assigned => nextStatus is ActionTaskStatuses.InProgress or ActionTaskStatuses.Blocked or ActionTaskStatuses.Submitted,
            ActionTaskStatuses.InProgress => nextStatus is ActionTaskStatuses.Blocked or ActionTaskStatuses.Submitted,
            ActionTaskStatuses.Blocked => nextStatus is ActionTaskStatuses.InProgress or ActionTaskStatuses.Submitted,
            ActionTaskStatuses.Submitted => nextStatus is ActionTaskStatuses.InProgress or ActionTaskStatuses.Blocked,
            _ => false
        };
    }
}
