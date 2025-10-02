using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationPublisher : INotificationPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(
        ApplicationDbContext db,
        IClock clock,
        ILogger<NotificationPublisher> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(
        NotificationKind kind,
        IReadOnlyCollection<string> recipientUserIds,
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (recipientUserIds is null)
        {
            throw new ArgumentNullException(nameof(recipientUserIds));
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var recipients = recipientUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (recipients.Length == 0)
        {
            _logger.LogInformation("No recipients supplied for notification kind {Kind}.", kind);
            return;
        }

        var timestamp = _clock.UtcNow.UtcDateTime;
        var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);

        var dispatches = recipients.Select(userId => new NotificationDispatch
        {
            RecipientUserId = userId,
            Kind = kind,
            PayloadJson = payloadJson,
            CreatedUtc = timestamp,
            AttemptCount = 0
        }).ToArray();

        await _db.NotificationDispatches.AddRangeAsync(dispatches, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Queued notification {Kind} for {RecipientCount} recipients.",
            kind,
            dispatches.Length);
    }
}
