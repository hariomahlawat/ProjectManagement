using System;
using System.Linq;
using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ItProjects;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// Resource-level access for IT projects. Today every signed-in user may read every project,
/// but callers go through here so a later narrowing is one change. The role lists are the
/// policy lists in <see cref="Policies.ItProjects"/>, so UI and policy cannot drift apart.
/// </summary>
public static class ItProjectAccess
{
    public static bool CanView(ClaimsPrincipal user, ItProject? project = null)
        => user.Identity?.IsAuthenticated == true && (project is null || !project.IsDeleted);

    public static bool CanManage(ClaimsPrincipal user, ItProject? project = null)
        => CanView(user, project) && HasAnyRole(user, Policies.ItProjects.ManageRoles);

    public static bool CanRemark(ClaimsPrincipal user, ItProject? project = null)
        => CanView(user, project) && HasAnyRole(user, Policies.ItProjects.RemarkRoles);

    /// <summary>
    /// The role a remark is written as: the senior one the author holds among the remark roles
    /// (Comdt, then HoD, then MCO, then ITO), or null when the user holds none.
    /// </summary>
    public static string? RemarkRoleOf(ClaimsPrincipal user)
        => Policies.ItProjects.RemarkRoles.FirstOrDefault(user.IsInRole);

    private static bool HasAnyRole(ClaimsPrincipal user, string[] roles)
        => roles.Any(user.IsInRole);
}
