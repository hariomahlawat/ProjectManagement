using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Services.Admin;

public sealed class AdminDashboardMetrics
{
    public int TotalUsers { get; init; }
    public int ActiveUsers { get; init; }
    public int DisabledUsers { get; init; }
    public int LockedUsers { get; init; }
    public int PendingDeletionUsers { get; init; }
    public int MustChangePasswordUsers { get; init; }
    public int LoginsLast7Days { get; init; }
    public int UniqueUsersLast7Days { get; init; }
    public int FailedLoginsLast7Days { get; init; }
    public int AuditEventsLast24Hours { get; init; }
    public int WarningEventsLast24Hours { get; init; }
    public int ErrorEventsLast24Hours { get; init; }
    public int ArchivedProjects { get; init; }
    public int TrashedProjects { get; init; }
    public int DeletedDocuments { get; init; }
    public int DeletedEvents { get; init; }

    // Compatibility aliases used by the existing dashboard presentation.
    public int MustChangePwd => MustChangePasswordUsers;
    public int LoginsLast7d => LoginsLast7Days;
    public int UniqueLoginsLast7d => UniqueUsersLast7Days;
    public int FailedLoginsLast7d => FailedLoginsLast7Days;
    public int AuditEvents24h => AuditEventsLast24Hours;
    public int WarningEvents24h => WarningEventsLast24Hours;
    public int ErrorEvents24h => ErrorEventsLast24Hours;
    public int DisabledPct => TotalUsers == 0 ? 0 : (int)Math.Round(100.0 * DisabledUsers / TotalUsers);
    public int MustChangePwdPct => TotalUsers == 0 ? 0 : (int)Math.Round(100.0 * MustChangePasswordUsers / TotalUsers);
    public int UniqueLoginPct => LoginsLast7Days == 0 ? 0 : (int)Math.Round(100.0 * UniqueUsersLast7Days / LoginsLast7Days);
    public int LoginAttemptsLast7d => LoginsLast7Days + FailedLoginsLast7Days;
    public int FailedLoginPct => LoginAttemptsLast7d == 0 ? 0 : (int)Math.Round(100.0 * FailedLoginsLast7Days / LoginAttemptsLast7d);
    public int WarningPct => AuditEventsLast24Hours == 0 ? 0 : (int)Math.Round(100.0 * WarningEventsLast24Hours / AuditEventsLast24Hours);
    public int ErrorPct => AuditEventsLast24Hours == 0 ? 0 : (int)Math.Round(100.0 * ErrorEventsLast24Hours / AuditEventsLast24Hours);
}

public sealed record AdminDashboardAction(string Level, string Message, string WhenLocal);

public sealed record AdminDashboardAttention(
    string Text,
    string LinkText,
    string NavigationKey,
    IReadOnlyDictionary<string, object?>? RouteValues = null);

public sealed record AdminDashboardSnapshot(
    AdminDashboardMetrics Metrics,
    IReadOnlyList<AdminDashboardAction> RecentActions,
    IReadOnlyList<AdminDashboardAttention> AttentionItems);

public interface IAdminDashboardService
{
    Task<AdminDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminDashboardService : IAdminDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly IUserAccountStateResolver _states;

