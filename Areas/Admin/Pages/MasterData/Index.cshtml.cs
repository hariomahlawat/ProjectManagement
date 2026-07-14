using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.MasterData;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private readonly IMasterDataAdministrationQueryService _masterData;
    private readonly IAdminTimeService _time;

    public IndexModel(
        IMasterDataAdministrationQueryService masterData,
        IAdminTimeService time)
    {
        _masterData = masterData ?? throw new ArgumentNullException(nameof(masterData));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public MasterDataOverviewSnapshot Snapshot { get; private set; } = new(
        Array.Empty<MasterDataDomainSummary>(),
        Array.Empty<MasterDataRecentChange>(),
        0, 0, 0, 0);

    public AdminPageHeaderModel Header { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _masterData.GetOverviewAsync(_time.TodayIst.Year, cancellationToken);
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Configuration governance",
            Title = "Master data",
            Description = "Maintain controlled taxonomies, reference lists and calendar configuration used throughout PRISM.",
            Icon = "bi-sliders2",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Integrity review",
                    Href = Url.Page("/MasterData/Integrity/Index", new { area = "Admin" }),
                    Icon = "bi-clipboard2-check",
                    IsPrimary = true
                },
                new AdminPageActionModel
                {
                    Text = "Administration guide",
                    Href = Url.Page("/Help/Index", new { area = "Admin", section = "master-data" }),
                    Icon = "bi-question-circle"
                }
            }
        };
    }

    public string DomainUrl(MasterDataDomainSummary domain) =>
        Url.Page(domain.Page, new { area = domain.Area }) ?? "#";

    public string FormatIst(DateTimeOffset whenUtc) => _time.FormatIst(whenUtc);
}
