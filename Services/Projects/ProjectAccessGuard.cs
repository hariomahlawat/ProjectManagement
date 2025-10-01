using System;
using System.Security.Claims;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public static class ProjectAccessGuard
{
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

        if (principal.IsInRole("Admin"))
        {
            return true;
        }

        if (principal.IsInRole("Project Officer"))
        {
            return true;
        }

        if (principal.IsInRole("HoD") &&
            string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
