using System.Security.Claims;
using ProjectManagement.Configuration;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaPermissionService
{
    // SECTION: Idea-level permissions
    // Preserve existing creation governance: Comdt/HoD/Admin may create Ideas.
    public bool CanCreateIdea(ClaimsPrincipal user) => IsLifecycleAuthority(user);

    /// <summary>
    /// Every authenticated PRISM user may view every non-deleted Idea.
    /// Deleted Ideas remain visible only through the governed recovery workspace.
    /// </summary>
    public bool CanViewIdea(ClaimsPrincipal user, ProjectIdea idea)
        => user?.Identity?.IsAuthenticated == true && !idea.IsDeleted;

    public bool CanViewDeletedIdeas(ClaimsPrincipal user) => IsLifecycleAuthority(user);

    /// <summary>
    /// Operational editing: assigned Project Officer, any HoD, or Comdt.
    /// Archived/deleted Ideas are read-only until restored.
    /// </summary>
    public bool CanEditIdeaCore(ClaimsPrincipal user, ProjectIdea idea)
        => IsWritable(idea) && IsOperationalEditor(user, idea);

    public bool CanEditDescription(ClaimsPrincipal user, ProjectIdea idea)
        => CanEditIdeaCore(user, idea);

    public bool CanEditIdea(ClaimsPrincipal user, ProjectIdea idea)
        => CanEditIdeaCore(user, idea);

    /// <summary>
    /// Lifecycle control is intentionally stricter than operational editing.
    /// Assigned Project Officers cannot archive/delete/restore solely by assignment.
    /// </summary>
    public bool CanArchiveIdea(ClaimsPrincipal user) => IsLifecycleAuthority(user);
    public bool CanRestoreIdea(ClaimsPrincipal user) => IsLifecycleAuthority(user);
    public bool CanDeleteIdea(ClaimsPrincipal user) => IsLifecycleAuthority(user);
    public bool CanRestoreDeletedIdea(ClaimsPrincipal user) => IsLifecycleAuthority(user);

    // SECTION: Collaboration permissions
    // Discussion remains organisation-visible and collaborative for authenticated users.
    public bool CanAddComment(ClaimsPrincipal user, ProjectIdea idea)
        => IsWritable(idea) && CanViewIdea(user, idea);

    public bool CanAddConferenceComment(ClaimsPrincipal user, ProjectIdea idea)
        => IsWritable(idea)
            && CanViewIdea(user, idea)
            && IsConferenceAuthority(user);

    public string GetDefaultCommentType(ClaimsPrincipal user, ProjectIdea idea)
        => user?.Identity?.IsAuthenticated == true
           && user.IsInRole(RoleNames.Comdt)
           && CanAddConferenceComment(user, idea)
            ? ProjectIdeaCommentTypes.Conference
            : ProjectIdeaCommentTypes.General;

    public bool CanEditComment(
        ClaimsPrincipal user,
        ProjectIdea idea,
        ProjectIdeaComment comment,
        DateTime? nowUtc = null)
        => CanMutateComment(user, idea, comment, nowUtc ?? DateTime.UtcNow, isDelete: false);

    public bool CanDeleteComment(
        ClaimsPrincipal user,
        ProjectIdea idea,
        ProjectIdeaComment comment,
        DateTime? nowUtc = null)
        => CanMutateComment(user, idea, comment, nowUtc ?? DateTime.UtcNow, isDelete: true);

    // Preserve the established collaboration behaviour for notes/documents. This is
    // deliberately separate from core Idea-edit authority so the permission change
    // does not create unrelated regressions in existing collaboration workflows.
    public bool CanAddNote(ClaimsPrincipal user, ProjectIdea idea)
        => IsWritable(idea) && (IsLifecycleAuthority(user) || IsAssignedProjectOfficer(user, idea));

    public bool CanUploadDocument(ClaimsPrincipal user, ProjectIdea idea)
        => IsWritable(idea) && (IsLifecycleAuthority(user) || IsAssignedProjectOfficer(user, idea));

    public bool CanDeleteDocument(ClaimsPrincipal user, ProjectIdeaDocument document, ProjectIdea idea)
    {
        return IsWritable(idea)
            && CanViewIdea(user, idea)
            && (IsLifecycleAuthority(user)
                || IsAssignedProjectOfficer(user, idea)
                || string.Equals(GetUserId(user), document.UploadedByUserId, StringComparison.Ordinal));
    }

    // SECTION: Helpers
    private bool CanMutateComment(
        ClaimsPrincipal user,
        ProjectIdea idea,
        ProjectIdeaComment comment,
        DateTime nowUtc,
        bool isDelete)
    {
        if (user?.Identity?.IsAuthenticated != true || !CanViewIdea(user, idea))
        {
            return false;
        }

        var userId = GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var decision = ProjectIdeaGovernancePolicy.EvaluateCommentMutation(
            idea,
            comment,
            new ProjectIdeaActorContext(userId, UserRoles(user)),
            ToUtc(nowUtc),
            isDelete);

        return decision.IsAllowed;
    }

    private static bool IsOperationalEditor(ClaimsPrincipal user, ProjectIdea idea)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var userId = GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        return ProjectIdeaGovernancePolicy.CanEditIdeaRecord(
            idea.AssignedProjectOfficerUserId,
            idea.Status,
            idea.IsDeleted,
            new ProjectIdeaActorContext(userId, UserRoles(user)));
    }

    private static bool IsLifecycleAuthority(ClaimsPrincipal user)
    {
        return user?.Identity?.IsAuthenticated == true
            && (user.IsInRole(RoleNames.Admin)
                || user.IsInRole(RoleNames.HoD)
                || user.IsInRole(RoleNames.Comdt));
    }

    private static bool IsConferenceAuthority(ClaimsPrincipal user)
    {
        return user?.Identity?.IsAuthenticated == true
            && Policies.ConferenceRemarks.ManageAllowedRoles.Any(user.IsInRole);
    }

    private static bool IsAssignedProjectOfficer(ClaimsPrincipal user, ProjectIdea idea)
    {
        return user?.Identity?.IsAuthenticated == true
            && string.Equals(GetUserId(user), idea.AssignedProjectOfficerUserId, StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<string> UserRoles(ClaimsPrincipal user)
        => user.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? GetUserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    private static bool IsWritable(ProjectIdea idea)
        => !idea.IsDeleted && !IsArchived(idea);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static bool IsArchived(ProjectIdea idea)
        => string.Equals(idea.Status, ProjectIdeaStatuses.Archived, StringComparison.OrdinalIgnoreCase);
}
