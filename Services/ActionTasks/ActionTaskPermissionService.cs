using System;
using System.Linq;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.ActionTasks;

public class ActionTaskPermissionService
{
    private static readonly string[] AssignmentTargets =
    {
        RoleNames.HoD,
        RoleNames.ProjectOfficer,
        RoleNames.Mco,
        RoleNames.Ta
    };

    // SECTION: Role capability checks
    public bool CanCreate(string role)
        => role == RoleNames.Comdt || role == RoleNames.HoD;

    public bool CanAssign(string assignerRole, string assigneeRole)
    {
        if (assignerRole != RoleNames.Comdt && assignerRole != RoleNames.HoD)
        {
            return false;
        }

        return AssignmentTargets.Contains(assigneeRole, StringComparer.OrdinalIgnoreCase);
    }

    public bool CanClose(string role)
        => role == RoleNames.Comdt || role == RoleNames.HoD;

    public bool CanViewAll(string role)
        => role == RoleNames.Comdt || role == RoleNames.HoD;

    public bool CanViewLogs(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    public bool CanUpdateTask(string role, string currentUserId, string ownerUserId)
        => CanViewAll(role) || string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal);

    public bool CanSubmit(string role)
        => role is RoleNames.Comdt or RoleNames.HoD or RoleNames.ProjectOfficer or RoleNames.Mco or RoleNames.Ta;
}
