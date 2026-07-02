using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Adds durable notification dispatch records to the current scoped DbContext without saving.
/// Business services use this contract when the notification must be committed atomically with
/// the source mutation in the same SaveChanges/transaction boundary.
/// </summary>
public interface INotificationOutboxWriter
{
    Task<int> QueueAsync(
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
