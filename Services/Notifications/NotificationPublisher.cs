using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationPublisher : INotificationPublisher
{
    private const int ModuleMaxLength = 64;
    private const int EventTypeMaxLength = 128;
    private const int ScopeTypeMaxLength = 64;
    private const int ScopeIdMaxLength = 128;
    private const int RouteMaxLength = 2048;
    private const int TitleMaxLength = 200;
    private const int SummaryMaxLength = 2000;
    private const int FingerprintMaxLength = 128;
    private const int ActorUserIdMaxLength = 450;

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

    public Task PublishAsync(
        NotificationKind kind,
        IReadOnlyCollection<string> recipientUserIds,
        object payload,
        CancellationToken cancellationToken = default)
        => PublishAsync(
            kind,
            recipientUserIds,
            payload,
            module: null,
            eventType: null,
            scopeType: null,
            scopeId: null,
            projectId: null,
            actorUserId: null,
            route: null,
            title: null,
            summary: null,
            fingerprint: null,
            cancellationToken);

    public async Task PublishAsync(
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

        var normalizedModule = NormalizeMetadata(module, ModuleMaxLength, nameof(module));
        var normalizedEventType = NormalizeMetadata(eventType, EventTypeMaxLength, nameof(eventType));
        var normalizedScopeType = NormalizeMetadata(scopeType, ScopeTypeMaxLength, nameof(scopeType));
        var normalizedScopeId = NormalizeMetadata(scopeId, ScopeIdMaxLength, nameof(scopeId));
        var normalizedActorUserId = NormalizeMetadata(actorUserId, ActorUserIdMaxLength, nameof(actorUserId));
        var normalizedRoute = NormalizeRouteSegments(
            NormalizeMetadata(route, RouteMaxLength, nameof(route)));
        var normalizedTitle = NormalizeMetadata(title, TitleMaxLength, nameof(title));
        var normalizedSummary = NormalizeMetadata(summary, SummaryMaxLength, nameof(summary));
        var normalizedFingerprint = NormalizeMetadata(fingerprint, FingerprintMaxLength, nameof(fingerprint));

        int? validatedProjectId = projectId;
        if (validatedProjectId.HasValue && validatedProjectId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId), "Project identifier must be a positive number.");
        }

        var timestamp = _clock.UtcNow.UtcDateTime;

        var envelope = new NotificationEnvelopeV1(
            Version: "v1",
            Module: normalizedModule,
            EventType: normalizedEventType,
            ScopeType: normalizedScopeType,
            ScopeId: normalizedScopeId,
            ProjectId: validatedProjectId,
            ActorUserId: normalizedActorUserId,
            Route: normalizedRoute,
            Title: normalizedTitle,
            Summary: normalizedSummary,
            Fingerprint: normalizedFingerprint,
            Payload: payload);

        var payloadJson = JsonSerializer.Serialize(envelope, SerializerOptions);

        var dispatches = recipients.Select(userId => new NotificationDispatch
        {
            RecipientUserId = userId,
            Kind = kind,
            Module = normalizedModule,
            EventType = normalizedEventType,
            ScopeType = normalizedScopeType,
            ScopeId = normalizedScopeId,
            ProjectId = validatedProjectId,
            ActorUserId = normalizedActorUserId,
            Route = normalizedRoute,
            Title = normalizedTitle,
            Summary = normalizedSummary,
            Fingerprint = normalizedFingerprint,
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

    internal static string? NormalizeRouteSegments(string? route)
    {
        if (route is null)
        {
            return null;
        }

        return Regex.Replace(route, "/projects(?<id>\\d+)(?=/)", "/projects/${id}");
    }

    private static string? NormalizeMetadata(string? value, int maxLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"The value for {parameterName} exceeds the maximum length of {maxLength}.",
                parameterName);
        }

        return trimmed;
    }

    private sealed record NotificationEnvelopeV1(
        string Version,
        string? Module,
        string? EventType,
        string? ScopeType,
        string? ScopeId,
        int? ProjectId,
        string? ActorUserId,
        string? Route,
        string? Title,
        string? Summary,
        string? Fingerprint,
        object Payload);
}
