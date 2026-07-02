using System.Globalization;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Services.Notebook;

public sealed class NotebookNotificationService : INotebookNotificationService
{
    private const string ModuleName = "Notebook";
    private const string ScopeType = "NotebookItem";
    private const int TitlePreviewLength = 96;

    private readonly INotificationOutboxWriter _outbox;

    public NotebookNotificationService(INotificationOutboxWriter outbox)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    public Task QueueSharedAsync(
        NotebookItem item,
        NotebookItemCollaborator collaboration,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(collaboration);

        var ownerName = DisplayName(item.Owner, "A PRISM user");
        var accessText = collaboration.Role == NotebookCollaborationRole.Viewer ? "view" : "edit";
        var title = DisplayTitle(item);

        return _outbox.QueueAsync(
            NotificationKind.NotebookShared,
            new[] { collaboration.UserId },
            new NotebookCollaborationNotificationPayload(
                item.Id,
                title,
                item.OwnerId,
                collaboration.UserId,
                collaboration.Role.ToString(),
                collaboration.Version),
            module: ModuleName,
            eventType: "NotebookShared",
            scopeType: ScopeType,
            scopeId: item.Id.ToString("D", CultureInfo.InvariantCulture),
            projectId: null,
            actorUserId: actorUserId,
            route: BuildSharedRoute(item.Id),
            title: "A note was shared with you",
            summary: $"{ownerName} shared “{title}” with you. You can {accessText} this note.",
            fingerprint: BuildFingerprint("shared", item.Id, collaboration.UserId, collaboration.Version),
            cancellationToken);
    }

    public Task QueueAccessRemovedAsync(
        NotebookItem item,
        NotebookItemCollaborator collaboration,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(collaboration);

        var ownerName = DisplayName(item.Owner, "The note owner");
        var title = DisplayTitle(item);

        return _outbox.QueueAsync(
            NotificationKind.NotebookAccessRemoved,
            new[] { collaboration.UserId },
            new NotebookCollaborationNotificationPayload(
                item.Id,
                title,
                item.OwnerId,
                collaboration.UserId,
                collaboration.Role.ToString(),
                collaboration.Version),
            module: ModuleName,
            eventType: "NotebookAccessRemoved",
            scopeType: ScopeType,
            scopeId: item.Id.ToString("D", CultureInfo.InvariantCulture),
            projectId: null,
            actorUserId: actorUserId,
            route: "/Notebook?view=shared",
            title: "Access to a shared note was removed",
            summary: $"{ownerName} removed your access to “{title}”.",
            fingerprint: BuildFingerprint("access-removed", item.Id, collaboration.UserId, collaboration.Version),
            cancellationToken);
    }

    public Task QueueCollaborationLeftAsync(
        NotebookItem item,
        NotebookItemCollaborator collaboration,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(collaboration);

        var collaboratorName = DisplayName(collaboration.User, "A collaborator");
        var title = DisplayTitle(item);

        return _outbox.QueueAsync(
            NotificationKind.NotebookCollaborationLeft,
            new[] { item.OwnerId },
            new NotebookCollaborationNotificationPayload(
                item.Id,
                title,
                item.OwnerId,
                collaboration.UserId,
                collaboration.Role.ToString(),
                collaboration.Version),
            module: ModuleName,
            eventType: "NotebookCollaborationLeft",
            scopeType: ScopeType,
            scopeId: item.Id.ToString("D", CultureInfo.InvariantCulture),
            projectId: null,
            actorUserId: actorUserId,
            route: BuildOwnerRoute(item.Id),
            title: "A collaborator left your note",
            summary: $"{collaboratorName} left “{title}”.",
            fingerprint: BuildFingerprint("collaboration-left", item.Id, collaboration.UserId, collaboration.Version),
            cancellationToken);
    }

    private static string BuildSharedRoute(Guid itemId)
        => $"/Notebook?view=shared&note={Uri.EscapeDataString(itemId.ToString("D", CultureInfo.InvariantCulture))}";

    private static string BuildOwnerRoute(Guid itemId)
        => $"/Notebook?view=home&note={Uri.EscapeDataString(itemId.ToString("D", CultureInfo.InvariantCulture))}";

    private static string BuildFingerprint(string eventName, Guid itemId, string userId, Guid eventId)
        => $"notebook:{itemId:N}:{eventName}:{userId}:{eventId:N}";

    private static string DisplayTitle(NotebookItem item)
    {
        var value = string.IsNullOrWhiteSpace(item.Title) ? "Untitled note" : item.Title.Trim();
        return value.Length <= TitlePreviewLength
            ? value
            : value[..(TitlePreviewLength - 1)] + "…";
    }

    private static string DisplayName(ApplicationUser? user, string fallback)
    {
        if (user is null)
        {
            return fallback;
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email.Trim();
        }

        return fallback;
    }

    private sealed record NotebookCollaborationNotificationPayload(
        Guid NotebookItemId,
        string NotebookTitle,
        string OwnerUserId,
        string CollaboratorUserId,
        string CollaborationRole,
        Guid CollaborationEventId);
}
