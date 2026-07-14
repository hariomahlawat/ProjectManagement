using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class IndexModel : PageModel
{
    private readonly IMasterDataAdministrationQueryService _query;
    private readonly IAdminMasterDataCommandService _commands;

    public IndexModel(
        IMasterDataAdministrationQueryService query,
        IAdminMasterDataCommandService commands)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    public CategoryDirectoryResult Result { get; private set; } = new(
        MasterDataCategoryKind.Technical,
        Array.Empty<CategoryAdminRow>(),
        0, 0, 0, 0, 0,
        string.Empty,
        "active");

    public AdminPageHeaderModel Header { get; private set; } = new();
    public AdminCategoryDirectoryModel DirectoryModel { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await _query.GetCategoriesAsync(
            MasterDataCategoryKind.Technical,
            new CategoryDirectoryRequest(Search, Status),
            cancellationToken);
        Search = Result.Search;
        Status = Result.Status;
        BuildPresentation();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _commands.ToggleTechnicalCategoryAsync(id, cancellationToken);
        return RedirectWithResult(result);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int offset, CancellationToken cancellationToken)
    {
        var result = await _commands.MoveTechnicalCategoryAsync(id, offset, cancellationToken);
        return RedirectWithResult(result);
    }

    private IActionResult RedirectWithResult(AdminOperationResult result)
    {
        TempData[result.Succeeded ? FlashMessageKeys.AdminMasterDataSuccess : FlashMessageKeys.AdminMasterDataError] = result.UserMessage;
        return RedirectToPage(new { q = Search, status = Status });
    }

    private void BuildPresentation()
    {
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Master data · Taxonomy",
            Title = "Technical categories",
            Description = "Maintain the technology taxonomy used for portfolio analysis and capability reporting.",
            Icon = "bi-cpu",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Master data centre",
                    Href = Url.Page("/MasterData/Index", new { area = "Admin" }),
                    Icon = "bi-arrow-left"
                },
                new AdminPageActionModel
                {
                    Text = "Add root category",
                    Href = Url.Page("./Create", new { area = "Admin" }),
                    Icon = "bi-plus-lg",
                    IsPrimary = true
                }
            }
        };

        DirectoryModel = new AdminCategoryDirectoryModel
        {
            Result = Result,
            Area = "Admin",
            IndexPage = "/TechnicalCategories/Index",
            CreatePage = "/TechnicalCategories/Create",
            EditPage = "/TechnicalCategories/Edit",
            DeletePage = "/TechnicalCategories/Delete"
        };
    }
}
