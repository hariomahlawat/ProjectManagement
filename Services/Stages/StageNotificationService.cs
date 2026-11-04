using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Queues stage notifications into the caller's DbContext without saving. Callers run it
/// through <see cref="TransactionalNotificationQueue"/> inside the business transaction, which
/// saves the rows with the change and contains any failure; exceptions here therefore
/// propagate instead of being swallowed.
/// </summary>
public sealed class StageNotificationService : IStageNotificationService
{
    private readonly INotificationOutboxWriter _outbox;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<StageNotificationService> _logger;

    public StageNotificationService(
        INotificationOutboxWriter outbox,
        INotificationPreferenceService preferences,
        ILogger<StageNotificationService> logger)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyStageStatusChangedAsync(
        ProjectStage stage,
        Project project,
        StageStatus previousStatus,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (stage is null)
        {
            throw new ArgumentNullException(nameof(stage));
        }

        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRecipient(recipients, project.LeadPoUserId);
        AddRecipient(recipients, project.HodUserId);
        recipients.Remove(actorUserId.Trim());

        if (recipients.Count == 0)
        {
            _logger.LogInformation(
                "No recipients other than the actor resolved for stage notification on project {ProjectId} stage {StageCode}.",
                stage.ProjectId,
                stage.StageCode);
            return;
        }

        var allowed = await FilterRecipientsAsync(recipients, stage.ProjectId, cancellationToken);
        if (allowed.Count == 0)
        {
            _logger.LogInformation(
                "All recipients opted out for stage notification on project {ProjectId} stage {StageCode}.",
                stage.ProjectId,
                stage.StageCode);
            return;
        }

        var projectName = GetProjectName(project);
        var eventType = "StageStatusChanged";
        var route = BuildRoute(stage.ProjectId, stage.StageCode);
        var title = string.Format(
            CultureInfo.InvariantCulture,
            "{0} stage {1}",
            stage.StageCode,
            ToDisplayStatus(stage.Status));

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Status changed from {0} to {1}.",
            ToSentenceStatus(previousStatus),
            ToSentenceStatus(stage.Status));

        var payload = new StageNotificationPayload(
            stage.Id,
            stage.ProjectId,
            stage.StageCode,
            projectName,
            previousStatus.ToString(),
            stage.Status.ToString(),
            stage.ActualStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            stage.CompletedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await _outbox.QueueAsync(
            NotificationKind.StageStatusChanged,
            allowed,
            payload,
            module: "Stages",
            eventType: eventType,
            scopeType: "Stage",
            scopeId: string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1}",
                stage.ProjectId,
                stage.StageCode),
            projectId: stage.ProjectId,
            actorUserId: actorUserId,
            route: route,
            title: title,
            summary: summary,
            fingerprint: null,
            cancellationToken: cancellationToken);
    }

    private static void AddRecipient(ISet<string> recipients, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            recipients.Add(userId);
        }
    }

    private async Task<IReadOnlyCollection<string>> FilterRecipientsAsync(
        IEnumerable<string> recipients,
        int projectId,
        CancellationToken cancellationToken)
    {
        var allowed = new List<string>();
        foreach (var recipient in recipients.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            if (await _preferences.AllowsAsync(
                    NotificationKind.StageStatusChanged,
                    recipient,
                    projectId,
                    cancellationToken))
            {
                allowed.Add(recipient);
            }
        }

        return allowed;
    }

    private static string GetProjectName(Project project)
    {
        if (!string.IsNullOrWhiteSpace(project.Name))
        {
            return project.Name;
        }

        return string.Format(CultureInfo.InvariantCulture, "Project {0}", project.Id);
    }


    private static string ToDisplayStatus(StageStatus status)
        => status switch
        {
            StageStatus.NotStarted => "not started",
            StageStatus.InProgress => "in progress",
            StageStatus.Completed => "completed",
            StageStatus.Skipped => "skipped",
            StageStatus.Blocked => "blocked",
            _ => status.ToString(),
        };

    private static string ToSentenceStatus(StageStatus status)
    {
        var display = ToDisplayStatus(status);
        return string.IsNullOrEmpty(display)
            ? display
            : char.ToUpperInvariant(display[0]) + display[1..];
    }

    private static string BuildRoute(int projectId, string stageCode)
    {
        var baseRoute = string.Format(CultureInfo.InvariantCulture, "/projects/overview/{0}", projectId);

        if (string.IsNullOrWhiteSpace(stageCode))
        {
            return baseRoute + "#timeline";
        }

        var safeStage = Uri.EscapeDataString(stageCode);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}?timeline-stage={1}#timeline",
            baseRoute,
            safeStage);
    }

    private sealed record StageNotificationPayload(
        int StageId,
        int ProjectId,
        string StageCode,
        string ProjectName,
        string PreviousStatus,
        string CurrentStatus,
        string? ActualStart,
        string? CompletedOn);
}
