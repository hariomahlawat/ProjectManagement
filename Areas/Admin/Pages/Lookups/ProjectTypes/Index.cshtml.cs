using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.ProjectTypes;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class IndexModel : PageModel
{
    private readonly IMasterDataAdministrationQueryService _query;
    private readonly IAdminMasterDataCommandService _commands;
    private readonly IListOrderNoticeService _order;

    public IndexModel(IMasterDataAdministrationQueryService query, IAdminMasterDataCommandService commands, IListOrderNoticeService order)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _order = order ?? throw new ArgumentNullException(nameof(order));
    }

    public ListOrderNotice OrderNotice { get; private set; } = ListOrderNotice.None(AdminMasterDataIntegrityService.ProjectTypeOrderKey);

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public FlatLookupDirectoryResult Result { get; private set; } = new(
        MasterDataFlatLookupKind.ProjectType,
        Array.Empty<FlatLookupAdminRow>(),
        0, 0, 0, 0, 0,
        1, 25, 1,
        string.Empty,
        "active");

    public AdminPageHeaderModel Header { get; private set; } = new();
    public AdminFlatLookupDirectoryModel DirectoryModel { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await _query.GetFlatLookupAsync(
            MasterDataFlatLookupKind.ProjectType,
            new FlatLookupDirectoryRequest(Search, Status, PageNumber, PageSize),
            cancellationToken);
        Search = Result.Search;
        Status = Result.Status;
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
        OrderNotice = await _order.GetAsync(User, AdminMasterDataIntegrityService.ProjectTypeOrderKey, cancellationToken);
        BuildPresentation();
    }

    public async Task<IActionResult> OnPostRenumberOrderAsync(CancellationToken cancellationToken)
    {
        var result = await _order.RenumberAsync(User, AdminMasterDataIntegrityService.ProjectTypeOrderKey, cancellationToken);
        TempData[result.Succeeded ? FlashMessageKeys.AdminMasterDataSuccess : FlashMessageKeys.AdminMasterDataError] = result.UserMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int offset, CancellationToken cancellationToken)
    {
        var result = await _commands.MoveProjectTypeAsync(id, offset, cancellationToken);
        TempData[result.Succeeded ? FlashMessageKeys.AdminMasterDataSuccess : FlashMessageKeys.AdminMasterDataError] = result.UserMessage;
        return RedirectToPage(new { q = Search, status = Status, pageNumber = Math.Max(1, PageNumber), pageSize = PageSize });
    }

    private void BuildPresentation()
    {
        Header = new AdminPageHeaderModel
        {
            Title = "Project types",
            Description = ProjectManagement.Services.Navigation.ModuleNav.AdminNavigationCatalog.GetEntry(ProjectManagement.Services.Navigation.ModuleNav.AdminNavigationKeys.ProjectTypes).Description,
            Icon = "bi-tags",
            Actions = new[]
            {
                new AdminPageActionModel { Text = "Lists used in forms", Href = Url.Page("/MasterData/Index", new { area = "Admin" }), Icon = "bi-arrow-left" },
                new AdminPageActionModel { Text = "Add project type", Href = Url.Page("./Create", new { area = "Admin" }), Icon = "bi-plus-lg", IsPrimary = true }
            }
        };
        DirectoryModel = new AdminFlatLookupDirectoryModel
        {
            Result = Result,
            SingularLabel = "project type",
            PluralLabel = "Project types",
            Description = "project classification used in project records",
            Icon = "bi-tags",
            IndexPage = "/Lookups/ProjectTypes/Index",
            CreatePage = "/Lookups/ProjectTypes/Create",
            EditPage = "/Lookups/ProjectTypes/Edit",
            DeactivatePage = "/Lookups/ProjectTypes/Deactivate"
        };
    }
}
