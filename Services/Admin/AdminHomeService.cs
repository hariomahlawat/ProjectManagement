using System.Globalization;
using System.Security.Claims;
using ProjectManagement.Services.Admin.Maintenance;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Services.Admin;

/// <summary>How the Home status line looks: calm unless an administrator has to act.</summary>
public enum AdminHomeTone
{
    Calm = 0,
    Unknown = 1,
    Attention = 2,
    Urgent = 3
}

public sealed record AdminHomeStatus(AdminHomeTone Tone, string Label, string Summary);

/// <summary>One thing an administrator has to act on ("To look at").</summary>
public sealed record AdminHomeLookAt(
    string Title,
    string Detail,
    string LinkText,
    string NavigationKey,
    IReadOnlyDictionary<string, object?>? RouteValues = null,
    bool Urgent = false);

/// <summary>A "What do you want to do?" tile: a task, the page it opens and one live figure.</summary>
public sealed record AdminHomeTile(
    string NavigationKey,
    string Title,
    string Body,
    string Icon,
    string Fact);

public sealed record AdminHomeSnapshot(
    AdminHomeStatus Status,
    IReadOnlyList<AdminHomeLookAt> LookAt,
    IReadOnlyList<AdminHomeTile> Tiles,
    IReadOnlyList<AdminDashboardAction> RecentActions);

public interface IAdminHomeService
{
    Task<AdminHomeSnapshot> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

/// <summary>
/// Administration Home (docs/ux/admin-module.md): one status line, "To look at" only when
/// something needs an administrator, six task tiles with one live figure each, and recent
/// changes. Tiles and figures appear only for pages the viewer can open.
/// </summary>
public sealed class AdminHomeService : IAdminHomeService
{
    private readonly IAdminDashboardService _dashboard;
    private readonly IAdminSystemHealthService _health;
    private readonly IAdminMasterDataIntegrityService _integrity;
    private readonly IMasterDataAdministrationQueryService _masterData;
    private readonly IAdminMaintenanceSummaryService _maintenance;
    private readonly IAdminNavigationAccess _access;
    private readonly IAdminRestoreTabsService _restore;
    private readonly IAdminTimeService _time;
    private readonly ILogger<AdminHomeService> _logger;

    public AdminHomeService(
        IAdminDashboardService dashboard,
        IAdminSystemHealthService health,
        IAdminMasterDataIntegrityService integrity,
        IMasterDataAdministrationQueryService masterData,
        IAdminMaintenanceSummaryService maintenance,
        IAdminNavigationAccess access,
        IAdminRestoreTabsService restore,
        IAdminTimeService time,
        ILogger<AdminHomeService> logger)
    {
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        _masterData = masterData ?? throw new ArgumentNullException(nameof(masterData));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AdminHomeSnapshot> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var openable = (await _access.GetOpenableEntriesAsync(user))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        bool CanOpen(string key) => openable.Contains(key);

        var dashboard = await _dashboard.GetAsync(cancellationToken);

        // Home never runs the system checks itself: it shows the last result, so opening Home does
        // not wait on storage or database checks.
        var health = CanOpen(AdminNavigationKeys.DatabaseHealth) ? _health.GetLatest() : null;

        var integrity = CanOpen(AdminNavigationKeys.MasterDataIntegrity)
            ? await TryAsync("data checks", () => _integrity.InspectAsync(cancellationToken))
            : null;
        var lists = CanOpen(AdminNavigationKeys.MasterDataCentre)
            ? await TryAsync("reference lists", () => _masterData.GetOverviewAsync(_time.TodayIst.Year, cancellationToken))
            : null;
        var jobs = CanOpen(AdminNavigationKeys.MaintenanceCentre)
            ? await TryAsync("data jobs", () => _maintenance.GetAsync(cancellationToken))
            : null;

        var lookAt = AdminHomeRules.BuildLookAt(dashboard.AttentionItems, health, integrity)
            .Where(item => CanOpen(item.NavigationKey))
            .ToArray();

        // The same counts as the tabs of Restore deleted items, so Home and that page agree.
        var restorable = CanOpen(AdminNavigationKeys.RecoveryCentre)
            ? (await _restore.GetAsync(user, cancellationToken)).Sum(tab => tab.Count ?? 0)
            : 0;

        var tiles = AdminHomeRules.BuildTiles(dashboard.Metrics, restorable, health, lists, jobs, FormatWhen)
            .Where(tile => CanOpen(tile.NavigationKey))
            .ToArray();

        return new AdminHomeSnapshot(
            AdminHomeRules.ResolveStatus(lookAt, health, CanOpen(AdminNavigationKeys.DatabaseHealth), FormatWhen),
            lookAt,
            tiles,
            dashboard.RecentActions);
    }

