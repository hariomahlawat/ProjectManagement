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

    [BindProperty(SupportsGet = true)]
    public bool Refresh { get; set; }

    public AdminSystemHealthSnapshot Snapshot { get; private set; } = EmptySnapshot();
    public AdminPageHeaderModel Header { get; private set; } = new();
    public IReadOnlyList<AdminMonitoringMetricModel> Metrics { get; private set; } = Array.Empty<AdminMonitoringMetricModel>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _health.CheckAsync(Refresh, cancellationToken);
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
        AdminHealthStatus.Warning => "Attention required",
        AdminHealthStatus.Critical => "Critical",
        _ => "Unavailable"
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
            Eyebrow = "Monitoring",
            Title = "System health",
            Description = "Verify database, migration, storage, security and background-service readiness.",
            Icon = "bi-heart-pulse",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Run checks again",
                    Href = Url.Page("./DbHealth", new { Refresh = true }),
                    Icon = "bi-arrow-clockwise",
                    IsPrimary = true
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
                Detail = "Review recommended",
                Icon = "bi-exclamation-triangle",
                Tone = Snapshot.WarningCount > 0 ? "warning" : "neutral"
            },
            new AdminMonitoringMetricModel
            {
                Label = "Critical",
                Value = Snapshot.CriticalCount.ToString("N0"),
                Detail = "Immediate action required",
                Icon = "bi-x-octagon",
                Tone = Snapshot.CriticalCount > 0 ? "danger" : "neutral"
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
        "Background services" => 3,
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
