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

    public int RestrictedUsers =>
        DisabledUsers + LockedUsers + PendingDeletionUsers + MustChangePasswordUsers;

    public int RecoveryQueue => TrashedProjects + DeletedDocuments + DeletedEvents;
}

public enum AdminAttentionSeverity
{
    Information = 0,
    Warning = 1,
    Critical = 2
}

public sealed record AdminDashboardAction(
    string Level,
    string Action,
    string Message,
    string Actor,
    string WhenLocal);

public sealed record AdminDashboardAttention(
    AdminAttentionSeverity Severity,
    string Title,
    string Detail,
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

    public AdminDashboardService(
        ApplicationDbContext db,
        IAdminTimeService time)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<AdminDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _time.UtcNow;
        var since7Days = nowUtc.AddDays(-7);
        var since24Hours = nowUtc.AddDays(-1).UtcDateTime;

        var totalUsers = await _db.Users.AsNoTracking().CountAsync(cancellationToken);
        var pendingDeletionUsers = await _db.Users.AsNoTracking()
            .CountAsync(user => user.PendingDeletion, cancellationToken);
        var disabledUsers = await _db.Users.AsNoTracking()
            .CountAsync(user => !user.PendingDeletion && user.IsDisabled, cancellationToken);
        var lockedUsers = await _db.Users.AsNoTracking()
            .CountAsync(user =>
                !user.PendingDeletion
                && !user.IsDisabled
                && user.LockoutEnd.HasValue
                && user.LockoutEnd > nowUtc,
                cancellationToken);
        var mustChangePasswordUsers = await _db.Users.AsNoTracking()
            .CountAsync(user =>
                !user.PendingDeletion
                && !user.IsDisabled
                && (!user.LockoutEnd.HasValue || user.LockoutEnd <= nowUtc)
                && user.MustChangePassword,
                cancellationToken);
        var activeUsers = Math.Max(
            0,
            totalUsers - pendingDeletionUsers - disabledUsers - lockedUsers - mustChangePasswordUsers);

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
        var warningEvents = await recentAuditQuery
            .CountAsync(audit => audit.Level == "Warning", cancellationToken);
        var errorEvents = await recentAuditQuery
            .CountAsync(audit => audit.Level == "Error", cancellationToken);

        var metrics = new AdminDashboardMetrics
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            DisabledUsers = disabledUsers,
            LockedUsers = lockedUsers,
            PendingDeletionUsers = pendingDeletionUsers,
            MustChangePasswordUsers = mustChangePasswordUsers,
            LoginsLast7Days = loginCount,
            UniqueUsersLast7Days = uniqueLoginCount,
            FailedLoginsLast7Days = failedLoginCount,
            AuditEventsLast24Hours = auditEvents,
            WarningEventsLast24Hours = warningEvents,
            ErrorEventsLast24Hours = errorEvents,
            ArchivedProjects = await _db.Projects.AsNoTracking()
                .CountAsync(project => !project.IsDeleted && project.IsArchived, cancellationToken),
            TrashedProjects = await _db.Projects.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(project => project.IsDeleted, cancellationToken),
            DeletedDocuments = await _db.ProjectDocuments.AsNoTracking()
                .CountAsync(document => document.Status == ProjectDocumentStatus.SoftDeleted, cancellationToken),
            DeletedEvents = await _db.Events.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(calendarEvent => calendarEvent.IsDeleted, cancellationToken)
        };

        var recentActions = await _db.AuditLogs.AsNoTracking()
            .Where(audit =>
                audit.Action.StartsWith("Admin")
                || audit.Action.StartsWith("MasterData.")
                || audit.Action.StartsWith("Projects.")
                || audit.Action.StartsWith("Project.")
                || audit.Action.StartsWith("Documents.")
                || audit.Action.StartsWith("Calendar."))
            .OrderByDescending(audit => audit.TimeUtc)
            .Take(6)
            .Select(audit => new
            {
                audit.Level,
                audit.Action,
                Message = audit.Message ?? audit.Action,
                Actor = audit.UserName ?? "System",
                audit.TimeUtc
            })
            .ToListAsync(cancellationToken);

        return new AdminDashboardSnapshot(
            metrics,
            recentActions
                .Select(action => new AdminDashboardAction(
                    action.Level,
                    action.Action,
                    action.Message,
                    action.Actor,
                    _time.FormatIst(action.TimeUtc)))
                .ToArray(),
            BuildAttention(metrics));
    }

    // Only what an administrator has to act on (docs/ux/admin-module.md, rule 5 "Colour means
    // something"). A pending password change, a disabled account or items in a bin are normal and
    // show as neutral figures on Home's task tiles instead.
    internal static IReadOnlyList<AdminDashboardAttention> BuildAttention(AdminDashboardMetrics metrics)
    {
        var items = new List<AdminDashboardAttention>();

        if (metrics.LockedUsers > 0)
        {
            items.Add(new(
                AdminAttentionSeverity.Warning,
                metrics.LockedUsers == 1
                    ? "1 account is locked after failed sign-ins"
                    : $"{metrics.LockedUsers:N0} accounts are locked after failed sign-ins",
                "The lock lifts by itself; clear it sooner from the person's account if they need in now.",
                "Review",
                AdminNavigationKeys.Users,
                new Dictionary<string, object?> { ["Status"] = "locked" }));
        }

        if (metrics.PendingDeletionUsers > 0)
        {
            items.Add(new(
                AdminAttentionSeverity.Warning,
                metrics.PendingDeletionUsers == 1
                    ? "1 account is waiting to be deleted"
                    : $"{metrics.PendingDeletionUsers:N0} accounts are waiting to be deleted",
                "Confirm the deletion or restore the account.",
                "Review",
                AdminNavigationKeys.Users,
                new Dictionary<string, object?> { ["Status"] = "pending-deletion" }));
        }

        if (metrics.ErrorEventsLast24Hours > 0)
        {
            items.Add(new(
                AdminAttentionSeverity.Warning,
                metrics.ErrorEventsLast24Hours == 1
                    ? "1 error in the audit log in the last 24 hours"
                    : $"{metrics.ErrorEventsLast24Hours:N0} errors in the audit log in the last 24 hours",
                "Something failed while people were working. Check what it was.",
                "Review",
                AdminNavigationKeys.Logs,
                new Dictionary<string, object?> { ["Level"] = "Error" }));
        }

        return items;
    }
}