    private string FormatWhen(DateTimeOffset utc) => _time.FormatIst(utc);

    // A figure that cannot be read leaves its tile without a number; it never breaks Home.
    private async Task<T?> TryAsync<T>(string what, Func<Task<T>> read) where T : class
    {
        try
        {
            return await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Administration Home could not read {What}; the tile is shown without its figure.", what);
            return null;
        }
    }
}

/// <summary>
/// The rules behind Home, kept apart from data access so they can be tested on their own.
/// Amber or red only when an administrator has to act (docs/ux/admin-module.md, rule 5).
/// </summary>
public static class AdminHomeRules
{
    public const string NoFigure = "";

    /// <summary>At most this many system checks get their own "To look at" line.</summary>
    public const int MaxHealthLines = 5;

    public static IReadOnlyList<AdminHomeLookAt> BuildLookAt(
        IReadOnlyList<AdminDashboardAttention> attention,
        AdminSystemHealthSnapshot? health,
        MasterDataIntegritySnapshot? integrity)
    {
        var items = new List<AdminHomeLookAt>();

        // Each failed or warning check is its own line, in its own words, failed ones first: one
        // line saying "5 checks need a look" hid the rest (docs/ux/admin-review-fixes.md, PR 3).
        // Checks that have not run yet are not listed: there is nothing to do about them.
        if (health is not null)
        {
            var problems = health.Checks
                .Where(check => check.Status is AdminHealthStatus.Critical or AdminHealthStatus.Warning)
                .OrderByDescending(check => check.Status == AdminHealthStatus.Critical)
                .ToArray();
            foreach (var check in problems.Take(MaxHealthLines))
            {
                var urgent = check.Status == AdminHealthStatus.Critical;
                items.Add(new AdminHomeLookAt(
                    check.Summary,
                    check.RecommendedAction is { Length: > 0 } action ? $"{check.Label}. {action}" : check.Label,
                    "Open System health",
                    AdminNavigationKeys.DatabaseHealth,
                    Urgent: urgent));
            }

            if (problems.Length > MaxHealthLines)
            {
                items.Add(new AdminHomeLookAt(
                    Count(problems.Length - MaxHealthLines, "more system check needs a look", "more system checks need a look"),
                    "System health lists every check and what to do.",
                    "Open System health",
                    AdminNavigationKeys.DatabaseHealth));
            }
        }

        items.AddRange(attention.Select(item => new AdminHomeLookAt(
            item.Title,
            item.Detail,
            item.LinkText,
            item.NavigationKey,
            item.RouteValues,
            item.Severity == AdminAttentionSeverity.Critical)));

        // Only critical data-check findings need an administrator; warnings (an inactive value
        // still used by old records, for example) are expected and stay on the Data checks page.
        var criticalFindings = integrity?.Checks
            .Where(check => check.Status == MasterDataIntegrityStatus.Critical)
            .Sum(check => check.Findings.Count) ?? 0;
        if (criticalFindings > 0)
        {
            items.Add(new AdminHomeLookAt(
                Count(criticalFindings, "problem in the reference lists", "problems in the reference lists"),
                "Data checks found values that forms cannot use correctly.",
                "Fix",
                AdminNavigationKeys.MasterDataIntegrity));
        }

        return items;
    }

