using System;
using System.Security.Claims;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

/// <summary>
/// Single source of truth for Activity module permissions. UI and service-layer
/// authorization must use the same rules so affordances never disagree with commands.
/// </summary>
public static class ActivityAuthorizationPolicy
{
    public static bool IsManager(ClaimsPrincipal principal)
        => IsInAnyRole(principal, ActivityRoleLists.ManagerRoles);

    public static bool IsDeleteApprover(ClaimsPrincipal principal)
        => IsInAnyRole(principal, ActivityRoleLists.DeleteApproverRoles);

    public static bool CanCreate(ClaimsPrincipal principal)
        => IsManager(principal);

    public static bool CanManage(Activity activity, ClaimsPrincipal principal, string? userId)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return CanManage(activity.CreatedByUserId, principal, userId);
    }

    public static bool CanManage(string? createdByUserId, ClaimsPrincipal principal, string? userId)
        => IsManager(principal)
           || (!string.IsNullOrWhiteSpace(userId)
               && string.Equals(createdByUserId, userId, StringComparison.OrdinalIgnoreCase));

    public static bool CanManageAttachment(
        Activity activity,
        ActivityAttachment attachment,
        ClaimsPrincipal principal,
        string? userId)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(attachment);

        return IsManager(principal)
               || (!string.IsNullOrWhiteSpace(userId)
                   && (string.Equals(activity.CreatedByUserId, userId, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(attachment.UploadedByUserId, userId, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool CanRequestDelete(ClaimsPrincipal principal)
        => IsManager(principal);

    public static bool CanDelete(ClaimsPrincipal principal)
        => IsDeleteApprover(principal);

    private static bool IsInAnyRole(ClaimsPrincipal principal, string[] roles)
    {
        ArgumentNullException.ThrowIfNull(principal);

        foreach (var role in roles)
        {
            if (principal.IsInRole(role))
            {
                return true;
            }
        }

        return false;
    }
}
