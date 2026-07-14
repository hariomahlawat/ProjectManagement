using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Recovery;

[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminRecoverySummaryService _summary;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAdminTimeService _time;

    public IndexModel(
        IAdminRecoverySummaryService summary,
        IAdminNavigationUrlBuilder navigation,
        IAdminTimeService time)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();
    public AdminRecoverySummary Summary { get; private set; } =
        new(0, 0, 0, 0, 0, 0, 7, 0, 0, Array.Empty<AdminRecoveryOperation>());

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await _summary.GetAsync(cancellationToken);
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Recovery and retention",
            Title = "Recovery centre",
            Description = "Review recoverable records, retention deadlines and controlled permanent-deletion activity.",
            Icon = "bi-arrow-counterclockwise",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Maintenance centre",
                    Href = Path(AdminNavigationKeys.MaintenanceCentre),
                    Icon = "bi-tools"
                }
            }
        };
    }

    public string? Path(string key) => _navigation.GetPath(HttpContext, key);
    public string FormatTime(DateTimeOffset value) => _time.FormatIst(value);
    public string FormatBytes(long bytes)
    {
        var size = (double)Math.Max(0, bytes);
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
