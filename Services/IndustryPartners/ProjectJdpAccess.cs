using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Services.IndustryPartners;

// SECTION: Who may add or remove a project's Joint Development Partners
// This is the single rule for JDP link changes, whichever page the change comes from
// (project Overview or the Industry Partners directory): Admin, HoD and Comdt may
// manage any project, and a Project Officer may manage the projects they lead.
public static class ProjectJdpAccess
{
    public static readonly string[] ManageAnyProjectRoles =
    {
        RoleNames.Admin,
        RoleNames.HoD,
        RoleNames.Comdt
    };

    public static bool CanManageAnyProject(ClaimsPrincipal user) =>
        ManageAnyProjectRoles.Any(user.IsInRole);

    /// <summary>Narrows <paramref name="projects"/> to those whose JDPs the user may change.</summary>
    public static IQueryable<Project> WhereUserCanManage(IQueryable<Project> projects, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(user);

        if (CanManageAnyProject(user))
        {
            return projects;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || !user.IsInRole(RoleNames.ProjectOfficer))
        {
            return projects.Where(_ => false);
        }

        return projects.Where(project => project.LeadPoUserId == userId);
    }
}
