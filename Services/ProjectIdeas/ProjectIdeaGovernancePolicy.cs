using ProjectManagement.Configuration;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

/// <summary>
/// Central governance rules for Project Idea lifecycle and discussion mutations.
/// Keeps UI permission checks and command-side enforcement aligned.
/// </summary>
public static class ProjectIdeaGovernancePolicy
{
    public static readonly TimeSpan AuthorMutationWindow = TimeSpan.FromHours(3);

    public const string PermissionDeniedMessage = "You do not have permission for this action.";
    public const string EditWindowMessage = "You can edit your remark within 3 hours of posting.";
    public const string DeleteWindowMessage = "You can delete your remark within 3 hours of posting.";
    public const string ArchivedIdeaMessage = "Archived ideas cannot be updated. Restore the idea first.";
    public const string DeletedIdeaMessage = "Deleted ideas cannot be updated.";

    /// <summary>
    /// Lifecycle authority is deliberately separate from operational editing.
    /// Comdt/HoD/Admin may archive, restore and soft-delete Ideas.
    /// </summary>
    public static bool CanManageIdeaLifecycle(IReadOnlyCollection<string> roles)
        => HasRole(roles, RoleNames.Comdt)
           || HasRole(roles, RoleNames.HoD)
           || HasRole(roles, RoleNames.Admin);

    // Retained as a compatibility alias for existing deletion command semantics.
    public static bool CanDeleteAnyIdea(IReadOnlyCollection<string> roles)
        => CanManageIdeaLifecycle(roles);

    /// <summary>
    /// Operational editing is allowed to the currently assigned Project Officer,
    /// any HoD and Comdt. Admin alone does not grant Idea-edit authority.
    /// </summary>
    public static bool CanEditIdeaRecord(
        string? assignedProjectOfficerUserId,
        string status,
        bool isDeleted,
        ProjectIdeaActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (isDeleted || string.Equals(status, ProjectIdeaStatuses.Archived, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HasRole(actor.Roles, RoleNames.Comdt) || HasRole(actor.Roles, RoleNames.HoD))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(actor.UserId)
            && string.Equals(assignedProjectOfficerUserId, actor.UserId, StringComparison.Ordinal);
    }

    public static bool CanManageConferenceComments(IReadOnlyCollection<string> roles)
        => HasRole(roles, RoleNames.Comdt)
           || HasRole(roles, RoleNames.HoD);

    public static bool HasGeneralCommentOverride(IReadOnlyCollection<string> roles)
        => HasRole(roles, RoleNames.Comdt)
           || HasRole(roles, RoleNames.HoD)
           || HasRole(roles, RoleNames.Admin);

    public static ProjectIdeaMutationDecision EvaluateCommentMutation(
        ProjectIdea idea,
        ProjectIdeaComment comment,
        ProjectIdeaActorContext actor,
        DateTime nowUtc,
        bool isDelete)
    {
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(actor);

        if (idea.IsDeleted)
        {
            return ProjectIdeaMutationDecision.Denied(DeletedIdeaMessage, "IdeaDeleted");
        }

        if (string.Equals(idea.Status, ProjectIdeaStatuses.Archived, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectIdeaMutationDecision.Denied(ArchivedIdeaMessage, "IdeaArchived");
        }

        if (comment.IsDeleted)
        {
            return ProjectIdeaMutationDecision.Denied("This discussion remark has already been deleted.", "CommentDeleted");
        }

        // All authenticated/authorised PRISM users may view operational Ideas.
        // Command actors are represented by a resolved user id.
        if (string.IsNullOrWhiteSpace(actor.UserId))
        {
            return ProjectIdeaMutationDecision.Denied(PermissionDeniedMessage, "IdeaNotVisible");
        }

        if (string.Equals(comment.CommentType, ProjectIdeaCommentTypes.Conference, StringComparison.OrdinalIgnoreCase))
        {
            return CanManageConferenceComments(actor.Roles)
                ? ProjectIdeaMutationDecision.Allowed()
                : ProjectIdeaMutationDecision.Denied(PermissionDeniedMessage, "ConferenceRequiresCommandRole");
        }

        if (HasGeneralCommentOverride(actor.Roles))
        {
            return ProjectIdeaMutationDecision.Allowed();
        }

        if (!string.Equals(comment.CreatedByUserId, actor.UserId, StringComparison.Ordinal))
        {
            return ProjectIdeaMutationDecision.Denied(PermissionDeniedMessage, "NotAuthor");
        }

        var createdUtc = ToUtc(comment.CreatedAt);
        if (nowUtc <= createdUtc.Add(AuthorMutationWindow))
        {
            return ProjectIdeaMutationDecision.Allowed();
        }

        return ProjectIdeaMutationDecision.Denied(
            isDelete ? DeleteWindowMessage : EditWindowMessage,
            "AuthorWindowExpired");
    }

    public static bool HasRole(IReadOnlyCollection<string> roles, string roleName)
        => roles.Any(role => string.Equals(role, roleName, StringComparison.OrdinalIgnoreCase));

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public sealed record ProjectIdeaActorContext
{
    public ProjectIdeaActorContext(string userId, IEnumerable<string>? roles)
    {
        UserId = userId?.Trim() ?? string.Empty;
        Roles = roles?
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }

    public string UserId { get; }
    public IReadOnlyCollection<string> Roles { get; }
}

public sealed record ProjectIdeaMutationDecision(
    bool IsAllowed,
    string? Message,
    string? ReasonCode)
{
    public static ProjectIdeaMutationDecision Allowed() => new(true, null, null);
    public static ProjectIdeaMutationDecision Denied(string message, string reasonCode) => new(false, message, reasonCode);
}
