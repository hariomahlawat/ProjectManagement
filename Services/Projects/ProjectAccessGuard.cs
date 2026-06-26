using System;
using System.Security.Claims;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public static class ProjectAccessGuard
{
    // All authenticated users may view project information and published media.
    public static bool CanViewProjectInformation(Project project, ClaimsPrincipal principal)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        return principal.Identity?.IsAuthenticated == true;
    }

    // Retains the existing restricted access policy for non-media project assets.
    public static bool CanViewProject(Project project, ClaimsPrincipal principal, string? userId)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (principal.IsInRole("Admin") ||
            principal.IsInRole("Project Officer") ||
            principal.IsInRole("Comdt") ||
            principal.IsInRole("MCO"))
        {
            return true;
        }

        if (principal.IsInRole("HoD") &&
            string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    // Media changes remain limited to Admin, the assigned HoD and the assigned Project Officer.
    public static bool CanManageProjectMedia(Project project, ClaimsPrincipal principal, string? userId)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (principal is null || principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (principal.IsInRole("Admin"))
        {
            return true;
        }

        if (principal.IsInRole("HoD") &&
            string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return principal.IsInRole("Project Officer") &&
               string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);
    }
}
