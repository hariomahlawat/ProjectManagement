using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Diagnostics;

[Authorize(Policy = AdminPolicies.SecurityView)]
public sealed class DbHealthModel : PageModel
{
    private readonly IDatabaseHealthService _health;

    public DbHealthModel(IDatabaseHealthService health)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
    }

    public DatabaseHealthSnapshot Snapshot { get; private set; } = new(
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
        Array.Empty<DatabaseHealthCheck>());

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _health.CheckAsync(cancellationToken);
    }

    public static string StatusCss(AdminHealthStatus status) => status switch
    {
        AdminHealthStatus.Healthy => "text-bg-success",
        AdminHealthStatus.Warning => "text-bg-warning",
        AdminHealthStatus.Critical => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    public static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "—";
        }

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
}
