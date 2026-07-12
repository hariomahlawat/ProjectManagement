using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskNotificationService : IActionTaskNotificationService
{
    // SECTION: Notification metadata constants
    private const string ModuleName = "ActionTracker";
    private const string ScopeType = "ActionTask";
    private const int TitlePreviewLength = 80;

    private readonly INotificationPublisher _publisher;
    private readonly INotificationPreferenceService _preferences;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IClock _clock;
    private readonly ILogger<ActionTaskNotificationService> _logger;

    public ActionTaskNotificationService(
        INotificationPublisher publisher,
        INotificationPreferenceService preferences,
        UserManager<ApplicationUser> userManager,
        IClock clock,
        ILogger<ActionTaskNotificationService> logger)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // SECTION: Intent-specific notification APIs
    public Task NotifyTaskAssignedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskAssigned,
            task,
            actorUserId,
            "ActionTaskAssigned",
            "New task assigned",
            $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} has been assigned to you.",
            BuildAssignedFingerprint(task),
            recipients =>
            {
                AddRecipient(recipients, task.AssignedToUserId);
                return Task.CompletedTask;
            },
            previousStatus: null,
            currentStatus: task.Status,
            dueDate: null,
            cancellationToken: cancellationToken);

    public Task NotifyProgressUpdatedAsync(ActionTaskItem task, ActionTaskUpdate? update, string actorUserId, CancellationToken cancellationToken = default)
    {
        var isConferenceRemark = string.Equals(
            update?.UpdateType,
            ActionTaskUpdateTypes.Conference,
            StringComparison.OrdinalIgnoreCase);

        return PublishForTaskAsync(
            NotificationKind.ActionTaskProgressUpdated,
            task,
            actorUserId,
            isConferenceRemark ? "ActionTaskConferenceRemarkAdded" : "ActionTaskProgressUpdated",
            isConferenceRemark ? "Conference direction added" : "Task progress updated",
            isConferenceRemark
                ? $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} has a new conference direction."
                : $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} has a new progress update.",
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:update:{1}", task.Id, update?.Id.ToString(CultureInfo.InvariantCulture) ?? DateTimeOffset.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)),
            recipients =>
            {
                AddRecipient(recipients, task.CreatedByUserId);
                AddRecipient(recipients, task.AssignedToUserId);
                return Task.CompletedTask;
            },
            previousStatus: null,
            currentStatus: task.Status,
            dueDate: null,
            cancellationToken: cancellationToken);
    }

    public Task NotifyStatusChangedAsync(ActionTaskItem task, string previousStatus, string newStatus, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskStatusChanged,
            task,
            actorUserId,
            "ActionTaskStatusChanged",
            "Task status updated",
            string.Format(CultureInfo.InvariantCulture, "{0} moved from {1} to {2}.", BuildTaskReference(task), previousStatus, newStatus),
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:status:{1}:{2}", task.Id, NormalizeFingerprintPart(newStatus), DateTimeOffset.UtcNow.Ticks),
            recipients =>
            {
                AddRecipient(recipients, task.CreatedByUserId);
                AddRecipient(recipients, task.AssignedToUserId);
                return Task.CompletedTask;
            },
            previousStatus: previousStatus,
            currentStatus: newStatus,
            dueDate: null,
            cancellationToken: cancellationToken);

    public Task NotifyTaskBlockedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskBlocked,
            task,
            actorUserId,
            "ActionTaskBlocked",
            "Task blocked",
            $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} has been marked blocked.",
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:blocked:{1}", task.Id, DateTimeOffset.UtcNow.Ticks),
            async recipients =>
            {
                AddRecipient(recipients, task.CreatedByUserId);
                AddRecipient(recipients, task.AssignedToUserId);
                await AddRoleRecipientsAsync(recipients, RoleNames.HoD, cancellationToken);
                await AddRoleRecipientsAsync(recipients, RoleNames.Comdt, cancellationToken);
            },
            previousStatus: null,
            currentStatus: ActionTaskStatuses.Blocked,
            dueDate: null,
            cancellationToken: cancellationToken);

    public Task NotifySubmittedForClosureAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskSubmittedForClosure,
            task,
            actorUserId,
            "ActionTaskSubmittedForClosure",
            "Task submitted for closure",
            $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} has been submitted for closure.",
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:submitted:{1}", task.Id, (task.SubmittedOn ?? DateTime.UtcNow).Ticks),
            async recipients =>
            {
                AddRecipient(recipients, task.CreatedByUserId);
                await AddRoleRecipientsAsync(recipients, RoleNames.HoD, cancellationToken);
                await AddRoleRecipientsAsync(recipients, RoleNames.Comdt, cancellationToken);
            },
            previousStatus: null,
            currentStatus: ActionTaskStatuses.Submitted,
            dueDate: null,
            cancellationToken: cancellationToken);

    public Task NotifyTaskClosedAsync(ActionTaskItem task, string actorUserId, bool closedByCommandAuthority, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskClosed,
            task,
            actorUserId,
            "ActionTaskClosed",
            "Task closed",
            closedByCommandAuthority
                ? $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} was closed by command authority."
                : $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} has been closed.",
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:closed:{1}", task.Id, (task.ClosedOn ?? DateTime.UtcNow).Ticks),
            recipients =>
            {
                AddRecipient(recipients, task.AssignedToUserId);
                AddRecipient(recipients, task.CreatedByUserId);
                return Task.CompletedTask;
            },
            previousStatus: null,
            currentStatus: ActionTaskStatuses.Closed,
            dueDate: null,
            cancellationToken: cancellationToken);

    public Task NotifyDueDateChangedAsync(ActionTaskItem task, DateTime oldDate, DateTime newDate, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskDueDateChanged,
            task,
            actorUserId,
            "ActionTaskDueDateChanged",
            "Task due date changed",
            string.Format(CultureInfo.InvariantCulture, "{0} due date changed from {1} to {2}.", BuildTaskReference(task), FormatDisplayDate(oldDate), FormatDisplayDate(newDate)),
            BuildDueDateFingerprint(task, oldDate, newDate),
            recipients =>
            {
                AddRecipient(recipients, task.AssignedToUserId);
                AddRecipient(recipients, task.CreatedByUserId);
                return Task.CompletedTask;
            },
            previousStatus: null,
            currentStatus: task.Status,
            dueDate: FormatDisplayDate(newDate),
            cancellationToken: cancellationToken);

    public Task NotifyMovedToBacklogAsync(ActionTaskItem task, string? previousAssigneeUserId, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskMovedToBacklog,
            task,
            actorUserId,
            "ActionTaskMovedToBacklog",
            "Task moved to backlog",
            $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} was moved to backlog.",
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:backlog:{1}", task.Id, DateTimeOffset.UtcNow.Ticks),
            recipients =>
            {
                AddRecipient(recipients, previousAssigneeUserId);
                AddRecipient(recipients, task.CreatedByUserId);
                return Task.CompletedTask;
            },
            previousStatus: null,
            currentStatus: ActionTaskStatuses.Backlog,
            dueDate: null,
            cancellationToken: cancellationToken);

    public Task NotifyRemovedFromSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskRemovedFromSprint,
            task,
            actorUserId,
            "ActionTaskRemovedFromSprint",
            "Task removed from sprint",
            $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} was removed from the sprint but remains assigned.",
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:removed-from-sprint:{1}", task.Id, DateTimeOffset.UtcNow.Ticks),
            recipients =>
            {
                AddRecipient(recipients, task.AssignedToUserId);
                AddRecipient(recipients, task.CreatedByUserId);
                return Task.CompletedTask;
            },
            previousStatus: null,
            currentStatus: task.Status,
            dueDate: null,
            cancellationToken: cancellationToken);

    public Task NotifyAddedToSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
        => PublishForTaskAsync(
            NotificationKind.ActionTaskAddedToSprint,
            task,
            actorUserId,
            "ActionTaskAddedToSprint",
            "Task added to sprint",
            $"{BuildTaskReference(task)} - {BuildTitlePreview(task)} has been added to a sprint.",
            string.Format(CultureInfo.InvariantCulture, "action-task:{0}:added-to-sprint:{1}:{2}", task.Id, task.SprintId?.ToString(CultureInfo.InvariantCulture) ?? "none", DateTimeOffset.UtcNow.Ticks),
            recipients =>
            {
                AddRecipient(recipients, task.AssignedToUserId);
                AddRecipient(recipients, task.CreatedByUserId);
                return Task.CompletedTask;
            },
            previousStatus: null,
            currentStatus: task.Status,
            dueDate: null,
            cancellationToken: cancellationToken);

    // SECTION: Publishing pipeline
    private async Task PublishForTaskAsync(
        NotificationKind kind,
        ActionTaskItem task,
        string actorUserId,
        string eventType,
        string title,
        string summary,
        string fingerprint,
        Func<ISet<string>, Task> resolveRecipients,
        string? previousStatus,
        string? currentStatus,
        string? dueDate,
        CancellationToken cancellationToken)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        try
        {
            var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await resolveRecipients(recipients);
            RemoveActor(recipients, actorUserId);

            if (recipients.Count == 0)
            {
                _logger.LogInformation("No Action Tracker notification recipients for task {TaskId}.", task.Id);
                return;
            }

            var allowed = await FilterRecipientsAsync(kind, recipients, cancellationToken);
            if (allowed.Count == 0)
            {
                _logger.LogInformation("All Action Tracker notification recipients opted out for task {TaskId}.", task.Id);
                return;
            }

            var payload = new ActionTaskNotificationPayload(
                task.Id,
                BuildTaskReference(task),
                task.Title,
                previousStatus,
                currentStatus,
                dueDate,
                actorUserId);

            await _publisher.PublishAsync(
                kind,
                allowed,
                payload,
                module: ModuleName,
                eventType: eventType,
                scopeType: ScopeType,
                scopeId: task.Id.ToString(CultureInfo.InvariantCulture),
                projectId: null,
                actorUserId: actorUserId,
                route: BuildTaskRoute(task.Id),
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
            _logger.LogError(ex, "Failed to publish Action Tracker notification {Kind} for task {TaskId}.", kind, task.Id);
        }
    }

    private async Task<IReadOnlyCollection<string>> FilterRecipientsAsync(NotificationKind kind, IEnumerable<string> recipients, CancellationToken cancellationToken)
    {
        var allowed = new List<string>();
        foreach (var recipient in recipients.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            if (await _preferences.AllowsAsync(kind, recipient, projectId: null, cancellationToken))
            {
                allowed.Add(recipient);
            }
        }

        return allowed;
    }

    // SECTION: Recipient helpers
    private static void AddRecipient(ISet<string> recipients, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            recipients.Add(userId);
        }
    }

    private static void RemoveActor(ISet<string> recipients, string actorUserId)
    {
        if (!string.IsNullOrWhiteSpace(actorUserId))
        {
            recipients.Remove(actorUserId);
        }
    }

    private async Task AddRoleRecipientsAsync(ISet<string> recipients, string role, CancellationToken cancellationToken)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddRecipient(recipients, user.Id);
        }
    }

    // SECTION: Formatting helpers
    private static string BuildTaskReference(ActionTaskItem task)
        => string.Format(CultureInfo.InvariantCulture, "AT-{0}", task.Id);

    private static string BuildTaskRoute(int taskId)
        => string.Format(CultureInfo.InvariantCulture, "/ActionTasks?ViewMode=Register&TaskScope=All&TaskId={0}", taskId);

    private static string BuildTitlePreview(ActionTaskItem task)
    {
        var title = string.IsNullOrWhiteSpace(task.Title) ? BuildTaskReference(task) : task.Title.Trim();
        return title.Length <= TitlePreviewLength ? title : string.Concat(title.AsSpan(0, TitlePreviewLength - 1), "…");
    }

    private static string FormatDisplayDate(DateTime value)
        => value.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static string NormalizeFingerprintPart(string value)
        => Uri.EscapeDataString(value.Trim().ToLowerInvariant().Replace(' ', '-'));

    private static string BuildAssignedFingerprint(ActionTaskItem task)
        => string.Format(
            CultureInfo.InvariantCulture,
            "action-task:{0}:assigned:{1}:{2}",
            task.Id,
            NormalizeFingerprintPart(task.AssignedToUserId),
            ResolveMutationVersion(task, task.AssignedOn));

    private string BuildDueDateFingerprint(ActionTaskItem task, DateTime oldDate, DateTime newDate)
        => string.Format(
            CultureInfo.InvariantCulture,
            "action-task:{0}:due:{1:yyyyMMdd}:{2:yyyyMMdd}:{3}",
            task.Id,
            oldDate.Date,
            newDate.Date,
            ResolveMutationVersion(task, _clock.UtcNow.UtcDateTime));

    private static string ResolveMutationVersion(ActionTaskItem task, DateTime fallbackTimestamp)
    {
        if (task.RowVersion is { Length: > 0 })
        {
            return Convert.ToHexString(task.RowVersion).ToLowerInvariant();
        }

        var utc = fallbackTimestamp.Kind switch
        {
            DateTimeKind.Utc => fallbackTimestamp,
            DateTimeKind.Local => fallbackTimestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(fallbackTimestamp, DateTimeKind.Utc)
        };
        return utc.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record ActionTaskNotificationPayload(
        int TaskId,
        string TaskReference,
        string Title,
        string? PreviousStatus,
        string? CurrentStatus,
        string? DueDate,
        string? ActorUserId);
}
