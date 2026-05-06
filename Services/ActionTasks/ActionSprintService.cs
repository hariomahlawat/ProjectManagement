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

    public ActionSprintService(
        ApplicationDbContext context,
        ActionTaskPermissionService permission,
        ActionSprintWorkflowPolicy workflow)
    {
        _context = context;
        _permission = permission;
        _workflow = workflow;
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

        var performedAt = DateTime.UtcNow;
        sprint.Status = ActionSprintStatus.Planned;
        sprint.RowVersion = NewSprintRowVersion();
        sprint.CreatedByUserId = userId;
        sprint.CreatedByRole = role;
        sprint.CreatedAtUtc = performedAt;
        sprint.StartDate = sprint.StartDate.Date;
        sprint.EndDate = sprint.EndDate.Date;

        _context.ActionSprints.Add(sprint);
        await _context.SaveChangesAsync(cancellationToken);

        AddSprintAudit(sprint.Id, "SprintCreated", userId, role, performedAt, null, DescribeSprint(sprint), $"Created Planning Window: {sprint.Name}");
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

        var performedAt = DateTime.UtcNow;
        var oldValue = DescribeSprint(sprint);

        sprint.Name = name;
        sprint.Goal = goal;
        sprint.StartDate = startDate.Date;
        sprint.EndDate = endDate.Date;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        AddSprintAudit(sprint.Id, "SprintUpdated", userId, role, performedAt, oldValue, DescribeSprint(sprint), $"Updated Planning Window: {sprint.Name}");
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
            throw new InvalidOperationException("Only one active Planning Window is allowed.");
        }

        var performedAt = DateTime.UtcNow;
        var oldStatus = sprint.Status.ToString();

        sprint.Status = ActionSprintStatus.Active;
        sprint.ActivatedAtUtc = performedAt;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        AddSprintAudit(sprint.Id, "SprintActivated", userId, role, performedAt, oldStatus, sprint.Status.ToString(), $"Activated Planning Window: {sprint.Name}");
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
            throw new InvalidOperationException("Planning Window contains unfinished tasks. Use the closure review to move unfinished tasks to the next Planning Window or backlog before closing.");
        }

        var performedAt = DateTime.UtcNow;
        var oldStatus = sprint.Status.ToString();

        sprint.Status = ActionSprintStatus.Closed;
        sprint.ClosedAtUtc = performedAt;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        AddSprintAudit(sprint.Id, "SprintClosed", userId, role, performedAt, oldStatus, sprint.Status.ToString(), $"Closed Planning Window: {sprint.Name}");
        await SaveSprintChangesAsync(cancellationToken);
        return sprint;
    }

    public async Task<ActionSprint> CloseSprintWithDispositionAsync(int sprintId, byte[] rowVersion, IReadOnlyCollection<int> carryForwardTaskIds, int? targetSprintId, IReadOnlyCollection<int> backlogTaskIds, string remarks, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanCloseSprint(role);
        EnsureSprintRowVersionSupplied(rowVersion);
        if (string.IsNullOrWhiteSpace(remarks))
        {
            throw new InvalidOperationException("Closure remarks are required when closing a Planning Window through closure review.");
        }

        var carryIds = (carryForwardTaskIds ?? Array.Empty<int>()).Distinct().ToList();
        var backIds = (backlogTaskIds ?? Array.Empty<int>()).Distinct().ToList();
        var duplicateDispositionIds = carryIds.Intersect(backIds).ToList();
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
                throw new InvalidOperationException("Select a target Planning Window for carry-forward tasks.");
            }

            targetSprint = await GetSprintForUpdateAsync(targetSprintId.Value, cancellationToken);
            _workflow.EnsureCanCarryForward(sprint, targetSprint);
        }

        var unfinishedTasks = await _context.ActionTasks
            .Where(t => !t.IsDeleted && t.SprintId == sprint.Id && t.Status != ActionTaskStatuses.Closed)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var dispositionedIds = carryIds.Concat(backIds).Distinct().ToHashSet();
        var missingDispositionIds = unfinishedTasks.Select(t => t.Id).Where(id => !dispositionedIds.Contains(id)).ToList();
        if (missingDispositionIds.Count > 0)
        {
            throw new InvalidOperationException("All unfinished tasks must be moved to the next Planning Window or backlog before the Planning Window can close.");
        }

        var invalidDispositionIds = dispositionedIds.Except(unfinishedTasks.Select(t => t.Id)).ToList();
        if (invalidDispositionIds.Count > 0)
        {
            throw new InvalidOperationException("Closure disposition includes tasks that are not unfinished tasks in this Planning Window.");
        }

        var performedAt = DateTime.UtcNow;
        foreach (var task in unfinishedTasks.Where(t => carryIds.Contains(t.Id)))
        {
            var oldValue = task.SprintId?.ToString();
            task.SprintId = targetSprint!.Id;
            AddTaskSprintAudit(task.Id, "TaskCarriedForward", userId, role, oldValue, targetSprint.Id.ToString(), $"Carried forward from Planning Window {sprint.Name} to {targetSprint.Name}. Closure remarks: {remarks.Trim()}", performedAt);
        }

        foreach (var task in unfinishedTasks.Where(t => backIds.Contains(t.Id)))
        {
            var oldValue = task.SprintId?.ToString();
            task.SprintId = null;
            AddTaskSprintAudit(task.Id, "TaskRemovedFromSprint", userId, role, oldValue, null, $"Moved from Planning Window {sprint.Name} to backlog during closure. Closure remarks: {remarks.Trim()}", performedAt);
        }

        var oldStatus = sprint.Status.ToString();
        sprint.Status = ActionSprintStatus.Closed;
        sprint.ClosedAtUtc = performedAt;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = performedAt;
        sprint.RowVersion = NewSprintRowVersion();

        var detail = $"Closed Planning Window: {sprint.Name}. Carried forward: {carryIds.Count}. Moved to backlog: {backIds.Count}. Remarks: {remarks.Trim()}";
        AddSprintAudit(sprint.Id, "SprintClosed", userId, role, performedAt, oldStatus, sprint.Status.ToString(), detail);
        await SaveSprintChangesAsync(cancellationToken);
        return sprint;
    }

    // SECTION: Sprint task assignment APIs
    public async Task<ActionTaskItem> AssignTaskToSprintAsync(int taskId, int sprintId, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanAssignTaskToSprint(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        EnsureTaskIsNotClosed(task, "Closed tasks cannot be assigned to a Planning Window.");

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanAcceptTask(sprint);

        var oldValue = task.SprintId?.ToString();
        task.SprintId = sprint.Id;
        AddTaskSprintAudit(task.Id, "TaskAssignedToSprint", userId, role, oldValue, sprint.Id.ToString(), $"Assigned to Planning Window: {sprint.Name}");

        await _context.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<ActionTaskItem> MoveTaskToBacklogAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanMoveTaskToBacklog(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        EnsureTaskIsNotClosed(task, "Closed tasks cannot be moved between a Planning Window and backlog.");

        if (task.SprintId.HasValue)
        {
            var currentSprint = await GetSprintForUpdateAsync(task.SprintId.Value, cancellationToken);
            _workflow.EnsureCanUpdate(currentSprint);
        }

        var oldValue = task.SprintId?.ToString();
        task.SprintId = null;
        AddTaskSprintAudit(task.Id, "TaskMovedToBacklog", userId, role, oldValue, null, "Removed from Planning Window and moved to backlog.");

        await _context.SaveChangesAsync(cancellationToken);
        return task;
    }

    // SECTION: Authorization helpers
    private async Task<int> CountUnfinishedSprintTasksAsync(int sprintId, CancellationToken cancellationToken)
        => await _context.ActionTasks.CountAsync(t => !t.IsDeleted && t.SprintId == sprintId && t.Status != ActionTaskStatuses.Closed, cancellationToken);

    private void EnsureCanCreateSprint(string role)
    {
        if (!_permission.CanCreateSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to create Planning Windows.");
        }
    }

    private void EnsureCanEditSprint(string role)
    {
        if (!_permission.CanEditSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to update Planning Windows.");
        }
    }

    private void EnsureCanActivateSprint(string role)
    {
        if (!_permission.CanActivateSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to activate Planning Windows.");
        }
    }

    private void EnsureCanCloseSprint(string role)
    {
        if (!_permission.CanCloseSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to close Planning Windows.");
        }
    }

    private void EnsureCanAssignTaskToSprint(string role)
    {
        if (!_permission.CanAssignTaskToSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to assign tasks to Planning Windows.");
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
            ?? throw new InvalidOperationException("Planning Window not found.");

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
            throw new InvalidOperationException("Planning Window row version is required for this operation.");
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
            throw new ActionTaskConcurrencyException("This Planning Window was updated by another user. Please reload the Planning Window details and try again.");
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
            PerformedAt = performedAt ?? DateTime.UtcNow,
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
