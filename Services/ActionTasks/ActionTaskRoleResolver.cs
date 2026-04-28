using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.ActionTasks;

public static class ActionTaskRoleResolver
{
    private static readonly string[] Precedence =
    {
        RoleNames.Comdt,
        RoleNames.HoD,
        RoleNames.ProjectOfficer,
        RoleNames.Mco,
        RoleNames.Ta
    };

    // SECTION: Resolve highest-precedence action-tracker role
    public static string? Resolve(ClaimsPrincipal user)
    {
        foreach (var role in Precedence)
        {
            if (user.IsInRole(role))
            {
                return role;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> AllowedAssignmentRoles()
        => new[] { RoleNames.HoD, RoleNames.ProjectOfficer, RoleNames.Mco, RoleNames.Ta };

    // SECTION: Resolve highest-precedence tracker role from user role list
    public static string? ResolveFromRoles(IEnumerable<string> roles)
    {
        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        return Precedence.FirstOrDefault(roleSet.Contains);
    }

    // SECTION: Resolve highest-precedence assignable tracker role from user role list
    public static string? ResolveAssignableRoleFromRoles(IEnumerable<string> roles)
    {
        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        return AllowedAssignmentRoles().FirstOrDefault(roleSet.Contains);
    }
}
