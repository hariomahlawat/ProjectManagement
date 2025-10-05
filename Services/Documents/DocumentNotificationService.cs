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

namespace ProjectManagement.Services.Documents;

public sealed class DocumentNotificationService : IDocumentNotificationService
{
    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<DocumentNotificationService> _logger;

    public DocumentNotificationService(
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        ILogger<DocumentNotificationService> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task NotifyDocumentPublishedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.DocumentPublished,
            document,
            project,
            actorUserId,
            "DocumentPublished",
            "published",
            cancellationToken);

    public Task NotifyDocumentReplacedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.DocumentReplaced,
            document,
            project,
            actorUserId,
            "DocumentReplaced",
            "updated",
            cancellationToken);

    public Task NotifyDocumentArchivedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.DocumentArchived,
            document,
            project,
            actorUserId,
            "DocumentArchived",
            "archived",
            cancellationToken);

    public Task NotifyDocumentRestoredAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.DocumentRestored,
            document,
            project,
            actorUserId,
            "DocumentRestored",
            "restored",
            cancellationToken);

    public Task NotifyDocumentDeletedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default)
        => NotifyAsync(
            NotificationKind.DocumentDeleted,
            document,
            project,
            actorUserId,
            "DocumentDeleted",
            "deleted",
            cancellationToken);

    private async Task NotifyAsync(
        NotificationKind kind,
        ProjectDocument document,
        Project project,
        string actorUserId,
        string eventType,
        string actionVerb,
        CancellationToken cancellationToken)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
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
                    "No recipients resolved for document notification {Kind} on document {DocumentId}.",
                    kind,
                    document.Id);
                return;
            }

            var allowed = await FilterRecipientsAsync(kind, recipients, project.Id, cancellationToken);
            if (allowed.Count == 0)
            {
                _logger.LogInformation(
                    "All recipients opted out for document notification {Kind} on document {DocumentId}.",
                    kind,
                    document.Id);
                return;
            }

            var projectName = GetProjectName(project);
            var route = BuildRoute(project.Id);
            var title = string.Format(
                CultureInfo.InvariantCulture,
                "{0} document {1} {2}",
                projectName,
                document.Title,
                actionVerb);

            var summary = string.Format(
                CultureInfo.InvariantCulture,
                "Document {0} was {1}.",
                document.Title,
                actionVerb);

            var payload = new DocumentNotificationPayload(
                document.Id,
                document.ProjectId,
                projectName,
                document.StageId,
                document.Title,
                document.Status.ToString(),
                document.FileStamp,
                document.UploadedByUserId,
                document.UploadedAtUtc?.ToString("o", CultureInfo.InvariantCulture));

            await _publisher.PublishAsync(
                kind,
                allowed,
                payload,
                module: "Documents",
                eventType: eventType,
                scopeType: "Document",
                scopeId: document.Id.ToString(CultureInfo.InvariantCulture),
                projectId: document.ProjectId,
                actorUserId: actorUserId,
                route: route,
                title: title,
                summary: summary,
                fingerprint: string.Format(
                    CultureInfo.InvariantCulture,
                    "document:{0}:{1}:{2}",
                    document.Id,
                    document.FileStamp,
                    eventType),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish document notification {Kind} for document {DocumentId}.", kind, document.Id);
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

    private static string GetProjectName(Project project)
    {
        if (!string.IsNullOrWhiteSpace(project.Name))
        {
            return project.Name;
        }

        return string.Format(CultureInfo.InvariantCulture, "Project {0}", project.Id);
    }

    private static string BuildRoute(int projectId)
        => string.Format(CultureInfo.InvariantCulture, "/projects/{0}/documents", projectId);

    private sealed record DocumentNotificationPayload(
        int DocumentId,
        int ProjectId,
        string ProjectName,
        int? StageId,
        string Title,
        string Status,
        int FileStamp,
        string? UploadedByUserId,
        string? UploadedAtUtc);
}
