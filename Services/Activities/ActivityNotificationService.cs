using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityNotificationService : IActivityNotificationService
{
    private static readonly string[] ApproverRoles = { "Admin", "HoD" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<ActivityNotificationService> _logger;

    public ActivityNotificationService(
        UserManager<ApplicationUser> userManager,
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        ILogger<ActivityNotificationService> logger)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyDeleteRequestedAsync(ActivityDeleteNotificationContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var recipients = await ResolveApproverRecipientsAsync(cancellationToken);
        recipients.RemoveWhere(userId => string.Equals(userId, context.RequestedByUserId, StringComparison.OrdinalIgnoreCase));

        var optedIn = await FilterRecipientsAsync(NotificationKind.ActivityDeleteRequested, recipients, cancellationToken);
        if (optedIn.Count == 0)
        {
            _logger.LogInformation("No recipients for activity delete request {RequestId}.", context.RequestId);
            return;
        }

        var requesterName = await ResolveDisplayNameAsync(context.RequestedByUserId, context.RequestedByDisplayName, cancellationToken);
        var payload = BuildPayload(context, requesterName, null, null, null, status: "Pending");

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Delete requested — {0}.",
            context.ActivityTitle);

        await PublishAsync(
            NotificationKind.ActivityDeleteRequested,
            optedIn,
            payload,
            context.RequestedByUserId,
            "/Activities/Approvals",
            $"Delete requested — {context.ActivityTitle}",
            summary,
            CreateFingerprint(context.ActivityId, context.RequestId, "requested"),
            cancellationToken);
    }

    public async Task NotifyDeleteApprovedAsync(ActivityDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            throw new ArgumentException("Approver user id is required.", nameof(approverUserId));
        }

        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRecipient(recipients, context.RequestedByUserId);

        var optedIn = await FilterRecipientsAsync(NotificationKind.ActivityDeleteApproved, recipients, cancellationToken);
        if (optedIn.Count == 0)
        {
            _logger.LogInformation("No recipients opted in for approved activity delete request {RequestId}.", context.RequestId);
            return;
        }

        var requesterName = await ResolveDisplayNameAsync(context.RequestedByUserId, context.RequestedByDisplayName, cancellationToken);
        var approverName = await ResolveDisplayNameAsync(approverUserId, null, cancellationToken);

        var payload = BuildPayload(context, requesterName, approverUserId, approverName, null, status: "Approved");

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Delete request approved by {0}.",
            approverName);

        await PublishAsync(
            NotificationKind.ActivityDeleteApproved,
            optedIn,
            payload,
            approverUserId,
            "/Activities/Index",
            $"Delete request approved — {context.ActivityTitle}",
            summary,
            CreateFingerprint(context.ActivityId, context.RequestId, "approved"),
            cancellationToken);
    }

    public async Task NotifyDeleteRejectedAsync(ActivityDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            throw new ArgumentException("Approver user id is required.", nameof(approverUserId));
        }

        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRecipient(recipients, context.RequestedByUserId);

        var optedIn = await FilterRecipientsAsync(NotificationKind.ActivityDeleteRejected, recipients, cancellationToken);
        if (optedIn.Count == 0)
        {
            _logger.LogInformation("No recipients opted in for rejected activity delete request {RequestId}.", context.RequestId);
            return;
        }

        var requesterName = await ResolveDisplayNameAsync(context.RequestedByUserId, context.RequestedByDisplayName, cancellationToken);
        var approverName = await ResolveDisplayNameAsync(approverUserId, null, cancellationToken);
        var notes = string.IsNullOrWhiteSpace(decisionNotes) ? null : decisionNotes.Trim();

        var payload = BuildPayload(context, requesterName, approverUserId, approverName, notes, status: "Rejected");

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Delete request rejected by {0}.",
            approverName);

        await PublishAsync(
            NotificationKind.ActivityDeleteRejected,
            optedIn,
            payload,
            approverUserId,
            "/Activities/Index",
            $"Delete request rejected — {context.ActivityTitle}",
            summary,
            CreateFingerprint(context.ActivityId, context.RequestId, "rejected"),
            cancellationToken);
    }

    private async Task<HashSet<string>> ResolveApproverRecipientsAsync(CancellationToken cancellationToken)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in ApproverRoles)
        {
            var users = await _userManager.GetUsersInRoleAsync(role);
            foreach (var user in users)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddRecipient(recipients, user.Id);
            }
        }

        return recipients;
    }

    private async Task<HashSet<string>> FilterRecipientsAsync(NotificationKind kind, HashSet<string> recipients, CancellationToken cancellationToken)
    {
        var optedIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var userId in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _preferences.AllowsAsync(kind, userId, projectId: null, cancellationToken))
            {
                optedIn.Add(userId);
            }
        }

        return optedIn;
    }

    private async Task<string> ResolveDisplayNameAsync(string userId, string? fallback, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return "User";
        }

        var user = await _userManager.FindByIdAsync(userId);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
        {
            return userId;
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        return user.UserName ?? userId;
    }

    private async Task PublishAsync(
        NotificationKind kind,
        IReadOnlyCollection<string> recipients,
        ActivityDeleteNotificationPayload payload,
        string actorUserId,
        string route,
        string title,
        string summary,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.PublishAsync(
                kind,
                recipients,
                payload,
                module: "Activities",
                eventType: "ActivityDelete",
                scopeType: "Activity",
                scopeId: payload.ActivityId.ToString(CultureInfo.InvariantCulture),
                projectId: null,
                actorUserId: actorUserId,
                route: route,
                title: title,
                summary: summary,
                fingerprint: fingerprint,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish activity delete notification for request {RequestId}.", payload.RequestId);
        }
    }

    private static ActivityDeleteNotificationPayload BuildPayload(
        ActivityDeleteNotificationContext context,
        string requesterName,
        string? approverUserId,
        string? approverDisplayName,
        string? decisionNotes,
        string status)
    {
        return new ActivityDeleteNotificationPayload(
            context.RequestId,
            context.ActivityId,
            context.ActivityTitle,
            context.ActivityTypeName,
            context.ActivityLocation,
            context.ScheduledStartUtc,
            context.RequestedAtUtc,
            context.RequestedByUserId,
            requesterName,
            context.RequestedByEmail,
            context.Reason,
            approverUserId,
            approverDisplayName,
            decisionNotes,
            status);
    }

    private static void AddRecipient(ISet<string> recipients, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        recipients.Add(userId);
    }

    private static string CreateFingerprint(int activityId, int requestId, string suffix)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "activity-delete:{0}:{1}:{2}",
            activityId,
            requestId,
            suffix);
    }

    private sealed record ActivityDeleteNotificationPayload(
        int RequestId,
        int ActivityId,
        string ActivityTitle,
        string ActivityTypeName,
        string? ActivityLocation,
        DateTimeOffset? ScheduledStartUtc,
        DateTimeOffset RequestedAtUtc,
        string RequestedByUserId,
        string RequestedByDisplayName,
        string? RequestedByEmail,
        string? Reason,
        string? ApproverUserId,
        string? ApproverDisplayName,
        string? DecisionNotes,
        string Status);
}
