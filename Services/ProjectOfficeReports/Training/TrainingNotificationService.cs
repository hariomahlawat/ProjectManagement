using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Services.ProjectOfficeReports.Training;

public sealed class TrainingNotificationService : ITrainingNotificationService
{
    private static readonly string[] ApproverRoles = { "Admin", "HoD" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly ILogger<TrainingNotificationService> _logger;

    public TrainingNotificationService(
        UserManager<ApplicationUser> userManager,
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        ILogger<TrainingNotificationService> logger)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyDeleteRequestedAsync(TrainingDeleteNotificationContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var recipients = await ResolveApproverRecipientsAsync(cancellationToken);
        recipients.RemoveWhere(userId => string.Equals(userId, context.RequestedByUserId, StringComparison.OrdinalIgnoreCase));

        var optedIn = await FilterRecipientsAsync(NotificationKind.TrainingDeleteRequested, recipients, cancellationToken);
        if (optedIn.Count == 0)
        {
            _logger.LogInformation(
                "No recipients for training delete request {RequestId}.",
                context.RequestId);
            return;
        }

        var requesterName = await ResolveDisplayNameAsync(context.RequestedByUserId, cancellationToken);
        var payload = BuildPayload(context, requesterName, null, null, null);

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Delete requested — {0} trainees ({1}). Reason: {2}",
            context.Total.ToString("N0", CultureInfo.InvariantCulture),
            context.TrainingTypeName,
            Truncate(context.Reason, 160));

        await PublishAsync(
            NotificationKind.TrainingDeleteRequested,
            optedIn,
            payload,
            context.RequestedByUserId,
            "/ProjectOfficeReports/Training/Approvals",
            $"Delete requested — {context.TrainingTypeName}",
            summary,
            CreateFingerprint(context.TrainingId, context.RequestId, "requested"),
            cancellationToken);
    }

    public async Task NotifyDeleteApprovedAsync(
        TrainingDeleteNotificationContext context,
        string approverUserId,
        CancellationToken cancellationToken)
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

        var optedIn = await FilterRecipientsAsync(NotificationKind.TrainingDeleteApproved, recipients, cancellationToken);
        if (optedIn.Count == 0)
        {
            _logger.LogInformation(
                "No recipients opted in for delete approval notification {RequestId}.",
                context.RequestId);
            return;
        }

        var requesterName = await ResolveDisplayNameAsync(context.RequestedByUserId, cancellationToken);
        var approverName = await ResolveDisplayNameAsync(approverUserId, cancellationToken);

