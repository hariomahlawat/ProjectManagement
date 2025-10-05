using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Tests;

internal sealed class RecordingNotificationPublisher : INotificationPublisher
{
    public List<NotificationCall> Events { get; } = new();

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

    public Task PublishAsync(
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
        Events.Add(new NotificationCall(
            kind,
            recipientUserIds.ToArray(),
            payload,
            module,
            eventType,
            scopeType,
            scopeId,
            projectId,
            actorUserId,
            route,
            title,
            summary,
            fingerprint));
        return Task.CompletedTask;
    }
}

internal sealed record NotificationCall(
    NotificationKind Kind,
    IReadOnlyCollection<string> Recipients,
    object Payload,
    string? Module,
    string? EventType,
    string? ScopeType,
    string? ScopeId,
    int? ProjectId,
    string? ActorUserId,
    string? Route,
    string? Title,
    string? Summary,
    string? Fingerprint);

internal sealed class TestPreferenceService : INotificationPreferenceService
{
    private readonly Func<NotificationKind, string, int?, bool> _allowEvaluator;

    public TestPreferenceService(Func<NotificationKind, string, int?, bool>? allowEvaluator = null)
    {
        _allowEvaluator = allowEvaluator ?? ((_, _, _) => true);
    }

    public List<(NotificationKind Kind, string UserId, int? ProjectId)> Calls { get; } = new();

    public Task<bool> AllowsAsync(NotificationKind kind, string userId, int? projectId = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((kind, userId, projectId));
        return Task.FromResult(_allowEvaluator(kind, userId, projectId));
    }
}
