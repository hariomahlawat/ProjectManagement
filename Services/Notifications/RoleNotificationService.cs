using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

public sealed class RoleNotificationService : ProjectManagement.Services.IRoleNotificationService
{
    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<RoleNotificationService> _logger;

    public RoleNotificationService(
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        ILogger<RoleNotificationService> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyRolesUpdatedAsync(
        ApplicationUser user,
        IReadOnlyCollection<string> addedRoles,
        IReadOnlyCollection<string> removedRoles,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        if (string.IsNullOrWhiteSpace(user.Id))
        {
            throw new ArgumentException("User must have an identifier.", nameof(user));
        }

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        try
        {
            if (!await _preferences.AllowsAsync(
                    NotificationKind.RoleAssignmentsChanged,
                    user.Id,
                    projectId: null,
                    cancellationToken))
            {
                _logger.LogInformation("User {UserId} opted out of role notifications.", user.Id);
                return;
            }

            var recipient = new[] { user.Id };
            var added = (addedRoles ?? Array.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var removed = (removedRoles ?? Array.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var displayName = string.IsNullOrWhiteSpace(user.FullName)
                ? user.UserName ?? user.Id
                : user.FullName;

            var summaryParts = new List<string>();
            if (added.Length > 0)
            {
                summaryParts.Add("Added: " + string.Join(", ", added));
            }

            if (removed.Length > 0)
            {
                summaryParts.Add("Removed: " + string.Join(", ", removed));
            }

            var summary = summaryParts.Count > 0
                ? string.Join("; ", summaryParts)
                : "Role assignments were reviewed.";

            var payload = new RoleNotificationPayload(
                user.Id,
                displayName,
                added,
                removed);

            await _publisher.PublishAsync(
                NotificationKind.RoleAssignmentsChanged,
                recipient,
                payload,
                module: "Users",
                eventType: "RoleAssignmentsChanged",
                scopeType: "User",
                scopeId: user.Id,
                projectId: null,
                actorUserId: actorUserId,
                route: "/admin/users",
                title: "Your roles have been updated",
                summary: summary,
                fingerprint: string.Format(
                    CultureInfo.InvariantCulture,
                    "role:{0}:{1}",
                    user.Id,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish role notification for user {UserId}.", user.Id);
        }
    }

    private sealed record RoleNotificationPayload(
        string UserId,
        string DisplayName,
        IReadOnlyCollection<string> AddedRoles,
        IReadOnlyCollection<string> RemovedRoles);
}
