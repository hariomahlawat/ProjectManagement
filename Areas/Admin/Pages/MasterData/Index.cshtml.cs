using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.MasterData;

/// <summary>
/// "Lists used in forms" (docs/ux/admin-module.md, PR 6): one card per list, grouped by where it
/// is used, with its value count and the form that offers it. Each card opens the list's own page.
/// </summary>
[Authorize(Policy = AdminPolicies.MasterDataManage)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private readonly IMasterDataAdministrationQueryService _masterData;
    private readonly IAdminTimeService _time;
    private readonly IAuthorizationService _authorization;

    public IndexModel(
        IMasterDataAdministrationQueryService masterData,
        IAdminTimeService time,
        IAuthorizationService authorization)
    {
        _masterData = masterData ?? throw new ArgumentNullException(nameof(masterData));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public MasterDataOverviewSnapshot Snapshot { get; private set; } = new(
        Array.Empty<MasterDataDomainSummary>(),
        Array.Empty<MasterDataRecentChange>(),
        0, 0, 0, 0);

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<ListGroupViewModel> Groups { get; private set; } = Array.Empty<ListGroupViewModel>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Snapshot = await _masterData.GetOverviewAsync(_time.TodayIst.Year, cancellationToken);

        // A card only for a list the viewer can open: each list keeps its own permission.
        var visible = new List<MasterDataDomainSummary>();
        foreach (var domain in Snapshot.Domains)
        {
            if (string.IsNullOrWhiteSpace(domain.Policy)
                || (await _authorization.AuthorizeAsync(User, domain.Policy)).Succeeded)
            {
                visible.Add(domain);
            }
        }

        Groups = MasterDataGroups.Ordered
            .Select(group => new ListGroupViewModel(
                group,
                visible.Where(domain => string.Equals(domain.Group, group, StringComparison.Ordinal)).ToArray()))
            .Where(group => group.Lists.Count > 0)
            .ToArray();

        Header = new AdminPageHeaderModel
        {
            Title = "Lists used in forms",
            Description = "The choices people pick from in PRISM's forms. Open a list to add, rename or reorder its values.",
            Icon = "bi-list-check",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Run data checks",
                    Href = Url.Page("/MasterData/Integrity/Index", new { area = "Admin" }),
                    Icon = "bi-clipboard2-check"
                }
            }
        };
    }

    public string DomainUrl(MasterDataDomainSummary domain) =>
        Url.Page(domain.Page, new { area = domain.Area }) ?? "#";

    /// <summary>
    /// Names the module a list opens in when it leaves Administration; null for a list that opens
    /// inside Administration, as the repository and social media lists now do.
    /// </summary>
    public static string? OpensIn(MasterDataDomainSummary domain)
    {
        if (AdminNavigationCatalog.IsInScope(domain.Area, domain.Page, null, null))
        {
            return null;
        }

        return domain.Area switch
        {
            "DocumentRepository" => "the Document Repository",
            "ProjectOfficeReports" => "Project office reports",
            _ => null
        };
    }

    public static string Values(MasterDataDomainSummary domain) =>
        (domain.Total == 1 ? "1 value" : $"{domain.Total:N0} values")
        + (domain.Inactive > 0 ? $" · {domain.Inactive:N0} deactivated" : string.Empty);

    public string FormatIst(DateTimeOffset whenUtc) => _time.FormatIst(whenUtc);

    public sealed record ListGroupViewModel(string Name, IReadOnlyList<MasterDataDomainSummary> Lists);
}