        var payload = BuildPayload(
            context,
            requesterName,
            approverUserId,
            approverName,
            null,
            status: "Approved");

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Delete request approved by {0} — {1} trainees.",
            approverName,
            context.Total.ToString("N0", CultureInfo.InvariantCulture));

        await PublishAsync(
            NotificationKind.TrainingDeleteApproved,
            optedIn,
            payload,
            approverUserId,
            "/ProjectOfficeReports/Training/Index",
            $"Delete request approved — {context.TrainingTypeName}",
            summary,
            CreateFingerprint(context.TrainingId, context.RequestId, "approved"),
            cancellationToken);
    }

    public async Task NotifyDeleteRejectedAsync(
        TrainingDeleteNotificationContext context,
        string approverUserId,
        string decisionNotes,
        CancellationToken cancellationToken)
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

        var optedIn = await FilterRecipientsAsync(NotificationKind.TrainingDeleteRejected, recipients, cancellationToken);
        if (optedIn.Count == 0)
        {
            _logger.LogInformation(
                "No recipients opted in for delete rejection notification {RequestId}.",
                context.RequestId);
            return;
        }

        var requesterName = await ResolveDisplayNameAsync(context.RequestedByUserId, cancellationToken);
        var approverName = await ResolveDisplayNameAsync(approverUserId, cancellationToken);

        var payload = BuildPayload(
            context,
            requesterName,
            approverUserId,
            approverName,
            decisionNotes,
            status: "Rejected");

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Delete request rejected by {0}.", approverName);

        await PublishAsync(
            NotificationKind.TrainingDeleteRejected,
            optedIn,
            payload,
            approverUserId,
            "/ProjectOfficeReports/Training/Manage?id=" + context.TrainingId,
            $"Delete request rejected — {context.TrainingTypeName}",
            summary,
            CreateFingerprint(context.TrainingId, context.RequestId, "rejected"),
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

    private async Task<HashSet<string>> FilterRecipientsAsync(
        NotificationKind kind,
        HashSet<string> candidates,
        CancellationToken cancellationToken)
    {
        var optedIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _preferences.AllowsAsync(kind, candidate, projectId: null, cancellationToken))
            {
                optedIn.Add(candidate);
            }
        }

        return optedIn;
    }

    private static void AddRecipient(ISet<string> recipients, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            recipients.Add(userId);
        }
    }

    private async Task<string> ResolveDisplayNameAsync(string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "Unknown";
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return userId;
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName!;
        }

        return string.IsNullOrWhiteSpace(user.UserName) ? user.Id : user.UserName!;
    }

    private TrainingDeleteNotificationPayload BuildPayload(
        TrainingDeleteNotificationContext context,
        string requesterName,
        string? decisionByUserId,
        string? decisionByName,
        string? decisionNotes,
        string status = "Pending")
    {
        var period = FormatPeriod(context);
        var officers = context.Officers.ToString("N0", CultureInfo.InvariantCulture);
        var jcos = context.JuniorCommissionedOfficers.ToString("N0", CultureInfo.InvariantCulture);
        var ors = context.OtherRanks.ToString("N0", CultureInfo.InvariantCulture);

        return new TrainingDeleteNotificationPayload(
            context.TrainingId,
            context.RequestId,
            context.TrainingTypeName,
            period,
            context.Total,
            officers,
            jcos,
            ors,
            context.Total.ToString("N0", CultureInfo.InvariantCulture),
            context.RequestedByUserId,
            requesterName,
            context.RequestedAtUtc,
            context.Reason,
            status,
            decisionByUserId,
            decisionByName,
            decisionNotes,
            DateTimeOffset.UtcNow);
    }

    private async Task PublishAsync(
        NotificationKind kind,
        IReadOnlyCollection<string> recipients,
        TrainingDeleteNotificationPayload payload,
        string actorUserId,
        string route,
        string title,
        string summary,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        await _publisher.PublishAsync(
            kind,
            recipients,
            payload,
            module: "TrainingTracker",
            eventType: "TrainingDelete",
            scopeType: "Training",
            scopeId: payload.TrainingId.ToString(),
            projectId: null,
            actorUserId: actorUserId,
            route: route,
            title: title,
            summary: summary,
            fingerprint: fingerprint,
            cancellationToken: cancellationToken);
    }

    private static string FormatPeriod(TrainingDeleteNotificationContext context)
    {
        if (context.StartDate.HasValue || context.EndDate.HasValue)
        {
            var start = context.StartDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "(not set)";
            var end = context.EndDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? start;
            return start == end ? start : $"{start} – {end}";
        }

        if (context.TrainingMonth.HasValue && context.TrainingYear.HasValue)
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(context.TrainingMonth.Value);
            return $"{monthName} {context.TrainingYear.Value}";
        }

        return "(unspecified)";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    private static string CreateFingerprint(Guid trainingId, Guid requestId, string suffix)
        => string.Format(
            CultureInfo.InvariantCulture,
            "training:{0}:delete:{1}:{2}",
            trainingId,
            requestId,
            suffix);

    private sealed record TrainingDeleteNotificationPayload(
        Guid TrainingId,
        Guid RequestId,
        string TrainingTypeName,
        string Period,
        int Total,
        string Officers,
        string JuniorCommissionedOfficers,
        string OtherRanks,
        string TotalFormatted,
        string RequestedByUserId,
        string RequestedByName,
        DateTimeOffset RequestedAtUtc,
        string Reason,
        string Status,
        string? DecisionByUserId,
        string? DecisionByName,
        string? DecisionNotes,
        DateTimeOffset PublishedAtUtc);
}
