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

public sealed class StageNotificationService : IStageNotificationService
{
    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<StageNotificationService> _logger;

    public StageNotificationService(
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        ILogger<StageNotificationService> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
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

        try
        {
            var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddRecipient(recipients, project.LeadPoUserId);
            AddRecipient(recipients, project.HodUserId);

            if (recipients.Count == 0)
            {
                _logger.LogInformation(
                    "No recipients resolved for stage notification on project {ProjectId} stage {StageCode}.",
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
                "{0} stage {1} {2}",
                projectName,
                stage.StageCode,
                stage.Status.ToString());

            var summary = string.Format(
                CultureInfo.InvariantCulture,
                "Stage {0} moved from {1} to {2}.",
                stage.StageCode,
                previousStatus,
                stage.Status);

            var payload = new StageNotificationPayload(
                stage.Id,
                stage.ProjectId,
                stage.StageCode,
                projectName,
                previousStatus.ToString(),
                stage.Status.ToString(),
                stage.ActualStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                stage.CompletedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await _publisher.PublishAsync(
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
                fingerprint: string.Format(
                    CultureInfo.InvariantCulture,
                    "stage:{0}:{1}:{2}",
                    stage.ProjectId,
                    stage.StageCode,
                    stage.Status),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish stage notification for project {ProjectId} stage {StageCode}.",
                stage.ProjectId,
                stage.StageCode);
        }
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

    private static string BuildRoute(int projectId, string stageCode)
        => string.Format(CultureInfo.InvariantCulture, "/projects/{0}/timeline/stages/{1}", projectId, stageCode);

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
