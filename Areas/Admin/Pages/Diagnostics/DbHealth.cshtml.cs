using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Diagnostics;

[Authorize(Policy = AdminPolicies.SecurityView)]
[ResponseCache(NoStore = true)]
public sealed class DbHealthModel : PageModel
{
    private readonly IAdminSystemHealthService _health;
    private readonly IAdminTimeService _time;

    public DbHealthModel(IAdminSystemHealthService health, IAdminTimeService time)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public AdminSystemHealthSnapshot Snapshot { get; private set; } = EmptySnapshot();
    public AdminPageHeaderModel Header { get; private set; } = new();
    public IReadOnlyList<AdminMonitoringMetricModel> Metrics { get; private set; } = Array.Empty<AdminMonitoringMetricModel>();

    // Viewing the page uses the cached checks. Running them again (probe files written to
    // disk, checks of up to 10 s each) is a POST: it used to be "?Refresh=true", which any
    // link, bookmark or repeated reload could trigger to bypass the cache.
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _health.CheckAsync(forceRefresh: false, cancellationToken);
        BuildPresentation();
        return Page();
    }

    public async Task<IActionResult> OnPostRefreshAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _health.CheckAsync(forceRefresh: true, cancellationToken);
        BuildPresentation();
        return Page();
    }

    public IEnumerable<IGrouping<string, AdminSystemHealthCheck>> CheckGroups => Snapshot.Checks
        .GroupBy(check => check.Group)
        .OrderBy(group => GroupOrder(group.Key))
        .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

    public string FormatIst(DateTimeOffset utc) => _time.FormatIst(utc);

    public string StatusTone(AdminHealthStatus status) => status switch
    {
        AdminHealthStatus.Healthy => "success",
        AdminHealthStatus.Warning => "warning",
        AdminHealthStatus.Critical => "danger",
        _ => "neutral"
    };

    public string StatusIcon(AdminHealthStatus status) => status switch
    {
        AdminHealthStatus.Healthy => "bi-check-circle",
        AdminHealthStatus.Warning => "bi-exclamation-triangle",
        AdminHealthStatus.Critical => "bi-x-octagon",
        _ => "bi-dash-circle"
    };

    public string StatusLabel(AdminHealthStatus status) => status switch
    {
        AdminHealthStatus.Healthy => "Healthy",
        AdminHealthStatus.Warning => "Some checks need a look",
        AdminHealthStatus.Critical => "Something is not working",
        _ => "Not run yet"
    };

    public string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue) return "—";
        var size = (double)bytes.Value;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var index = 0;
        while (size >= 1024 && index < units.Length - 1)
        {
            size /= 1024;
            index++;
        }
        return $"{size:0.##} {units[index]}";
    }

    private void BuildPresentation()
    {
        Header = new AdminPageHeaderModel
        {
            Title = "System health",
            Description = "Whether PRISM's database, file storage, search and background jobs are working. Run the checks again after fixing something.",
            Icon = "bi-heart-pulse",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Run checks again",
                    Href = Url.Page("./DbHealth", "Refresh"),
                    Icon = "bi-arrow-clockwise",
                    IsPrimary = true,
                    UsesPost = true
                }
            }
        };

        Metrics = new[]
        {
            new AdminMonitoringMetricModel
            {
                Label = "Overall status",
                Value = StatusLabel(Snapshot.OverallStatus),
                Detail = $"Checked {FormatIst(Snapshot.CheckedUtc)}",
                Icon = StatusIcon(Snapshot.OverallStatus),
                Tone = StatusTone(Snapshot.OverallStatus)
            },
            new AdminMonitoringMetricModel
            {
                Label = "Healthy checks",
                Value = Snapshot.HealthyCount.ToString("N0"),
                Detail = $"{Snapshot.Checks.Count:N0} checks completed",
                Icon = "bi-check2-circle",
                Tone = "success"
            },
            new AdminMonitoringMetricModel
            {
                Label = "Warnings",
                Value = Snapshot.WarningCount.ToString("N0"),
                Detail = "Worth a look",
                Icon = "bi-exclamation-triangle",
                Tone = Snapshot.WarningCount > 0 ? "warning" : "neutral"
            },
            new AdminMonitoringMetricModel
            {
                Label = "Critical",
                Value = Snapshot.CriticalCount.ToString("N0"),
                Detail = "Need fixing now",
                Icon = "bi-x-octagon",
                Tone = Snapshot.CriticalCount > 0 ? "danger" : "neutral"
            },
            new AdminMonitoringMetricModel
            {
                Label = "Not run yet",
                Value = Snapshot.UnavailableCount.ToString("N0"),
                Detail = "Jobs that have not run since PRISM started",
                Icon = "bi-hourglass-split",
                Tone = "neutral"
            },
            new AdminMonitoringMetricModel
            {
                Label = "Application",
                Value = Snapshot.ApplicationVersion,
                Detail = Snapshot.EnvironmentName,
                Icon = "bi-box-seam",
                Tone = "neutral"
            }
        };
    }

    private static int GroupOrder(string group) => group switch
    {
        "Database" => 0,
        "Storage" => 1,
        "Application security" => 2,
        "Search" => 3,
        "Media library" => 4,
        "Background services" => 5,
        _ => 9
    };

    private static AdminSystemHealthSnapshot EmptySnapshot() => new(
        AdminHealthStatus.Unavailable,
        DateTimeOffset.UtcNow,
        "Unknown",
        "Unknown",
        Array.Empty<AdminSystemHealthCheck>(),
        new DatabaseHealthSnapshot(
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "(not available)",
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<DatabaseHealthCheck>()));
}
