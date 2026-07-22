using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Areas.Admin.Pages.Projects;

[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class ArchivedModel : PageModel
{
    private readonly IProjectRecoveryQueryService _query;
    private readonly ProjectModerationService _moderation;
    private readonly IAdminNavigationUrlBuilder _navigation;
    private readonly IAdminTimeService _time;

    public ArchivedModel(
        IProjectRecoveryQueryService query,
        ProjectModerationService moderation,
        IAdminNavigationUrlBuilder navigation,
        IAdminTimeService time)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty] public int RestoreProjectId { get; set; }

    public AdminPageHeaderModel Header { get; private set; } = new();
    public ProjectRecoveryPage<ArchivedProjectRow> Result { get; private set; } =
        new(Array.Empty<ArchivedProjectRow>(), 0, 1, 25);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Normalize();
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRestoreAsync(CancellationToken cancellationToken)
    {
        Normalize();
        if (RestoreProjectId <= 0)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "The selected archived project is invalid.";
            return RedirectToPage(RouteValues());
        }

        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) return Challenge();
        var result = await _moderation.RestoreFromArchiveAsync(RestoreProjectId, actor, cancellationToken);
        TempData[result.Status == ProjectModerationStatus.Success
            ? FlashMessageKeys.AdminRecoverySuccess
            : FlashMessageKeys.AdminRecoveryError] = result.Status == ProjectModerationStatus.Success
                ? "Project restored to the active portfolio."
                : result.Error ?? "The archived project could not be restored.";
        return RedirectToPage(RouteValues());
    }

    public string FormatTime(DateTimeOffset? value) => _time.FormatIst(value);
    public string StageName(string? code) => string.IsNullOrWhiteSpace(code) ? "Stage unavailable" : StageCodes.DisplayNameOf(code);
    public string PageUrl(int page) => Url.Page(null, new { Search, PageNumber = page, PageSize }) ?? "#";

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Result = await _query.QueryArchivedAsync(new ProjectArchiveQuery(Search, PageNumber, PageSize), cancellationToken);
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Recovery and retention",
            Title = "Archived projects",
            Description = "Review projects removed from active operational views and restore them without changing their lifecycle records.",
            Icon = "bi-archive",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Recovery centre",
                    Href = _navigation.GetPath(HttpContext, AdminNavigationKeys.RecoveryCentre),
                    Icon = "bi-arrow-left"
                }
            }
        };
    }

    private void Normalize()
    {
        Search = Search?.Trim();
        if (string.IsNullOrWhiteSpace(Search)) Search = null;
        else if (Search.Length > 160) Search = Search[..160];
        PageNumber = Math.Max(1, PageNumber);
        PageSize = PageSize is 10 or 25 or 50 or 100 ? PageSize : 25;
    }

    private object RouteValues() => new { Search, PageNumber, PageSize };
}
