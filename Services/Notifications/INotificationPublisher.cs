using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

public interface INotificationPublisher
{
    Task PublishAsync(
        NotificationKind kind,
        IReadOnlyCollection<string> recipientUserIds,
        object payload,
        CancellationToken cancellationToken = default);

    Task PublishAsync(
        NotificationKind kind,
        IReadOnlyCollection<string> recipientUserIds,
        object payload,
        string? module,
        string? eventType,
        string? scopeType,
        string? scopeId,
        int? projectId,
        string? actorUserId,
        string? route,
        string? title,
        string? summary,
        string? fingerprint,
        CancellationToken cancellationToken = default);
}
