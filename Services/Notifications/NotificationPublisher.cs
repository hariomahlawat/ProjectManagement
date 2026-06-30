using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;

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

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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
        ArgumentNullException.ThrowIfNull(recipientUserIds);
        ArgumentNullException.ThrowIfNull(payload);

        var recipients = recipientUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.Length <= ActorUserIdMaxLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (recipients.Length == 0)
        {
            _logger.LogInformation("No valid recipients supplied for notification kind {Kind}.", kind);
            return;
        }

        if (projectId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId), "Project identifier must be a positive number.");
        }

        var normalizedModule = NormalizeText(module, ModuleMaxLength, appendEllipsis: false);
        var normalizedEventType = NormalizeText(eventType, EventTypeMaxLength, appendEllipsis: false);
        var normalizedScopeType = NormalizeText(scopeType, ScopeTypeMaxLength, appendEllipsis: false);
        var normalizedScopeId = NormalizeText(scopeId, ScopeIdMaxLength, appendEllipsis: false);
        var normalizedActorUserId = NormalizeText(actorUserId, ActorUserIdMaxLength, appendEllipsis: false);
        var normalizedRoute = NormalizeRouteSegments(route);
        var normalizedTitle = NormalizeText(title, TitleMaxLength, appendEllipsis: true);
        var normalizedSummary = NormalizeText(summary, SummaryMaxLength, appendEllipsis: true);
        var normalizedFingerprint = NormalizeFingerprint(fingerprint);
        var occurredUtc = _clock.UtcNow.UtcDateTime;

        var envelope = new NotificationEnvelopeV1(
            Version: "v1",
            Kind: kind.ToString(),
            OccurredUtc: occurredUtc,
            Module: normalizedModule,
            EventType: normalizedEventType,
            ScopeType: normalizedScopeType,
            ScopeId: normalizedScopeId,
            ProjectId: projectId,
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
            ProjectId = projectId,
            ActorUserId = normalizedActorUserId,
            Route = normalizedRoute,
            Title = normalizedTitle,
            Summary = normalizedSummary,
            Fingerprint = normalizedFingerprint,
            PayloadJson = payloadJson,
            CreatedUtc = occurredUtc,
            AttemptCount = 0,
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
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var trimmed = route.Trim();
        if (trimmed.Length > RouteMaxLength
            || !trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.Contains('\\')
            || trimmed.Any(char.IsControl))
        {
            return null;
        }

        // SECTION: Backward-compatible project route normalization.
        var normalized = Regex.Replace(
            trimmed,
            "/projects(?<id>\\d+)(?=/)",
            "/projects/${id}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        normalized = Regex.Replace(
            normalized,
            "/projects/(?<id>\\d+)/overview(?<rest>(?:[/?#].*)?)",
            match => $"/projects/overview/{match.Groups["id"].Value}{match.Groups["rest"].Value}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return normalized;
    }

    private static string? NormalizeText(string? value, int maxLength, bool appendEllipsis)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value.Trim(), "\\s+", " ");
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        if (!appendEllipsis || maxLength < 2)
        {
            return normalized[..maxLength];
        }

        return normalized[..(maxLength - 1)] + "…";
    }

    private static string? NormalizeFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var normalized = fingerprint.Trim();
        if (normalized.Length <= FingerprintMaxLength)
        {
            return normalized;
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
        return $"sha256:{digest}";
    }

    private sealed record NotificationEnvelopeV1(
        string Version,
        string Kind,
        DateTime OccurredUtc,
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
