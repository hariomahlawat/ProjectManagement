using System;
using System.Linq;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public class ActionTaskPermissionService
{
    private static readonly string[] ComdtAssignmentTargets =
    {
        RoleNames.HoD,
        RoleNames.ProjectOfficer,
        RoleNames.Mco,
        RoleNames.Ta,
        RoleNames.Ito
    };

    private static readonly string[] HoDAssignmentTargets =
    {
        RoleNames.ProjectOfficer,
        RoleNames.Mco,
        RoleNames.Ta,
        RoleNames.Ito
    };

    // SECTION: Role capability checks
    public bool CanCreate(string role)
        => IsPlanningAuthority(role);

    public bool CanAssign(string assignerRole, string assigneeRole)
    {
        if (string.Equals(assignerRole, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase))
        {
            return ComdtAssignmentTargets.Contains(assigneeRole, StringComparer.OrdinalIgnoreCase);
        }

        if (string.Equals(assignerRole, RoleNames.HoD, StringComparison.OrdinalIgnoreCase))
        {
            return HoDAssignmentTargets.Contains(assigneeRole, StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }

    public bool CanClose(string role)
        => IsPlanningAuthority(role);

    // SECTION: Direct command closure is limited to planning authorities and non-closed tasks.
    public bool CanCloseTaskDirectly(ActionTaskItem? task, string role)
        => task is not null
            && !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && IsPlanningAuthority(role);

    // SECTION: Returning a closure submission is a command decision, not an assignee status edit.
    public bool CanReturnTaskForAction(ActionTaskItem? task, string role)
        => task is not null
            && string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && IsPlanningAuthority(role);

    public bool CanChangeTaskDate(string role)
        => IsPlanningAuthority(role);

    // SECTION: Operational task-management permissions
    // Comdt/HoD are the task planning authorities. Admin retains system/audit responsibilities
    // but does not implicitly receive command task mutation rights.
    public bool CanEditTaskDetails(string role)
        => IsPlanningAuthority(role);

    public bool CanReassignTask(ActionTaskItem? task, string role)
        => task is not null
            && !string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && IsPlanningAuthority(role);

    public bool CanChangeTaskPriority(ActionTaskItem? task, string role)
        => task is not null
            && !string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && IsPlanningAuthority(role);

    public bool CanMutateTaskRemark(ActionTaskUpdate? update, string role, string currentUserId, DateTime nowUtc)
    {
        if (update is null || update.IsDeleted
            || string.Equals(update.UpdateType, ActionTaskUpdateTypes.Progress, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(update.UpdateType, ActionTaskUpdateTypes.Conference, StringComparison.OrdinalIgnoreCase))
        {
            return CanAddConferenceUpdate(role);
        }

        if (IsPlanningAuthority(role))
        {
            return true;
        }

        if (!string.Equals(update.CreatedByUserId, currentUserId, StringComparison.Ordinal))
        {
            return false;
        }

        var age = nowUtc - update.CreatedAtUtc;
        return age >= TimeSpan.Zero && age <= TimeSpan.FromHours(3);
    }

    // SECTION: Sprint lifecycle permission checks
    public bool CanCreateSprint(string role)
        => IsPlanningAuthority(role);

    public bool CanEditSprint(string role)
        => IsPlanningAuthority(role);

    public bool CanActivateSprint(string role)
        => IsPlanningAuthority(role);

    public bool CanCloseSprint(string role)
        => IsPlanningAuthority(role);

    public bool CanAssignTaskToSprint(string role)
        => IsPlanningAuthority(role);

    public bool CanMoveTaskToBacklog(string role)
        => IsPlanningAuthority(role);

    // SECTION: Backward-compatible broad sprint capability checks
    public bool CanManageSprints(string role)
        => IsPlanningAuthority(role);

    public bool CanMoveTasksInSprint(string role)
        => IsPlanningAuthority(role);

    public bool CanViewAll(string role)
        => IsPlanningAuthority(role);

    public bool CanViewLogs(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    // SECTION: Thread Read Authorization
    public bool CanViewTaskThread(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    // SECTION: Update Write Authorization
    public bool CanAddTaskUpdate(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    public bool CanAddConferenceUpdate(string role)
        => Policies.ConferenceRemarks.ManageAllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    // SECTION: Attachment Write Authorization
    public bool CanUploadTaskAttachment(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    public bool CanUpdateTask(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    public bool CanSubmit(string role)
        => role is RoleNames.Comdt or RoleNames.HoD or RoleNames.ProjectOfficer or RoleNames.Mco or RoleNames.Ta or RoleNames.Ito;

    // SECTION: Shared role helpers
    private static bool IsPlanningAuthority(string role)
        => string.Equals(role, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, RoleNames.HoD, StringComparison.OrdinalIgnoreCase);
}