    public AdminDashboardService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IUserAccountStateResolver states)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _states = states ?? throw new ArgumentNullException(nameof(states));
    }

    public async Task<AdminDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _time.UtcNow;
        var since7Days = nowUtc.AddDays(-7);
        var since24Hours = nowUtc.AddDays(-1).UtcDateTime;

        var users = await _db.Users.AsNoTracking().ToListAsync(cancellationToken);
        var stateCounts = users
            .Select(user => _states.Resolve(user, nowUtc).State)
            .GroupBy(state => state)
            .ToDictionary(group => group.Key, group => group.Count());

        var successfulLoginQuery = _db.AuthEvents.AsNoTracking()
            .Where(authEvent =>
                authEvent.Event == AuthenticationEventNames.LoginSucceeded
                && authEvent.WhenUtc >= since7Days);

        var loginCount = await successfulLoginQuery.CountAsync(cancellationToken);
        var uniqueLoginCount = await successfulLoginQuery
            .Select(authEvent => authEvent.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        var failedLoginCount = await _db.AuditLogs.AsNoTracking()
            .CountAsync(audit =>
                audit.TimeUtc >= since7Days.UtcDateTime
                && audit.Action == AuthenticationEventNames.AuditLoginFailed,
                cancellationToken);

        var recentAuditQuery = _db.AuditLogs.AsNoTracking()
            .Where(audit => audit.TimeUtc >= since24Hours);

        var auditEvents = await recentAuditQuery.CountAsync(cancellationToken);
        var warningEvents = await recentAuditQuery.CountAsync(audit => audit.Level == "Warning", cancellationToken);
        var errorEvents = await recentAuditQuery.CountAsync(audit => audit.Level == "Error", cancellationToken);

        var metrics = new AdminDashboardMetrics
        {
            TotalUsers = users.Count,
            ActiveUsers = Count(stateCounts, AdminUserAccountState.Active),
            DisabledUsers = Count(stateCounts, AdminUserAccountState.Disabled),
            LockedUsers = Count(stateCounts, AdminUserAccountState.TemporarilyLocked),
            PendingDeletionUsers = Count(stateCounts, AdminUserAccountState.PendingDeletion),
            MustChangePasswordUsers = Count(stateCounts, AdminUserAccountState.MustChangePassword),
            LoginsLast7Days = loginCount,
            UniqueUsersLast7Days = uniqueLoginCount,
            FailedLoginsLast7Days = failedLoginCount,
            AuditEventsLast24Hours = auditEvents,
            WarningEventsLast24Hours = warningEvents,
            ErrorEventsLast24Hours = errorEvents,
            ArchivedProjects = await _db.Projects.AsNoTracking()
                .CountAsync(project => !project.IsDeleted && project.IsArchived, cancellationToken),
            TrashedProjects = await _db.Projects.AsNoTracking()
                .CountAsync(project => project.IsDeleted, cancellationToken),
            DeletedDocuments = await _db.ProjectDocuments.AsNoTracking()
                .CountAsync(document => document.Status == ProjectDocumentStatus.SoftDeleted, cancellationToken),
            DeletedEvents = await _db.Events.AsNoTracking()
                .CountAsync(calendarEvent => calendarEvent.IsDeleted, cancellationToken)
        };

        var recentActions = await _db.AuditLogs.AsNoTracking()
            .Where(audit =>
                audit.Action.StartsWith("Admin")
                || audit.Action.StartsWith("Projects.")
                || audit.Action.StartsWith("Project.")
                || audit.Action.StartsWith("Documents.")
                || audit.Action.StartsWith("Calendar."))
            .OrderByDescending(audit => audit.TimeUtc)
            .Take(10)
            .Select(audit => new
            {
                audit.Level,
                Message = audit.Message ?? audit.Action,
                audit.TimeUtc
            })
            .ToListAsync(cancellationToken);

        var attention = BuildAttention(metrics);
        return new AdminDashboardSnapshot(
            metrics,
            recentActions
                .Select(action => new AdminDashboardAction(
                    action.Level,
                    action.Message,
                    _time.FormatIst(action.TimeUtc)))
                .ToArray(),
            attention);
    }

    private static int Count(
        IReadOnlyDictionary<AdminUserAccountState, int> counts,
        AdminUserAccountState state) =>
        counts.TryGetValue(state, out var value) ? value : 0;

    private static IReadOnlyList<AdminDashboardAttention> BuildAttention(AdminDashboardMetrics metrics)
    {
        var items = new List<AdminDashboardAttention>();

        if (metrics.MustChangePasswordUsers > 0)
        {
            items.Add(new(
                $"{metrics.MustChangePasswordUsers} user(s) must change password",
                "View",
                AdminNavigationKeys.Users,
                new Dictionary<string, object?> { ["status"] = "must-change-password" }));
        }

        if (metrics.DisabledUsers > 0)
        {
            items.Add(new(
                $"{metrics.DisabledUsers} user(s) are disabled",
                "Manage",
                AdminNavigationKeys.Users,
                new Dictionary<string, object?> { ["status"] = "disabled" }));
        }

        if (metrics.PendingDeletionUsers > 0)
        {
            items.Add(new(
                $"{metrics.PendingDeletionUsers} account(s) are pending deletion",
                "Review",
                AdminNavigationKeys.Users,
                new Dictionary<string, object?> { ["status"] = "pending-deletion" }));
        }

        if (metrics.ErrorEventsLast24Hours > 0)
        {
            items.Add(new(
                $"{metrics.ErrorEventsLast24Hours} error log(s) in the last 24 hours",
                "Investigate",
                AdminNavigationKeys.Logs,
                new Dictionary<string, object?> { ["Level"] = "Error" }));
        }

        if (metrics.TrashedProjects > 0)
        {
            items.Add(new(
                $"{metrics.TrashedProjects} project(s) in trash",
                "Review",
                AdminNavigationKeys.ProjectTrash));
        }

        if (metrics.DeletedDocuments > 0)
        {
            items.Add(new(
                $"{metrics.DeletedDocuments} document(s) in recycle bin",
                "Restore",
                AdminNavigationKeys.DocumentRecycle));
        }

        if (metrics.DeletedEvents > 0)
        {
            items.Add(new(
                $"{metrics.DeletedEvents} deleted calendar event(s)",
                "Recover",
                AdminNavigationKeys.DeletedEvents));
        }

        return items;
    }
}
