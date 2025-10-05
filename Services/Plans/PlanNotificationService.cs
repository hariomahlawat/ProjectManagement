using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Services.Plans;

public sealed class PlanNotificationService : IPlanNotificationService
{
    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<PlanNotificationService> _logger;

    public PlanNotificationService(
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        ILogger<PlanNotificationService> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task NotifyPlanSubmittedAsync(
        PlanVersion plan,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.PlanSubmitted,
            plan,
            project,
            actorUserId,
            recipients =>
            {
                AddRecipient(recipients, project.HodUserId);
            },
            BuildMetadata(
                plan,
                project,
                actorUserId,
                "PlanSubmitted",
                "submitted",
                summary => string.Format(
                    CultureInfo.InvariantCulture,
                    "Plan version {0} submitted for approval.",
                    plan.VersionNo)),
            cancellationToken);

    public Task NotifyPlanApprovedAsync(
        PlanVersion plan,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.PlanApproved,
            plan,
            project,
            actorUserId,
            recipients =>
            {
                AddRecipient(recipients, plan.OwnerUserId);
                AddRecipient(recipients, plan.SubmittedByUserId);
                AddRecipient(recipients, project.LeadPoUserId);
            },
            BuildMetadata(
                plan,
                project,
                actorUserId,
                "PlanApproved",
                "approved",
                summary => string.Format(
                    CultureInfo.InvariantCulture,
                    "Plan version {0} approved.",
                    plan.VersionNo)),
            cancellationToken);

    public Task NotifyPlanRejectedAsync(
        PlanVersion plan,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.PlanRejected,
            plan,
            project,
            actorUserId,
            recipients =>
            {
                AddRecipient(recipients, plan.OwnerUserId);
                AddRecipient(recipients, plan.SubmittedByUserId);
                AddRecipient(recipients, project.LeadPoUserId);
            },
            BuildMetadata(
                plan,
                project,
                actorUserId,
                "PlanRejected",
                "rejected",
                summary => string.Format(
                    CultureInfo.InvariantCulture,
                    "Plan version {0} rejected.",
                    plan.VersionNo)),
            cancellationToken);

    private async Task NotifyAsync(
        NotificationKind kind,
        PlanVersion plan,
        Project project,
        string actorUserId,
        Action<HashSet<string>> populateRecipients,
        NotificationMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
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
            populateRecipients(recipients);

            if (recipients.Count == 0)
            {
                _logger.LogInformation("No recipients resolved for plan notification {Kind} on plan {PlanId}.", kind, plan.Id);
                return;
            }

            var allowed = await FilterRecipientsAsync(kind, recipients, plan.ProjectId, cancellationToken);
            if (allowed.Count == 0)
            {
                _logger.LogInformation("All recipients opted out for plan notification {Kind} on plan {PlanId}.", kind, plan.Id);
                return;
            }

            var payload = new PlanNotificationPayload(
                plan.Id,
                plan.ProjectId,
                GetProjectName(project),
                plan.VersionNo,
                plan.Status.ToString(),
                plan.OwnerUserId,
                plan.SubmittedByUserId,
                plan.RejectionNote);

            await _publisher.PublishAsync(
                kind,
                allowed,
                payload,
                metadata.Module,
                metadata.EventType,
                metadata.ScopeType,
                metadata.ScopeId,
                plan.ProjectId,
                actorUserId,
                metadata.Route,
                metadata.Title,
                metadata.Summary,
                metadata.Fingerprint,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish plan notification {Kind} for plan {PlanId}.", kind, plan.Id);
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
        NotificationKind kind,
        IEnumerable<string> recipients,
        int projectId,
        CancellationToken cancellationToken)
    {
        var allowed = new List<string>();
        foreach (var recipient in recipients.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            if (await _preferences.AllowsAsync(kind, recipient, projectId, cancellationToken))
            {
                allowed.Add(recipient);
            }
        }

        return allowed;
    }

    private static NotificationMetadata BuildMetadata(
        PlanVersion plan,
        Project project,
        string actorUserId,
        string eventType,
        string actionVerb,
        Func<string?, string> summaryBuilder)
    {
        var projectName = GetProjectName(project);
        var title = string.Format(
            CultureInfo.InvariantCulture,
            "{0} plan {1}",
            projectName,
            actionVerb);

        var summary = summaryBuilder(projectName);

        return new NotificationMetadata(
            Module: "Plans",
            EventType: eventType,
            ScopeType: "Project",
            ScopeId: plan.ProjectId.ToString(CultureInfo.InvariantCulture),
            Route: BuildRoute(plan.ProjectId),
            Title: title,
            Summary: summary,
            Fingerprint: string.Format(
                CultureInfo.InvariantCulture,
                "plan:{0}:{1}",
                plan.Id,
                eventType));
    }

    private static string GetProjectName(Project project)
    {
        if (!string.IsNullOrWhiteSpace(project.Name))
        {
            return project.Name;
        }

        return string.Format(CultureInfo.InvariantCulture, "Project {0}", project.Id);
    }

    private static string BuildRoute(int projectId)
        => string.Format(CultureInfo.InvariantCulture, "/projects/{0}/timeline", projectId);

    private sealed record NotificationMetadata(
        string Module,
        string EventType,
        string ScopeType,
        string ScopeId,
        string Route,
        string Title,
        string Summary,
        string Fingerprint);

    private sealed record PlanNotificationPayload(
        int PlanId,
        int ProjectId,
        string ProjectName,
        int VersionNo,
        string Status,
        string? OwnerUserId,
        string? SubmittedByUserId,
        string? RejectionNote);
}