    public static AdminHomeStatus ResolveStatus(
        IReadOnlyList<AdminHomeLookAt> lookAt,
        AdminSystemHealthSnapshot? health,
        bool canSeeHealth,
        Func<DateTimeOffset, string> formatWhen)
    {
        var tail = lookAt.Count switch
        {
            0 => "Nothing needs you right now.",
            1 => "1 thing to look at below.",
            _ => $"{lookAt.Count} things to look at below."
        };

        if (!canSeeHealth)
        {
            return new AdminHomeStatus(lookAt.Count > 0 ? AdminHomeTone.Attention : AdminHomeTone.Calm, lookAt.Count > 0 ? "To look at" : "All clear", tail);
        }

        if (health is null)
        {
            return new AdminHomeStatus(
                lookAt.Count > 0 ? AdminHomeTone.Attention : AdminHomeTone.Unknown,
                "Not checked yet",
                $"The system checks have not run since PRISM last started. {tail}");
        }

        var when = formatWhen(health.CheckedUtc);
        return health.OverallStatus switch
        {
            AdminHealthStatus.Critical => new AdminHomeStatus(
                AdminHomeTone.Urgent,
                "Needs attention",
                $"{Count(health.CriticalCount, "system check", "system checks")} failed at {when}. {tail}"),
            AdminHealthStatus.Warning or AdminHealthStatus.Unavailable => new AdminHomeStatus(
                AdminHomeTone.Attention,
                "Running, with warnings",
                $"Some system checks need a look ({when}). {tail}"),
            _ => new AdminHomeStatus(
                lookAt.Count > 0 ? AdminHomeTone.Attention : AdminHomeTone.Calm,
                "Running normally",
                $"All system checks passed at {when}. {tail}")
        };
    }

    public static IReadOnlyList<AdminHomeTile> BuildTiles(
        AdminDashboardMetrics metrics,
        int restorable,
        AdminSystemHealthSnapshot? health,
        MasterDataOverviewSnapshot? lists,
        AdminMaintenanceSummary? jobs,
        Func<DateTimeOffset, string> formatWhen) =>
        new[]
        {
            new AdminHomeTile(
                AdminNavigationKeys.Users,
                "Add or manage users",
                "Create accounts, change roles, reset a password, unlock or disable someone.",
                "bi-people",
                metrics.LockedUsers > 0
                    ? $"{N(metrics.TotalUsers)} accounts · {N(metrics.LockedUsers)} locked"
                    : $"{N(metrics.TotalUsers)} accounts · {N(metrics.DisabledUsers)} disabled"),
            new AdminHomeTile(
                AdminNavigationKeys.Logins,
                "Check sign-ins and the audit log",
                "See who signed in, failed attempts, and every change made in PRISM.",
                "bi-box-arrow-in-right",
                $"{N(metrics.LoginsLast7Days)} sign-ins in 7 days · {N(metrics.FailedLoginsLast7Days)} failed"),
            new AdminHomeTile(
                AdminNavigationKeys.RecoveryCentre,
                "Restore something deleted",
                "Bring back a deleted project, document, calendar event or chat file.",
                "bi-arrow-counterclockwise",
                restorable switch
                {
                    0 => "Nothing deleted to restore",
                    1 => "1 deleted item can be restored",
                    _ => $"{N(restorable)} deleted items can be restored"
                }),
            new AdminHomeTile(
                AdminNavigationKeys.MasterDataCentre,
                "Edit the lists used in forms",
                "Categories, project types, sponsoring units, activity types and more.",
                "bi-list-check",
                lists is null
                    ? NoFigure
                    : $"{N(lists.Domains.Count)} lists · {N(lists.ActiveRecords)} active values"),
            new AdminHomeTile(
                AdminNavigationKeys.DatabaseHealth,
                "Check that PRISM is healthy",
                "Database, storage, background jobs and the search index.",
                "bi-heart-pulse",
                health switch
                {
                    null => "Not checked since PRISM started",
                    { OverallStatus: AdminHealthStatus.Healthy } => $"All checks passed · {formatWhen(health.CheckedUtc)}",
                    { OverallStatus: AdminHealthStatus.Critical } => $"{Count(health.CriticalCount, "check", "checks")} failed · {formatWhen(health.CheckedUtc)}",
                    _ => $"{Count(health.WarningCount + health.UnavailableCount, "check needs", "checks need")} a look · {formatWhen(health.CheckedUtc)}"
                }),
            new AdminHomeTile(
                AdminNavigationKeys.MaintenanceCentre,
                "Run a data job",
                "Copy FFC, IPR and activity PDFs into the document repository, or import legacy projects.",
                "bi-cloud-arrow-up",
                jobs switch
                {
                    null => NoFigure,
                    { PdfIngestionRunning: true } => "A PDF run is in progress",
                    { LastPdfRun: { } run } => $"Last PDF run: {run.Status.ToLowerInvariant()} · {formatWhen(run.CompletedAtUtc)}",
                    _ => "No PDF run since PRISM started"
                })
        };

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Count(int value, string one, string many) =>
        value == 1 ? $"1 {one}" : $"{N(value)} {many}";
}
