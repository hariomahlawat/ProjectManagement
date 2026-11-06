using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Maintenance;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Maintenance;

[Authorize(Policy = AdminPolicies.IngestionManage)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminMaintenanceSummaryService _summary;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAdminTimeService _time;

    public IndexModel(
        IAdminMaintenanceSummaryService summary,
        IAdminNavigationUrlBuilder navigation,
        IAdminTimeService time)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();
    public AdminMaintenanceSummary Summary { get; private set; } = new(false, null, 0, 0, null, null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await _summary.GetAsync(cancellationToken);
        Header = new AdminPageHeaderModel
        {
            Title = "Data jobs",
            Description = "Jobs that bring data into PRISM in bulk. Each one checks its input first and records what it did in the audit log.",
            Icon = "bi-tools"
        };
    }

    public string? Path(string key) => _navigation.GetPath(HttpContext, key);
    public string FormatTime(DateTimeOffset? value) => _time.FormatIst(value);
    public string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{duration.TotalMinutes:0.#} min"
        : $"{Math.Max(0, duration.TotalSeconds):0} sec";
}
