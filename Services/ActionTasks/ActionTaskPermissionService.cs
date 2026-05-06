using System;
using System.Linq;
using ProjectManagement.Configuration;

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
        => role == RoleNames.Comdt || role == RoleNames.HoD;

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
        => role == RoleNames.Comdt || role == RoleNames.HoD;

    public bool CanManageSprints(string role)
        => role == RoleNames.Comdt || role == RoleNames.HoD;

    public bool CanMoveTasksInSprint(string role)
        => CanManageSprints(role);

    public bool CanViewAll(string role)
        => role == RoleNames.Comdt || role == RoleNames.HoD;

    public bool CanViewLogs(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    // SECTION: Thread Read Authorization
    public bool CanViewTaskThread(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    // SECTION: Update Write Authorization
    public bool CanAddTaskUpdate(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    // SECTION: Attachment Write Authorization
    public bool CanUploadTaskAttachment(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    public bool CanUpdateTask(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    public bool CanSubmit(string role)
        => role is RoleNames.Comdt or RoleNames.HoD or RoleNames.ProjectOfficer or RoleNames.Mco or RoleNames.Ta or RoleNames.Ito;
}
