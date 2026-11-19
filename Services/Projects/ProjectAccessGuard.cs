using System;
using System.Security.Claims;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public static class ProjectAccessGuard
{
    // All authenticated users may view project information and published project content.
    public static bool CanViewProjectInformation(Project project, ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Identity?.IsAuthenticated == true;
    }

    // Kept as the common project-view entry point for older callers.
    public static bool CanViewProject(Project project, ClaimsPrincipal principal, string? userId)
    {
        _ = userId;
        return CanViewProjectInformation(project, principal);
    }

    public static bool CanViewProjectDocuments(Project project, ClaimsPrincipal principal)
        => CanViewProjectInformation(project, principal);

    public static bool CanViewProjectMedia(Project project, ClaimsPrincipal principal)
        => CanViewProjectInformation(project, principal);

    // Project media changes are available to Admin, every HoD, and the assigned Project
    // Officer, while the project is not in the trash. A trashed project's photos and videos
    // stay viewable but are read-only until the project is restored; the media library
    // already excludes trashed projects, so anything added there would never appear.
    public static bool CanManageProjectMedia(Project project, ClaimsPrincipal principal, string? userId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return !project.IsDeleted && CanManageProjectContent(project, principal, userId);
    }

    public static bool CanManageProjectDocuments(Project project, ClaimsPrincipal principal, string? userId)
        => CanManageProjectContent(project, principal, userId);

    // Project changes are available to Admin, every HoD, and the assigned Project Officer.
    private static bool CanManageProjectContent(Project project, ClaimsPrincipal principal, string? userId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Identity?.IsAuthenticated == true
            && CanManageProjectContent(
                project.LeadPoUserId,
                userId,
                principal.IsInRole("Admin"),
                principal.IsInRole("HoD"),
                principal.IsInRole("Project Officer"));
    }

    // The same rule from plain values, for code that knows the person's roles but has no signed-in
    // principal (the briefing deck Studio says where a gap can be fixed by whom).
    public static bool CanManageProjectContent(
        string? projectLeadPoUserId,
        string? userId,
        bool isAdmin,
        bool isHod,
        bool isProjectOfficer)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (isAdmin || isHod)
        {
            return true;
        }

        return isProjectOfficer &&
               string.Equals(projectLeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);
    }
}
