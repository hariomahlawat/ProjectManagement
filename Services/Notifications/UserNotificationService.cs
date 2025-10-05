using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Notifications;

public sealed class UserNotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public UserNotificationService(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NotificationListItem>> ListAsync(
        ClaimsPrincipal principal,
        string userId,
        NotificationListOptions options,
        CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required to query notifications.", nameof(userId));
        }

        options ??= new NotificationListOptions();

        var limit = Math.Clamp(options.Limit ?? 20, 1, 200);

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId);

        if (options.OnlyUnread)
        {
            query = query.Where(n => n.ReadUtc == null);
        }

        if (options.ProjectId.HasValue)
        {
            query = query.Where(n => n.ProjectId == options.ProjectId);
        }

        query = query.OrderByDescending(n => n.CreatedUtc)
                     .Take(limit);

        var notifications = await query.ToListAsync(cancellationToken);

        return await ProjectAsync(principal, userId, notifications, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationListItem>> ProjectAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<Notification> notifications,
        CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required to query notifications.", nameof(userId));
        }

        if (notifications is null)
        {
            throw new ArgumentNullException(nameof(notifications));
        }

        if (notifications.Count == 0)
        {
            return Array.Empty<NotificationListItem>();
        }

        var projectIds = notifications
            .Where(n => n.ProjectId.HasValue)
            .Select(n => n.ProjectId!.Value)
            .Distinct()
            .ToList();

        var accessibleProjects = await GetAccessibleProjectsAsync(principal, userId, projectIds, cancellationToken);

        var mutedProjects = await _db.UserProjectMutes
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ProjectId)
            .ToListAsync(cancellationToken);

        var mutedProjectSet = mutedProjects.Count == 0
            ? null
            : new HashSet<int>(mutedProjects);

        var results = new List<NotificationListItem>(notifications.Count);

        foreach (var notification in notifications)
        {
            string? projectName = null;
            if (notification.ProjectId is int projectId)
            {
                if (!accessibleProjects.TryGetValue(projectId, out projectName))
                {
                    continue;
                }
            }

            var isMuted = notification.ProjectId is int pid && mutedProjectSet?.Contains(pid) == true;
            var normalizedRoute = NotificationPublisher.NormalizeRouteSegments(notification.Route);

            results.Add(new NotificationListItem(
                notification.Id,
                notification.Module,
                notification.EventType,
                notification.ScopeType,
                notification.ScopeId,
                notification.ProjectId,
                projectName,
                notification.ActorUserId,
                normalizedRoute,
                notification.Title,
                notification.Summary,
                notification.CreatedUtc,
                notification.SeenUtc,
                notification.ReadUtc,
                isMuted));
        }

        return results;
    }

    public async Task<int> CountUnreadAsync(
        ClaimsPrincipal principal,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required to query notifications.", nameof(userId));
        }

        var unreadNotifications = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId && n.ReadUtc == null)
            .Select(n => new { n.Id, n.ProjectId })
            .ToListAsync(cancellationToken);

        if (unreadNotifications.Count == 0)
        {
            return 0;
        }

        var projectIds = unreadNotifications
            .Where(n => n.ProjectId.HasValue)
            .Select(n => n.ProjectId!.Value)
            .Distinct()
            .ToList();

        var accessibleProjects = await GetAccessibleProjectsAsync(principal, userId, projectIds, cancellationToken);

        var count = 0;
        foreach (var notification in unreadNotifications)
        {
            if (notification.ProjectId is int projectId && !accessibleProjects.ContainsKey(projectId))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public Task<NotificationOperationResult> MarkReadAsync(
        ClaimsPrincipal principal,
        string userId,
        int notificationId,
        CancellationToken cancellationToken = default)
        => UpdateReadStateAsync(principal, userId, notificationId, true, cancellationToken);

    public Task<NotificationOperationResult> MarkUnreadAsync(
        ClaimsPrincipal principal,
        string userId,
        int notificationId,
        CancellationToken cancellationToken = default)
        => UpdateReadStateAsync(principal, userId, notificationId, false, cancellationToken);

    public async Task<NotificationOperationResult> SetProjectMuteAsync(
        ClaimsPrincipal principal,
        string userId,
        int projectId,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required to update notification mutes.", nameof(userId));
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotificationOperationResult.NotFound;
        }

        if (!ProjectAccessGuard.CanViewProject(project, principal, userId))
        {
            return NotificationOperationResult.Forbidden;
        }

        var existingMute = await _db.UserProjectMutes
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);

        if (muted)
        {
            if (existingMute is null)
            {
                _db.UserProjectMutes.Add(new UserProjectMute
                {
                    ProjectId = projectId,
                    UserId = userId,
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            if (existingMute is not null)
            {
                _db.UserProjectMutes.Remove(existingMute);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return NotificationOperationResult.Success;
    }

    private async Task<NotificationOperationResult> UpdateReadStateAsync(
        ClaimsPrincipal principal,
        string userId,
        int notificationId,
        bool markRead,
        CancellationToken cancellationToken)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required to update notifications.", nameof(userId));
        }

        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId, cancellationToken);

        if (notification is null)
        {
            return NotificationOperationResult.NotFound;
        }

        if (notification.ProjectId is int projectId)
        {
            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

            if (project is null || !ProjectAccessGuard.CanViewProject(project, principal, userId))
            {
                return NotificationOperationResult.Forbidden;
            }
        }

        if (markRead)
        {
            if (notification.ReadUtc is null)
            {
                var now = _clock.UtcNow.UtcDateTime;
                notification.ReadUtc = now;
                notification.SeenUtc ??= now;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            if (notification.ReadUtc is not null)
            {
                notification.ReadUtc = null;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return NotificationOperationResult.Success;
    }

    private async Task<Dictionary<int, string>> GetAccessibleProjectsAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var accessible = new Dictionary<int, string>();

        foreach (var project in projects)
        {
            if (ProjectAccessGuard.CanViewProject(project, principal, userId))
            {
                accessible[project.Id] = project.Name;
            }
        }

        return accessible;
    }
}

public sealed class NotificationListOptions
{
    public int? Limit { get; set; }

    public bool OnlyUnread { get; set; }

    public int? ProjectId { get; set; }
}

public enum NotificationOperationResult
{
    Success,
    NotFound,
    Forbidden,
}
