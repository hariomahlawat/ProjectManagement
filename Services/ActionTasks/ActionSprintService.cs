using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public class ActionSprintService
{
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

    // SECTION: Sprint mutation APIs
    public async Task<ActionSprint> CreateSprintAsync(ActionSprint sprint, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanManageSprint(role);
        _workflow.ValidateDateRange(sprint.StartDate, sprint.EndDate);

        sprint.Status = ActionSprintStatus.Planned;
        sprint.CreatedByUserId = userId;
        sprint.CreatedByRole = role;
        sprint.CreatedAtUtc = DateTime.UtcNow;
        sprint.StartDate = sprint.StartDate.Date;
        sprint.EndDate = sprint.EndDate.Date;

        _context.ActionSprints.Add(sprint);
        await _context.SaveChangesAsync(cancellationToken);
        return sprint;
    }

    public async Task<ActionSprint> UpdateSprintAsync(int sprintId, string name, string? goal, DateTime startDate, DateTime endDate, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanManageSprint(role);
        _workflow.ValidateDateRange(startDate, endDate);

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanUpdate(sprint);

        sprint.Name = name;
        sprint.Goal = goal;
        sprint.StartDate = startDate.Date;
        sprint.EndDate = endDate.Date;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return sprint;
    }

    public async Task<ActionSprint> ActivateSprintAsync(int sprintId, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanManageSprint(role);

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanActivate(sprint);
        _workflow.ValidateDateRange(sprint.StartDate, sprint.EndDate);

        var activeSprintExists = await _context.ActionSprints
            .AnyAsync(x => x.Id != sprintId && !x.IsDeleted && x.Status == ActionSprintStatus.Active, cancellationToken);
        if (activeSprintExists)
        {
            throw new InvalidOperationException("Only one active sprint is allowed.");
        }

        sprint.Status = ActionSprintStatus.Active;
        sprint.ActivatedAtUtc = DateTime.UtcNow;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = sprint.ActivatedAtUtc;

        await _context.SaveChangesAsync(cancellationToken);
        return sprint;
    }

    public async Task<ActionSprint> CloseSprintAsync(int sprintId, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanManageSprint(role);

        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanClose(sprint);

        sprint.Status = ActionSprintStatus.Closed;
        sprint.ClosedAtUtc = DateTime.UtcNow;
        sprint.UpdatedByUserId = userId;
        sprint.UpdatedByRole = role;
        sprint.UpdatedAtUtc = sprint.ClosedAtUtc;

        await _context.SaveChangesAsync(cancellationToken);
        return sprint;
    }

    // SECTION: Sprint task assignment APIs
    public async Task<ActionTaskItem> AssignTaskToSprintAsync(int taskId, int sprintId, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanMoveTasksInSprint(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        var sprint = await GetSprintForUpdateAsync(sprintId, cancellationToken);
        _workflow.EnsureCanAcceptTask(sprint);

        var oldValue = task.SprintId?.ToString();
        task.SprintId = sprint.Id;
        AddTaskSprintAudit(task.Id, "TaskAssignedToSprint", userId, role, oldValue, sprint.Id.ToString(), $"Assigned to sprint: {sprint.Name}");

        await _context.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<ActionTaskItem> MoveTaskToBacklogAsync(int taskId, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureCanMoveTasksInSprint(role);

        var task = await GetTaskForUpdateAsync(taskId, cancellationToken);
        if (task.SprintId.HasValue)
        {
            var currentSprint = await GetSprintForUpdateAsync(task.SprintId.Value, cancellationToken);
            _workflow.EnsureCanUpdate(currentSprint);
        }

        var oldValue = task.SprintId?.ToString();
        task.SprintId = null;
        AddTaskSprintAudit(task.Id, "TaskMovedToBacklog", userId, role, oldValue, null, "Moved to backlog.");

        await _context.SaveChangesAsync(cancellationToken);
        return task;
    }

    // SECTION: Authorization helpers
    private void EnsureCanManageSprint(string role)
    {
        if (!_permission.CanManageSprints(role))
        {
            throw new InvalidOperationException("You are not authorized to manage sprints.");
        }
    }

    private void EnsureCanMoveTasksInSprint(string role)
    {
        if (!_permission.CanMoveTasksInSprint(role))
        {
            throw new InvalidOperationException("You are not authorized to move tasks into or out of sprints.");
        }
    }

    // SECTION: Entity loading helpers
    private async Task<ActionSprint> GetSprintForUpdateAsync(int sprintId, CancellationToken cancellationToken)
        => await _context.ActionSprints.FirstOrDefaultAsync(x => x.Id == sprintId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("Sprint not found.");

    private async Task<ActionTaskItem> GetTaskForUpdateAsync(int taskId, CancellationToken cancellationToken)
        => await _context.ActionTasks.FirstOrDefaultAsync(x => x.Id == taskId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("Task not found.");

    // SECTION: Audit helpers
    private void AddTaskSprintAudit(int taskId, string actionType, string userId, string role, string? oldValue, string? newValue, string remarks)
    {
        _context.ActionTaskAuditLogs.Add(new ActionTaskAuditLog
        {
            TaskId = taskId,
            ActionType = actionType,
            PerformedByUserId = userId,
            PerformedByRole = role,
            PerformedAt = DateTime.UtcNow,
            OldValue = oldValue,
            NewValue = newValue,
            Remarks = remarks
        });
    }
}
