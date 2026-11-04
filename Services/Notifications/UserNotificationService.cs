using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Notifications;

public sealed class UserNotificationService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 120;

    private static readonly NotificationKind[] SuppressedSelfNotificationKinds =
    {
        NotificationKind.StageStatusChanged,
        NotificationKind.DocumentPublished,
        NotificationKind.DocumentReplaced,
        NotificationKind.DocumentArchived,
        NotificationKind.DocumentRestored,
        NotificationKind.DocumentDeleted,
    };

    private static readonly NotificationKind[] ActionRequiredKinds =
        NotificationPresentationCatalog.ActionRequiredKinds.ToArray();

    private static readonly NotificationKind[] ApprovalKinds =
    {
        NotificationKind.PlanSubmitted,
        NotificationKind.PlanApproved,
        NotificationKind.PlanRejected,
        NotificationKind.PlanWithdrawn,
        NotificationKind.PlanSuperseded,
        NotificationKind.TrainingDeleteRequested,
        NotificationKind.TrainingDeleteApproved,
        NotificationKind.TrainingDeleteRejected,
        NotificationKind.ActivityDeleteRequested,
        NotificationKind.ActivityDeleteApproved,
        NotificationKind.ActivityDeleteRejected,
    };

    private static readonly NotificationKind[] CollaborationKinds =
    {
        NotificationKind.RemarkCreated,
        NotificationKind.MentionedInRemark,
        NotificationKind.NotebookShared,
        NotificationKind.NotebookAccessRemoved,
        NotificationKind.NotebookCollaborationLeft,
        NotificationKind.NotebookAccessChanged,
    };

    private static readonly NotificationKind[] AssignmentKinds =
    {
        NotificationKind.StageAssigned,
        NotificationKind.RoleAssignmentsChanged,
        NotificationKind.ProjectAssignmentChanged,
    };

    private static readonly NotificationKind[] LifecycleKinds =
    {
        NotificationKind.StageStatusChanged,
        NotificationKind.StageAssigned,
    };

    private static readonly NotificationKind[] DocumentKinds =
    {
        NotificationKind.DocumentPublished,
        NotificationKind.DocumentReplaced,
        NotificationKind.DocumentArchived,
        NotificationKind.DocumentRestored,
        NotificationKind.DocumentDeleted,
    };

    private static readonly NotificationKind[] ActionTaskKinds =
    {
        NotificationKind.ActionTaskAssigned,
        NotificationKind.ActionTaskProgressUpdated,
        NotificationKind.ActionTaskStatusChanged,
        NotificationKind.ActionTaskBlocked,
        NotificationKind.ActionTaskSubmittedForClosure,
        NotificationKind.ActionTaskClosed,
        NotificationKind.ActionTaskDueDateChanged,
        NotificationKind.ActionTaskMovedToBacklog,
        NotificationKind.ActionTaskRemovedFromSprint,
        NotificationKind.ActionTaskAddedToSprint,
    };

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public UserNotificationService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<NotificationListItem>> ListAsync(
        ClaimsPrincipal principal,
        string userId,
        NotificationListOptions options,
        CancellationToken cancellationToken = default)
    {
        var page = await ListPageAsync(principal, userId, options, cancellationToken);
        return page.Items;
    }

    public async Task<NotificationPageDto> ListPageAsync(
        ClaimsPrincipal principal,
        string userId,
        NotificationListOptions? options,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);
        options ??= new NotificationListOptions();

        var limit = Math.Clamp(options.Limit ?? DefaultPageSize, 1, MaximumPageSize);
        var projectMap = await GetAccessibleProjectMapAsync(principal, userId, cancellationToken);
        var accessibleProjectIds = projectMap.Keys.ToArray();
        var mutedProjectIds = await GetMutedProjectIdsAsync(userId, cancellationToken);

        var query = BuildAccessibleQuery(userId, accessibleProjectIds);
        query = ApplyFilters(query, options, projectMap, mutedProjectIds);

        var totalCount = await query.CountAsync(cancellationToken);

        // Priority ordering serves the bell's first page only. Cursor paging is defined over the
        // chronological order, so a cursor always pages chronologically.
        var hasCursor = NotificationCursor.TryDecode(options.Cursor, out var cursor);
        var actionRequiredFirst = options.ActionRequiredFirst && !hasCursor;

        if (hasCursor)
        {
            query = query.Where(n =>
                n.CreatedUtc < cursor.CreatedUtc
                || (n.CreatedUtc == cursor.CreatedUtc && n.Id < cursor.Id));
        }

        var ordered = actionRequiredFirst
            ? query
                .OrderByDescending(n => n.ReadUtc == null
                    && n.Kind.HasValue
                    && ActionRequiredKinds.Contains(n.Kind.Value))
                .ThenByDescending(n => n.CreatedUtc)
                .ThenByDescending(n => n.Id)
            : query
                .OrderByDescending(n => n.CreatedUtc)
                .ThenByDescending(n => n.Id);

        // One aggregate for the badge, the unread header, the seen-all boundary and the list
        // version. It runs BEFORE the rows are read on purpose: the version may then lag the rows
        // but can never include a row the page is missing. A change between the two reads makes
        // the client's next version check disagree and reload; reading in the other order could
        // cache a version that already counts a row the client never received. The seen-all
        // boundary matters because a priority-ordered or filtered page can omit newer rows, so the
        // bell must not derive it from the rows it shows.
        var summary = await SummarizeAsync(
            userId,
            accessibleProjectIds,
            mutedProjectIds,
            cancellationToken);
        var unreadCount = summary.UnreadCount;
        var unseenCount = summary.UnseenCount;
        var latestNotificationId = summary.LatestNotificationId;
        var listVersion = summary.ListVersion;

        var rows = await ordered
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var items = await ProjectNotificationsAsync(
            rows,
            userId,
            projectMap,
            mutedProjectIds,
            NotificationGrouping.CombinesRemarksAcrossProjects(principal),
            cancellationToken);
        var filterOptions = options.IncludeFilterOptions
            ? await GetFilterOptionsAsync(
                userId,
                accessibleProjectIds,
                projectMap,
                mutedProjectIds,
                cancellationToken)
            : NotificationFilterOptions.Empty;
        var folderCounts = options.IncludeFilterOptions
            ? await GetFolderCountsAsync(
                userId,
                accessibleProjectIds,
                mutedProjectIds,
                cancellationToken)
            : Array.Empty<NotificationFolderCountDto>();

        // A priority-ordered page has no chronological boundary to continue from.
        var nextCursor = hasMore && rows.Count > 0 && !actionRequiredFirst
            ? NotificationCursor.Encode(rows[^1].CreatedUtc, rows[^1].Id)
            : null;

        return new NotificationPageDto(
            items,
            totalCount,
            unreadCount,
            nextCursor,
            hasMore,
            filterOptions.Projects,
            filterOptions.Modules,
            folderCounts,
            unseenCount,
            latestNotificationId,
            listVersion);
    }

    public async Task<IReadOnlyList<NotificationListItem>> ProjectAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<Notification> notifications,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);
        ArgumentNullException.ThrowIfNull(notifications);

        if (notifications.Count == 0)
        {
            return Array.Empty<NotificationListItem>();
        }

        var projectIds = notifications
            .Where(notification => notification.ProjectId.HasValue)
            .Select(notification => notification.ProjectId!.Value)
            .Distinct()
            .ToArray();
        var accessibleProjects = await GetAccessibleProjectsAsync(
            principal,
            userId,
            projectIds,
            cancellationToken);
        var mutedProjectIds = await GetMutedProjectIdsAsync(userId, cancellationToken);

        return await ProjectNotificationsAsync(
            notifications
                .OrderByDescending(row => row.CreatedUtc)
                .ThenByDescending(row => row.Id)
                .ToArray(),
            userId,
            accessibleProjects,
            mutedProjectIds,
            NotificationGrouping.CombinesRemarksAcrossProjects(principal),
            cancellationToken);
    }

    private async Task<IReadOnlyList<NotificationListItem>> ProjectNotificationsAsync(
        IReadOnlyCollection<Notification> notifications,
        string userId,
        IReadOnlyDictionary<int, string> accessibleProjects,
        IReadOnlyCollection<int> mutedProjectIds,
        bool combineRemarksAcrossProjects,
        CancellationToken cancellationToken)
    {
        if (notifications.Count == 0)
        {
            return Array.Empty<NotificationListItem>();
        }

        var actorIds = notifications
            .Select(notification => notification.ActorUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, string> actorNames;
        if (actorIds.Length == 0)
        {
            actorNames = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var actors = await _db.Users
                .AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    user.FullName,
                    user.UserName,
                    user.Email,
                })
                .ToListAsync(cancellationToken);

            actorNames = actors.ToDictionary(
                user => user.Id,
                user => FirstNonEmpty(user.FullName, user.UserName, user.Email, "User"),
                StringComparer.Ordinal);
        }

        // Rows are projected in the order supplied: ListPageAsync passes them already ordered
        // (chronologically or priority-first); ProjectAsync orders its ad-hoc set itself.
        var results = new List<NotificationListItem>(notifications.Count);
        foreach (var notification in notifications)
        {
            if (string.Equals(notification.ActorUserId, userId, StringComparison.Ordinal)
                && notification.Kind.HasValue
                && SuppressedSelfNotificationKinds.Contains(notification.Kind.Value))
            {
                continue;
            }

            string? projectName = null;
            if (notification.ProjectId is int projectId
                && !accessibleProjects.TryGetValue(projectId, out projectName))
            {
                continue;
            }

            var presentation = NotificationPresentationCatalog.Resolve(
                notification.Kind,
                notification.Module,
                notification.EventType);
            var content = NotificationContentFormatter.Format(
                notification.Kind,
                notification.Module,
                notification.EventType,
                notification.ScopeId,
                projectName,
                notification.Title,
                notification.Summary);
            actorNames.TryGetValue(notification.ActorUserId ?? string.Empty, out var actorDisplayName);

            results.Add(new NotificationListItem(
                notification.Id,
                notification.Module,
                notification.EventType,
                notification.ScopeType,
                notification.ScopeId,
                notification.ProjectId,
                projectName,
                notification.ActorUserId,
                NotificationPublisher.NormalizeRouteSegments(notification.Route),
                content.Title,
                content.Summary,
                EnsureUtc(notification.CreatedUtc),
                TimeFmt.ToIst(EnsureUtc(notification.CreatedUtc)),
                notification.SeenUtc.HasValue ? EnsureUtc(notification.SeenUtc.Value) : null,
                notification.ReadUtc.HasValue ? EnsureUtc(notification.ReadUtc.Value) : null,
                notification.ProjectId is int mutedProjectId && mutedProjectIds.Contains(mutedProjectId),
                notification.Kind?.ToString(),
                actorDisplayName,
                presentation.Category,
                presentation.IconCssClass,
                presentation.Priority,
                presentation.IsActionRequired,
                notification.DeliveredUtc.HasValue ? EnsureUtc(notification.DeliveredUtc.Value) : null,
                content.SummaryTooltip,
                NotificationGrouping.ResolveGroupKey(
                    notification.Kind,
                    presentation,
                    notification.ProjectId,
                    EnsureUtc(notification.CreatedUtc),
                    combineRemarksAcrossProjects)));
        }

        return results;
    }

    public async Task<int> CountUnreadAsync(
        ClaimsPrincipal principal,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);

        var projectMap = await GetAccessibleProjectMapAsync(principal, userId, cancellationToken);
        var accessibleProjectIds = projectMap.Keys.ToArray();
        var mutedProjectIds = await GetMutedProjectIdsAsync(userId, cancellationToken);

        return await CountUnreadInternalAsync(
            userId,
            accessibleProjectIds,
            mutedProjectIds,
            cancellationToken);
    }

    /// <summary>
    /// Counts notifications the recipient has not yet seen. This drives the bell badge: opening
    /// the bell marks items seen, so the badge reflects what is new since the last look rather
    /// than every item that was never individually opened.
    /// </summary>
    public async Task<int> CountUnseenAsync(
        ClaimsPrincipal principal,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);

        var projectMap = await GetAccessibleProjectMapAsync(principal, userId, cancellationToken);
        var mutedProjectIds = await GetMutedProjectIdsAsync(userId, cancellationToken);

        return await CountUnseenInternalAsync(
            userId,
            projectMap.Keys.ToArray(),
            mutedProjectIds,
            cancellationToken);
    }

    /// <summary>
    /// Returns the unread and unseen counts together for callers that publish both, avoiding a
    /// second accessible-project lookup.
    /// </summary>
    public async Task<NotificationCounts> CountAsync(
        ClaimsPrincipal principal,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);

        var projectMap = await GetAccessibleProjectMapAsync(principal, userId, cancellationToken);
        var mutedProjectIds = await GetMutedProjectIdsAsync(userId, cancellationToken);
        var summary = await SummarizeAsync(
            userId,
            projectMap.Keys.ToArray(),
            mutedProjectIds,
            cancellationToken);

        return new NotificationCounts(
            summary.UnreadCount,
            summary.UnseenCount,
            summary.LatestNotificationId,
            summary.ListVersion);
    }

    /// <summary>
    /// Unread count, unseen (badge) count and newest id over the accessible, unmuted rows, in a
    /// single round trip. The predicates match <see cref="CountUnreadInternalAsync"/> and
    /// <see cref="CountUnseenInternalAsync"/>.
    /// </summary>
    private async Task<NotificationSummary> SummarizeAsync(
        string userId,
        IReadOnlyCollection<int> accessibleProjectIds,
        IReadOnlyCollection<int> mutedProjectIds,
        CancellationToken cancellationToken)
    {
        var summary = await FilterMuted(
                BuildAccessibleQuery(userId, accessibleProjectIds),
                mutedProjectIds,
                onlyMuted: false)
            .GroupBy(_ => 1)
            .Select(rows => new NotificationSummary(
                rows.Count(n => n.ReadUtc == null),
                rows.Count(n => n.SeenUtc == null && n.ReadUtc == null),
                rows.Max(n => (int?)n.Id),
                rows.Count(),
                rows.Sum(n => n.ReadUtc == null ? (long)n.Id : 0L),
                rows.Sum(n => n.SeenUtc == null && n.ReadUtc == null ? (long)n.Id : 0L)))
            .FirstOrDefaultAsync(cancellationToken);

        return summary ?? new NotificationSummary(0, 0, null, 0, 0, 0);
    }

    private async Task<int> CountUnseenInternalAsync(
        string userId,
        IReadOnlyCollection<int> accessibleProjectIds,
        IReadOnlyCollection<int> mutedProjectIds,
        CancellationToken cancellationToken)
    {
        // A read notification has been looked at even if a legacy path never stamped SeenUtc.
        var query = FilterMuted(
            BuildAccessibleQuery(userId, accessibleProjectIds)
                .Where(notification => notification.SeenUtc == null && notification.ReadUtc == null),
            mutedProjectIds,
            onlyMuted: false);

        return await query.CountAsync(cancellationToken);
    }

    private async Task<int> CountUnreadInternalAsync(
        string userId,
        IReadOnlyCollection<int> accessibleProjectIds,
        IReadOnlyCollection<int> mutedProjectIds,
        CancellationToken cancellationToken)
    {
        var query = BuildAccessibleQuery(userId, accessibleProjectIds)
            .Where(notification => notification.ReadUtc == null);

        if (mutedProjectIds.Count > 0)
        {
            query = query.Where(notification =>
                !notification.ProjectId.HasValue
                || !mutedProjectIds.Contains(notification.ProjectId.Value));
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<NotificationOperationResult> MarkReadAsync(
        ClaimsPrincipal principal,
        string userId,
        int notificationId,
        CancellationToken cancellationToken = default)
    {
        var result = await SetReadStateAsync(
            principal,
            userId,
            new[] { notificationId },
            markRead: true,
            appliesToAll: false,
            cancellationToken);

        return result.Result;
    }

    public async Task<NotificationOperationResult> MarkUnreadAsync(
        ClaimsPrincipal principal,
        string userId,
        int notificationId,
        CancellationToken cancellationToken = default)
    {
        var result = await SetReadStateAsync(
            principal,
            userId,
            new[] { notificationId },
            markRead: false,
            appliesToAll: false,
            cancellationToken);

        return result.Result;
    }

    public Task<NotificationReadMutationResult> MarkManyReadAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<int> notificationIds,
        CancellationToken cancellationToken = default)
        => SetReadStateAsync(
            principal,
            userId,
            notificationIds,
            markRead: true,
            appliesToAll: false,
            cancellationToken);

    public Task<NotificationReadMutationResult> MarkManyUnreadAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<int> notificationIds,
        CancellationToken cancellationToken = default)
        => SetReadStateAsync(
            principal,
            userId,
            notificationIds,
            markRead: false,
            appliesToAll: false,
            cancellationToken);

    public async Task<NotificationReadMutationResult> MarkAllReadAsync(
        ClaimsPrincipal principal,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);

        var projectMap = await GetAccessibleProjectMapAsync(principal, userId, cancellationToken);
        var mutedProjectIds = await GetMutedProjectIdsAsync(userId, cancellationToken);

        var query = BuildAccessibleQuery(userId, projectMap.Keys.ToArray())
            .Where(n => n.ReadUtc == null);

        if (mutedProjectIds.Count > 0)
        {
            query = query.Where(n => !n.ProjectId.HasValue || !mutedProjectIds.Contains(n.ProjectId.Value));
        }

        var now = _clock.UtcNow.UtcDateTime;
        int affectedCount;

        if (_db.Database.IsRelational())
        {
            affectedCount = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.ReadUtc, now)
                .SetProperty(n => n.SeenUtc, n => n.SeenUtc ?? now),
                cancellationToken);
        }
        else
        {
            // BuildAccessibleQuery is intentionally no-tracking for read paths. Reload the
            // matching rows as tracked entities for providers (notably EF InMemory) that do not
            // implement ExecuteUpdateAsync.
            var rowIds = await query
                .Select(notification => notification.Id)
                .ToListAsync(cancellationToken);
            var rows = rowIds.Count == 0
                ? new List<Notification>()
                : await _db.Notifications
                    .Where(notification => rowIds.Contains(notification.Id))
                    .ToListAsync(cancellationToken);

            foreach (var notification in rows)
            {
                notification.ReadUtc = now;
                notification.SeenUtc ??= now;
            }

            affectedCount = rows.Count;
            if (affectedCount > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // Read-all is represented as a scoped mutation instead of returning every database id.
        // This keeps SignalR and HTTP payloads bounded while allowing every open tab to update
        // all notification rows currently present in its own view.
        return new NotificationReadMutationResult(
            NotificationOperationResult.Success,
            Array.Empty<int>(),
            true,
            now,
            now,
            true,
            affectedCount);
    }

    public async Task<NotificationSeenMutationResult> MarkSeenAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<int> notificationIds,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);

        var ids = NormalizeIds(notificationIds);
        if (ids.Length == 0)
        {
            return new NotificationSeenMutationResult(
                NotificationOperationResult.Success,
                Array.Empty<int>(),
                _clock.UtcNow.UtcDateTime,
                AppliesToAll: false,
                AffectedCount: 0);
        }

        var rows = await GetAccessibleNotificationsByIdsAsync(
            principal,
            userId,
            ids,
            cancellationToken);

        if (rows.Count == 0)
        {
            return new NotificationSeenMutationResult(
                NotificationOperationResult.NotFound,
                Array.Empty<int>(),
                _clock.UtcNow.UtcDateTime,
                AppliesToAll: false,
                AffectedCount: 0);
        }

        var now = _clock.UtcNow.UtcDateTime;
        var changed = new List<int>();

        foreach (var notification in rows)
        {
            if (notification.SeenUtc is null)
            {
                notification.SeenUtc = now;
                changed.Add(notification.Id);
            }
        }

        if (changed.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new NotificationSeenMutationResult(
            NotificationOperationResult.Success,
            changed,
            now,
            AppliesToAll: false,
            AffectedCount: changed.Count);
    }

    /// <summary>
    /// Marks every accessible, unmuted notification the recipient has not seen as seen. Read
    /// state is unchanged. When <paramref name="upToId"/> is supplied, notifications with a
    /// higher identifier (materialised after the caller's view was loaded) are left unseen.
    /// </summary>
    public async Task<NotificationSeenMutationResult> MarkAllSeenAsync(
        ClaimsPrincipal principal,
        string userId,
        int? upToId,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);

        var projectMap = await GetAccessibleProjectMapAsync(principal, userId, cancellationToken);
        var mutedProjectIds = await GetMutedProjectIdsAsync(userId, cancellationToken);

        var query = FilterMuted(
            BuildAccessibleQuery(userId, projectMap.Keys.ToArray())
                .Where(n => n.SeenUtc == null),
            mutedProjectIds,
            onlyMuted: false);

        if (upToId is int maxId)
        {
            query = query.Where(n => n.Id <= maxId);
        }

        var now = _clock.UtcNow.UtcDateTime;
        int affectedCount;

        if (_db.Database.IsRelational())
        {
            affectedCount = await query.ExecuteUpdateAsync(
                setters => setters.SetProperty(n => n.SeenUtc, now),
                cancellationToken);
        }
        else
        {
            // EF InMemory does not implement ExecuteUpdateAsync; reload the rows as tracked.
            var rowIds = await query
                .Select(notification => notification.Id)
                .ToListAsync(cancellationToken);
            var rows = rowIds.Count == 0
                ? new List<Notification>()
                : await _db.Notifications
                    .Where(notification => rowIds.Contains(notification.Id))
                    .ToListAsync(cancellationToken);

            foreach (var notification in rows)
            {
                notification.SeenUtc ??= now;
            }

            affectedCount = rows.Count;
            if (affectedCount > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // Like read-all, seen-all is broadcast as a scoped mutation instead of every id.
        return new NotificationSeenMutationResult(
            NotificationOperationResult.Success,
            Array.Empty<int>(),
            now,
            AppliesToAll: true,
            AffectedCount: affectedCount);
    }

    public async Task<NotificationOperationResult> SetProjectMuteAsync(
        ClaimsPrincipal principal,
        string userId,
        int projectId,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        var result = await SetProjectMuteDetailedAsync(
            principal,
            userId,
            projectId,
            muted,
            cancellationToken);

        return result.Result;
    }

    public async Task<NotificationProjectMuteResult> SetProjectMuteDetailedAsync(
        ClaimsPrincipal principal,
        string userId,
        int projectId,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        ValidatePrincipalAndUser(principal, userId);

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotificationProjectMuteResult.NotFound(projectId, muted);
        }

        if (!ProjectAccessGuard.CanViewProject(project, principal, userId))
        {
            return NotificationProjectMuteResult.Forbidden(projectId, muted);
        }

        var existingMute = await _db.UserProjectMutes
            .FirstOrDefaultAsync(
                m => m.ProjectId == projectId && m.UserId == userId,
                cancellationToken);

        var changedIds = new List<int>();

        if (muted)
        {
            if (existingMute is null)
            {
                _db.UserProjectMutes.Add(new UserProjectMute
                {
                    ProjectId = projectId,
                    UserId = userId,
                });
            }

            // Muting is a forward-looking preference. Existing unread rows are closed once so
            // they do not reappear as an unread backlog when the project is later unmuted.
            var now = _clock.UtcNow.UtcDateTime;
            var unreadRows = await _db.Notifications
                .WhereKindIsReadable()
                .Where(n =>
                    n.RecipientUserId == userId
                    && n.ProjectId == projectId
                    && n.ReadUtc == null)
                .ToListAsync(cancellationToken);

            foreach (var notification in unreadRows)
            {
                notification.ReadUtc = now;
                notification.SeenUtc ??= now;
                changedIds.Add(notification.Id);
            }
        }
        else if (existingMute is not null)
        {
            _db.UserProjectMutes.Remove(existingMute);
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return NotificationProjectMuteResult.Success(projectId, muted, changedIds);
    }

    private async Task<NotificationReadMutationResult> SetReadStateAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<int> notificationIds,
        bool markRead,
        bool appliesToAll,
        CancellationToken cancellationToken)
    {
        ValidatePrincipalAndUser(principal, userId);

        var ids = NormalizeIds(notificationIds);
        if (ids.Length == 0)
        {
            return new NotificationReadMutationResult(
                NotificationOperationResult.Success,
                Array.Empty<int>(),
                markRead,
                null,
                null,
                appliesToAll,
                0);
        }

        var rows = await GetAccessibleNotificationsByIdsAsync(
            principal,
            userId,
            ids,
            cancellationToken);

        if (rows.Count == 0)
        {
            return new NotificationReadMutationResult(
                NotificationOperationResult.NotFound,
                Array.Empty<int>(),
                markRead,
                null,
                null,
                appliesToAll,
                0);
        }

        var now = _clock.UtcNow.UtcDateTime;
        var changed = new List<int>();

        foreach (var notification in rows)
        {
            if (markRead)
            {
                if (notification.ReadUtc is null)
                {
                    notification.ReadUtc = now;
                    notification.SeenUtc ??= now;
                    changed.Add(notification.Id);
                }
            }
            else if (notification.ReadUtc is not null)
            {
                notification.ReadUtc = null;
                notification.SeenUtc ??= now;
                changed.Add(notification.Id);
            }
        }

        if (changed.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new NotificationReadMutationResult(
            NotificationOperationResult.Success,
            changed,
            markRead,
            markRead ? now : null,
            now,
            appliesToAll,
            changed.Count);
    }

    private async Task<List<Notification>> GetAccessibleNotificationsByIdsAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Notifications
            .WhereKindIsReadable()
            .Where(n => n.RecipientUserId == userId && ids.Contains(n.Id))
            .Where(n => n.ActorUserId == null
                || n.ActorUserId != userId
                || !n.Kind.HasValue
                || !SuppressedSelfNotificationKinds.Contains(n.Kind.Value))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return rows;
        }

        var projectIds = rows
            .Where(n => n.ProjectId.HasValue)
            .Select(n => n.ProjectId!.Value)
            .Distinct()
            .ToArray();

        var accessibleProjects = await GetAccessibleProjectsAsync(
            principal,
            userId,
            projectIds,
            cancellationToken);

        return rows
            .Where(n => !n.ProjectId.HasValue || accessibleProjects.ContainsKey(n.ProjectId.Value))
            .ToList();
    }

    private IQueryable<Notification> BuildAccessibleQuery(
        string userId,
        IReadOnlyCollection<int> accessibleProjectIds)
    {
        // Every list, count and folder query starts here, so rows with a kind from a newer
        // release are skipped consistently on an older instance during a rolling deployment.
        var query = _db.Notifications
            .AsNoTracking()
            .WhereKindIsReadable()
            .Where(n => n.RecipientUserId == userId)
            .Where(n => n.ActorUserId == null
                || n.ActorUserId != userId
                || !n.Kind.HasValue
                || !SuppressedSelfNotificationKinds.Contains(n.Kind.Value));

        if (accessibleProjectIds.Count == 0)
        {
            return query.Where(n => n.ProjectId == null);
        }

        return query.Where(n =>
            n.ProjectId == null
            || (n.ProjectId.HasValue && accessibleProjectIds.Contains(n.ProjectId.Value)));
    }

    private static IQueryable<Notification> ApplyFilters(
        IQueryable<Notification> query,
        NotificationListOptions options,
        IReadOnlyDictionary<int, string> projectMap,
        IReadOnlyCollection<int> mutedProjectIds)
    {
        query = ApplyFolderFilter(query, options.Folder, mutedProjectIds);

        if (options.ProjectId.HasValue)
        {
            query = query.Where(n => n.ProjectId == options.ProjectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(options.Module))
        {
            var module = options.Module.Trim();
            query = query.Where(n => n.Module == module);
        }

        if (options.OnlyUnread)
        {
            query = query.Where(n => n.ReadUtc == null);
        }
        else if (options.OnlyRead)
        {
            query = query.Where(n => n.ReadUtc != null);
        }

        var folder = NormalizeFolder(options.Folder);
        if (folder is null)
        {
            if (options.OnlyMuted)
            {
                query = FilterMuted(query, mutedProjectIds, onlyMuted: true);
            }
            else if (!options.IncludeMuted)
            {
                query = FilterMuted(query, mutedProjectIds, onlyMuted: false);
            }
        }

        var search = NormalizeSearch(options.Search);
        if (search is not null)
        {
            var searchLower = search.ToLower();
            var matchingProjectIds = projectMap
                .Where(pair => pair.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();

            query = query.Where(n =>
                (n.Title != null && n.Title.ToLower().Contains(searchLower))
                || (n.Summary != null && n.Summary.ToLower().Contains(searchLower))
                || (n.Module != null && n.Module.ToLower().Contains(searchLower))
                || (n.EventType != null && n.EventType.ToLower().Contains(searchLower))
                || (n.ScopeType != null && n.ScopeType.ToLower().Contains(searchLower))
                || (n.ProjectId.HasValue && matchingProjectIds.Contains(n.ProjectId.Value)));
        }

        return query;
    }

    private static IQueryable<Notification> ApplyFolderFilter(
        IQueryable<Notification> query,
        string? requestedFolder,
        IReadOnlyCollection<int> mutedProjectIds)
    {
        return NormalizeFolder(requestedFolder) switch
        {
            "inbox" => FilterMuted(query, mutedProjectIds, onlyMuted: false),
            "all" => query,
            "unread" => FilterMuted(query.Where(n => n.ReadUtc == null), mutedProjectIds, onlyMuted: false),
            "action" => FilterMuted(FilterByKinds(query, ActionRequiredKinds), mutedProjectIds, onlyMuted: false),
            "approvals" => FilterMuted(FilterByKinds(query, ApprovalKinds), mutedProjectIds, onlyMuted: false),
            "collaboration" => FilterMuted(FilterByKinds(query, CollaborationKinds), mutedProjectIds, onlyMuted: false),
            "assignments" => FilterMuted(FilterByKinds(query, AssignmentKinds), mutedProjectIds, onlyMuted: false),
            "lifecycle" => FilterMuted(FilterByKinds(query, LifecycleKinds), mutedProjectIds, onlyMuted: false),
            "documents" => FilterMuted(FilterByKinds(query, DocumentKinds), mutedProjectIds, onlyMuted: false),
            "tasks" => FilterMuted(FilterByKinds(query, ActionTaskKinds), mutedProjectIds, onlyMuted: false),
            "muted" => FilterMuted(query, mutedProjectIds, onlyMuted: true),
            _ => query,
        };
    }

    private static IQueryable<Notification> FilterByKinds(
        IQueryable<Notification> query,
        NotificationKind[] kinds)
        => query.Where(n => n.Kind.HasValue && kinds.Contains(n.Kind.Value));

    private static IQueryable<Notification> FilterMuted(
        IQueryable<Notification> query,
        IReadOnlyCollection<int> mutedProjectIds,
        bool onlyMuted)
    {
        if (mutedProjectIds.Count == 0)
        {
            return onlyMuted ? query.Where(_ => false) : query;
        }

        return onlyMuted
            ? query.Where(n => n.ProjectId.HasValue && mutedProjectIds.Contains(n.ProjectId.Value))
            : query.Where(n => !n.ProjectId.HasValue || !mutedProjectIds.Contains(n.ProjectId.Value));
    }

    private async Task<IReadOnlyList<NotificationFolderCountDto>> GetFolderCountsAsync(
        string userId,
        IReadOnlyCollection<int> accessibleProjectIds,
        IReadOnlyCollection<int> mutedProjectIds,
        CancellationToken cancellationToken)
    {
        var mutedIds = mutedProjectIds.ToArray();
        var aggregates = await BuildAccessibleQuery(userId, accessibleProjectIds)
            .Select(notification => new
            {
                notification.Kind,
                IsRead = notification.ReadUtc != null,
                IsMuted = notification.ProjectId.HasValue
                    && mutedIds.Contains(notification.ProjectId.Value),
            })
            .GroupBy(row => new { row.Kind, row.IsRead, row.IsMuted })
            .Select(group => new
            {
                group.Key.Kind,
                group.Key.IsRead,
                group.Key.IsMuted,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        int CountFolder(
            IReadOnlyCollection<NotificationKind>? kinds,
            bool unreadOnly = false,
            bool includeMuted = false,
            bool onlyMuted = false)
            => aggregates
                .Where(row => !unreadOnly || !row.IsRead)
                .Where(row => onlyMuted ? row.IsMuted : includeMuted || !row.IsMuted)
                .Where(row => kinds is null || (row.Kind.HasValue && kinds.Contains(row.Kind.Value)))
                .Sum(row => row.Count);

        NotificationFolderCountDto Build(
            string key,
            IReadOnlyCollection<NotificationKind>? kinds = null,
            bool includeMuted = false,
            bool onlyMuted = false)
            => new(
                key,
                CountFolder(kinds, includeMuted: includeMuted, onlyMuted: onlyMuted),
                CountFolder(kinds, unreadOnly: true, includeMuted: includeMuted, onlyMuted: onlyMuted));

        return new[]
        {
            Build("inbox"),
            new NotificationFolderCountDto(
                "unread",
                CountFolder(null, unreadOnly: true),
                CountFolder(null, unreadOnly: true)),
            Build("action", ActionRequiredKinds),
            Build("approvals", ApprovalKinds),
            Build("collaboration", CollaborationKinds),
            Build("assignments", AssignmentKinds),
            Build("lifecycle", LifecycleKinds),
            Build("documents", DocumentKinds),
            Build("tasks", ActionTaskKinds),
            Build("all", includeMuted: true),
            Build("muted", includeMuted: true, onlyMuted: true),
        };
    }

    private async Task<NotificationFilterOptions> GetFilterOptionsAsync(
        string userId,
        IReadOnlyCollection<int> accessibleProjectIds,
        IReadOnlyDictionary<int, string> projectMap,
        IReadOnlyCollection<int> mutedProjectIds,
        CancellationToken cancellationToken)
    {
        var projectIdsWithNotifications = await BuildAccessibleQuery(userId, accessibleProjectIds)
            .Where(n => n.ProjectId.HasValue)
            .Select(n => n.ProjectId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var projects = projectIdsWithNotifications
            .Where(projectMap.ContainsKey)
            .Select(projectId => new NotificationProjectOptionDto(
                projectId,
                projectMap[projectId],
                mutedProjectIds.Contains(projectId)))
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var modules = await BuildAccessibleQuery(userId, accessibleProjectIds)
            .Where(n => n.Module != null && n.Module != string.Empty)
            .Select(n => n.Module!)
            .Distinct()
            .OrderBy(module => module)
            .ToListAsync(cancellationToken);

        return new NotificationFilterOptions(projects, modules);
    }

    private async Task<Dictionary<int, string>> GetAccessibleProjectMapAsync(
        ClaimsPrincipal principal,
        string userId,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new Dictionary<int, string>();
        }

        // Only projects referenced by this user's retained notifications, found with a subquery
        // in the same round trip. Full rows are loaded on purpose: ProjectAccessGuard receives
        // the same Project it always has, so a future resource-level rule keeps working here.
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => _db.Notifications.Any(notification =>
                notification.RecipientUserId == userId
                && notification.ProjectId == project.Id))
            .ToListAsync(cancellationToken);

        return ToAccessibleProjectMap(projects, principal, userId);
    }

    private async Task<Dictionary<int, string>> GetAccessibleProjectsAsync(
        ClaimsPrincipal principal,
        string userId,
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0 || principal.Identity?.IsAuthenticated != true)
        {
            return new Dictionary<int, string>();
        }

        var projects = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .ToListAsync(cancellationToken);

        return ToAccessibleProjectMap(projects, principal, userId);
    }

    private static Dictionary<int, string> ToAccessibleProjectMap(
        IEnumerable<Project> projects,
        ClaimsPrincipal principal,
        string userId)
        => projects
            .Where(project => ProjectAccessGuard.CanViewProject(project, principal, userId))
            .ToDictionary(
                project => project.Id,
                project => FirstNonEmpty(project.Name, $"Project #{project.Id}"));

    private sealed record NotificationSummary(
        int UnreadCount,
        int UnseenCount,
        int? LatestNotificationId,
        int TotalCount,
        long UnreadIdSum,
        long UnseenIdSum)
    {
        /// <summary>
        /// Changes whenever a row the bell could show is added, deleted (for example by
        /// retention), read, unread or seen, including read/unread changes that leave the
        /// counts equal. Clients compare it to skip reloading an unchanged list.
        /// </summary>
        public string ListVersion => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{TotalCount}.{LatestNotificationId ?? 0}.{UnreadCount}.{UnseenCount}.{UnreadIdSum}.{UnseenIdSum}");
    }

    private async Task<HashSet<int>> GetMutedProjectIdsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var ids = await _db.UserProjectMutes
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ProjectId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    private static int[] NormalizeIds(IReadOnlyCollection<int>? ids)
        => ids is null
            ? Array.Empty<int>()
            : ids.Where(id => id > 0).Distinct().Take(500).ToArray();

    private static string? NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        return folder.Trim().ToLowerInvariant() switch
        {
            "inbox" => "inbox",
            "all" => "all",
            "unread" => "unread",
            "action" => "action",
            "approvals" => "approvals",
            "collaboration" => "collaboration",
            "assignments" => "assignments",
            "lifecycle" => "lifecycle",
            "documents" => "documents",
            "tasks" => "tasks",
            "muted" => "muted",
            _ => null,
        };
    }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var normalized = search.Trim();
        return normalized.Length <= MaximumSearchLength
            ? normalized
            : normalized[..MaximumSearchLength];
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static void ValidatePrincipalAndUser(ClaimsPrincipal principal, string userId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required for notification operations.", nameof(userId));
        }
    }

    private sealed record NotificationFilterOptions(
        IReadOnlyList<NotificationProjectOptionDto> Projects,
        IReadOnlyList<string> Modules)
    {
        public static NotificationFilterOptions Empty { get; } = new(
            Array.Empty<NotificationProjectOptionDto>(),
            Array.Empty<string>());
    }

    private sealed record NotificationCursor(DateTime CreatedUtc, int Id)
    {
        public static string Encode(DateTime createdUtc, int id)
        {
            var raw = FormattableString.Invariant($"{EnsureUtc(createdUtc).Ticks}:{id}");
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
        }

        public static bool TryDecode(string? encoded, out NotificationCursor cursor)
        {
            cursor = new NotificationCursor(default, 0);
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return false;
            }

            try
            {
                var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
                var separator = raw.IndexOf(':', StringComparison.Ordinal);
                if (separator <= 0
                    || !long.TryParse(raw[..separator], out var ticks)
                    || !int.TryParse(raw[(separator + 1)..], out var id)
                    || ticks <= 0
                    || id <= 0)
                {
                    return false;
                }

                cursor = new NotificationCursor(new DateTime(ticks, DateTimeKind.Utc), id);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}

public sealed class NotificationListOptions
{
    public int? Limit { get; set; }

    public bool OnlyUnread { get; set; }

    public bool OnlyRead { get; set; }

    public bool OnlyMuted { get; set; }

    public bool IncludeMuted { get; set; }

    public int? ProjectId { get; set; }

    public string? Module { get; set; }

    public string? Folder { get; set; }

    public string? Search { get; set; }

    public string? Cursor { get; set; }

    public bool IncludeFilterOptions { get; set; } = true;

    /// <summary>
    /// Orders unread action-required notifications before everything else, then newest first.
    /// Applies to the first page only: a page requested with a cursor is always chronological and
    /// a priority-ordered page returns no next cursor.
    /// </summary>
    public bool ActionRequiredFirst { get; set; }
}

public sealed record NotificationCounts(
    int UnreadCount,
    int UnseenCount,
    int? LatestNotificationId = null,
    string? ListVersion = null);

public enum NotificationOperationResult
{
    Success,
    NotFound,
    Forbidden,
}

public sealed record NotificationReadMutationResult(
    NotificationOperationResult Result,
    IReadOnlyList<int> NotificationIds,
    bool IsRead,
    DateTime? ReadUtc,
    DateTime? SeenUtc,
    bool AppliesToAll,
    int AffectedCount);

public sealed record NotificationSeenMutationResult(
    NotificationOperationResult Result,
    IReadOnlyList<int> NotificationIds,
    DateTime SeenUtc,
    bool AppliesToAll,
    int AffectedCount);

public sealed record NotificationProjectMuteResult(
    NotificationOperationResult Result,
    int ProjectId,
    bool IsMuted,
    IReadOnlyList<int> ChangedNotificationIds)
{
    public static NotificationProjectMuteResult Success(
        int projectId,
        bool muted,
        IReadOnlyList<int> changedNotificationIds)
        => new(NotificationOperationResult.Success, projectId, muted, changedNotificationIds);

    public static NotificationProjectMuteResult NotFound(int projectId, bool muted)
        => new(NotificationOperationResult.NotFound, projectId, muted, Array.Empty<int>());

    public static NotificationProjectMuteResult Forbidden(int projectId, bool muted)
        => new(NotificationOperationResult.Forbidden, projectId, muted, Array.Empty<int>());
}
