using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaPermissionService
{
    // SECTION: Idea-level permissions
    public bool CanCreateIdea(ClaimsPrincipal user) => IsPrivileged(user);

    public bool CanViewIdea(ClaimsPrincipal user, ProjectIdea idea)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (IsPrivileged(user))
        {
            return true;
        }

        var userId = GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        return string.Equals(idea.AssignedProjectOfficerUserId, userId, StringComparison.Ordinal)
            || string.Equals(idea.AssignedHodUserId, userId, StringComparison.Ordinal)
            || string.Equals(idea.CreatedByUserId, userId, StringComparison.Ordinal);
    }

    public bool CanEditIdeaCore(ClaimsPrincipal user, ProjectIdea idea) => !IsArchived(idea) && IsPrivileged(user);

    public bool CanEditDescription(ClaimsPrincipal user, ProjectIdea idea)
    {
        return !IsArchived(idea)
            && (IsPrivileged(user) || IsAssignedProjectOfficer(user, idea));
    }

    public bool CanEditIdea(ClaimsPrincipal user, ProjectIdea idea) => CanEditDescription(user, idea);
    public bool CanArchiveIdea(ClaimsPrincipal user) => IsPrivileged(user);
    public bool CanRestoreIdea(ClaimsPrincipal user) => IsPrivileged(user);

    // SECTION: Collaboration permissions
    public bool CanAddComment(ClaimsPrincipal user, ProjectIdea idea) => !IsArchived(idea) && CanViewIdea(user, idea);
    public bool CanAddNote(ClaimsPrincipal user, ProjectIdea idea) => !IsArchived(idea) && (IsPrivileged(user) || IsAssignedProjectOfficer(user, idea));
    public bool CanUploadDocument(ClaimsPrincipal user, ProjectIdea idea) => !IsArchived(idea) && (IsPrivileged(user) || IsAssignedProjectOfficer(user, idea));

    public bool CanDeleteDocument(ClaimsPrincipal user, ProjectIdeaDocument document, ProjectIdea idea)
    {
        return !IsArchived(idea)
            && CanViewIdea(user, idea)
            && (IsPrivileged(user)
                || IsAssignedProjectOfficer(user, idea)
                || string.Equals(GetUserId(user), document.UploadedByUserId, StringComparison.Ordinal));
    }

    // SECTION: Helpers
    private static bool IsPrivileged(ClaimsPrincipal user)
    {
        return user?.Identity?.IsAuthenticated == true
            && (user.IsInRole(RoleNames.Admin)
                || user.IsInRole(RoleNames.HoD)
                || user.IsInRole(RoleNames.Comdt));
    }

    private static bool IsAssignedProjectOfficer(ClaimsPrincipal user, ProjectIdea idea)
    {
        return user?.Identity?.IsAuthenticated == true
            && string.Equals(GetUserId(user), idea.AssignedProjectOfficerUserId, StringComparison.Ordinal);
    }

    private static string? GetUserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    private static bool IsArchived(ProjectIdea idea)
    {
        return string.Equals(idea.Status, ProjectIdeaStatuses.Archived, StringComparison.Ordinal);
    }
}
