using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaPermissionService
{
    // SECTION: Privileged role checks
    public bool CanCreateIdea(ClaimsPrincipal user) => IsPrivileged(user);
    public bool CanArchiveIdea(ClaimsPrincipal user) => IsPrivileged(user);
    public bool CanEditIdea(ClaimsPrincipal user, ProjectIdea idea) => IsPrivileged(user) || IsAssignedProjectOfficer(user, idea);
    public bool CanAddComment(ClaimsPrincipal user, ProjectIdea idea) => IsPrivileged(user) || IsAssigned(user, idea);
    public bool CanAddNote(ClaimsPrincipal user, ProjectIdea idea) => IsPrivileged(user) || IsAssigned(user, idea);
    public bool CanUploadDocument(ClaimsPrincipal user, ProjectIdea idea) => IsPrivileged(user) || IsAssigned(user, idea);
    public bool CanDeleteDocument(ClaimsPrincipal user, ProjectIdeaDocument document, ProjectIdea idea) => IsPrivileged(user) || IsAssignedProjectOfficer(user, idea) || string.Equals(GetUserId(user), document.UploadedByUserId, StringComparison.Ordinal);

    // SECTION: Helpers
    private static bool IsPrivileged(ClaimsPrincipal user) => user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.HoD) || user.IsInRole(RoleNames.Comdt);
    private static bool IsAssigned(ClaimsPrincipal user, ProjectIdea idea) => IsAssignedProjectOfficer(user, idea) || string.Equals(GetUserId(user), idea.AssignedHodUserId, StringComparison.Ordinal);
    private static bool IsAssignedProjectOfficer(ClaimsPrincipal user, ProjectIdea idea) => string.Equals(GetUserId(user), idea.AssignedProjectOfficerUserId, StringComparison.Ordinal);
    private static string? GetUserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);
}
