using System;
using System.Linq;
using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// Resource-level access for the modules on the IT projects engine (IT, MOLTI and SRE projects).
/// Every signed-in user may read every project. The role lists are the policy lists in
/// <see cref="Policies"/>, so UI and policy cannot drift apart. In the modules that assign a
/// Project Officer (docs/ux/molti-sre-projects.md) the officer assigned to a project also works
/// on it and remarks on it: that is decided here, per project, not by a role list.
/// </summary>
public static class ItProjectAccess
{
    public static bool CanView(ClaimsPrincipal user, ItProject? project = null)
        => user.Identity?.IsAuthenticated == true && (project is null || !project.IsDeleted);

    /// <summary>
    /// Whether the user holds a manage role of the project's module (IT projects when no project
    /// is given): creating and deleting projects and, where the module assigns one, choosing the
    /// officer.
    /// </summary>
    public static bool CanManage(ClaimsPrincipal user, ItProject? project = null)
        => CanView(user, project) && HasAnyRole(user, ManageRolesOf(project?.Module ?? ItProjectModules.It));

    /// <summary>Whether the user holds a manage role of that module.</summary>
    public static bool CanManage(ClaimsPrincipal user, string module)
        => CanView(user) && HasAnyRole(user, ManageRolesOf(module));

    /// <summary>
    /// Whether the user may change this project: its details, stages, years and attachments, and
    /// drop or reinstate it. The manage roles, and the Project Officer assigned to it.
    /// </summary>
    public static bool CanWorkOn(ClaimsPrincipal user, ItProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return CanManage(user, project) || IsAssignedOfficer(user, project);
    }

    /// <summary>
    /// Whether the user may write remarks: a remark role of the module (IT projects when no
    /// project is given), or the Project Officer assigned to the project.
    /// </summary>
    public static bool CanRemark(ClaimsPrincipal user, ItProject? project = null)
        => RemarkRoleOf(user, project) is not null;

    /// <summary>
    /// The role a remark is written as: the senior one the author holds among the module's remark
    /// roles (Comdt, then HoD, then MCO, then ITO for IT projects), else Project Officer for the
    /// officer assigned to the project, or null when the user may not remark.
    /// </summary>
    public static string? RemarkRoleOf(ClaimsPrincipal user, ItProject? project = null)
    {
        if (!CanView(user, project))
        {
            return null;
        }

        var role = RemarkRolesOf(project?.Module ?? ItProjectModules.It).FirstOrDefault(user.IsInRole);
        if (role is not null)
        {
            return role;
        }

        return project is not null && IsAssignedOfficer(user, project) ? RoleNames.ProjectOfficer : null;
    }

    /// <summary>
    /// Whether the user is the Project Officer assigned to this live project. Only in a module
    /// that assigns one, and only while the user still holds the Project Officer role, so taking
    /// the role away ends the rights even before the project is reassigned.
    /// </summary>
    public static bool IsAssignedOfficer(ClaimsPrincipal user, ItProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!CanView(user, project) ||
            !ItProjectModules.IsKnown(project.Module) ||
            !ItProjectModules.HasAssignedOfficer(project.Module) ||
            string.IsNullOrEmpty(project.ProjectOfficerUserId))
        {
            return false;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.Equals(userId, project.ProjectOfficerUserId, StringComparison.Ordinal)
            && user.IsInRole(RoleNames.ProjectOfficer);
    }

    /// <summary>The manage roles of a module; none for an unknown module.</summary>
    public static string[] ManageRolesOf(string module) => module switch
    {
        ItProjectModules.It => Policies.ItProjects.ManageRoles,
        ItProjectModules.Molti => Policies.MoltiProjects.ManageRoles,
        ItProjectModules.Sre => Policies.SreProjects.ManageRoles,
        _ => Array.Empty<string>()
    };

    /// <summary>The remark roles of a module in seniority order; none for an unknown module.</summary>
    public static string[] RemarkRolesOf(string module) => module switch
    {
        ItProjectModules.It => Policies.ItProjects.RemarkRoles,
        ItProjectModules.Molti => Policies.MoltiProjects.RemarkRoles,
        ItProjectModules.Sre => Policies.SreProjects.RemarkRoles,
        _ => Array.Empty<string>()
    };

    private static bool HasAnyRole(ClaimsPrincipal user, string[] roles)
        => roles.Any(user.IsInRole);
}
