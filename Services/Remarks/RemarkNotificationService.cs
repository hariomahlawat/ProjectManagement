using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Services.Remarks;

public sealed class RemarkNotificationService : IRemarkNotificationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<RemarkNotificationService> _logger;

    public RemarkNotificationService(
        UserManager<ApplicationUser> userManager,
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        ILogger<RemarkNotificationService> logger)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyRemarkCreatedAsync(
        Remark remark,
        RemarkActorContext actor,
        RemarkProjectInfo project,
        CancellationToken cancellationToken = default)
    {
        if (remark is null)
        {
            throw new ArgumentNullException(nameof(remark));
        }

        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        try
        {
            var recipients = await ResolveRecipientsAsync(project, remark, cancellationToken);
            if (recipients.Count == 0)
            {
                _logger.LogInformation(
                    "No remark notification recipients found for project {ProjectId}.",
                    project.ProjectId);
                return;
            }

            var optedInRecipients = await FilterOptOutAsync(
                recipients,
                project.ProjectId,
                cancellationToken);
            if (optedInRecipients.Count == 0)
            {
                _logger.LogInformation(
                    "All potential recipients for remark {RemarkId} have opted out of notifications.",
                    remark.Id);
                return;
            }

            var payload = BuildPayload(remark, actor, project);

            await _publisher.PublishAsync(
                NotificationKind.RemarkCreated,
                optedInRecipients,
                payload,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed preparing remark notification for remark {RemarkId}.", remark.Id);
        }
    }

    private async Task<HashSet<string>> ResolveRecipientsAsync(
        RemarkProjectInfo project,
        Remark remark,
        CancellationToken cancellationToken)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRecipient(recipients, project.LeadPoUserId);
        AddRecipient(recipients, project.HodUserId);

        if (remark.Mentions is { Count: > 0 })
        {
            foreach (var mention in remark.Mentions)
            {
                AddRecipient(recipients, mention.UserId);
            }
        }

        await AddRoleRecipientsAsync(recipients, "Comdt", cancellationToken);

        if (remark.Type == RemarkType.External)
        {
            await AddRoleRecipientsAsync(recipients, "MCO", cancellationToken);
        }

        return recipients;
    }

    private async Task AddRoleRecipientsAsync(
        ISet<string> recipients,
        string role,
        CancellationToken cancellationToken)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddRecipient(recipients, user.Id);
        }
    }

    private static void AddRecipient(ISet<string> recipients, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            recipients.Add(userId);
        }
    }

    private async Task<IReadOnlyCollection<string>> FilterOptOutAsync(
        HashSet<string> recipients,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return Array.Empty<string>();
        }

        var allowed = new List<string>(recipients.Count);

        foreach (var userId in recipients)
        {
            if (await _preferences.AllowsAsync(
                    NotificationKind.RemarkCreated,
                    userId,
                    projectId,
                    cancellationToken))
            {
                allowed.Add(userId);
            }
        }

        return allowed;
    }

    private static RemarkCreatedNotificationPayload BuildPayload(
        Remark remark,
        RemarkActorContext actor,
        RemarkProjectInfo project)
    {
        var stage = string.IsNullOrWhiteSpace(remark.StageNameSnapshot)
            ? string.IsNullOrWhiteSpace(remark.StageRef) ? "N/A" : remark.StageRef!
            : remark.StageNameSnapshot;

        return new RemarkCreatedNotificationPayload(
            remark.Id,
            project.ProjectId,
            project.ProjectName,
            actor.UserId,
            actor.ActorRole.ToString(),
            remark.Type.ToString(),
            BuildPreview(remark.Body),
            remark.EventDate,
            stage,
            remark.CreatedAtUtc);
    }

    private static string BuildPreview(string htmlBody)
    {
        var plain = ToPlainText(htmlBody);
        if (plain.Length <= 120)
        {
            return plain;
        }

        return string.Concat(plain.AsSpan(0, 120).TrimEnd(), "…");
    }

    private static string ToPlainText(string html)
        => Regex.Replace(html ?? string.Empty, "<.*?>", string.Empty).Trim();

    private sealed record RemarkCreatedNotificationPayload(
        int RemarkId,
        int ProjectId,
        string ProjectName,
        string AuthorUserId,
        string AuthorRole,
        string RemarkType,
        string Preview,
        DateOnly EventDate,
        string Stage,
        DateTime CreatedAtUtc);
}
