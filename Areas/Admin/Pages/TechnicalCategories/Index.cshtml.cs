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

    // The form sends the state the administrator chose and the row version it showed, so a
    // resubmitted form or another administrator's change cannot flip the item the wrong way.
    public async Task<IActionResult> OnPostToggleAsync(int id, bool isActive, string? rowVersion, CancellationToken cancellationToken)
    {
        var result = await _commands.SetTechnicalCategoryActiveAsync(id, isActive, DecodeRowVersion(rowVersion), cancellationToken);
        return RedirectWithResult(result);
    }

    private static byte[] DecodeRowVersion(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? Array.Empty<byte>() : Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
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
            Title = "Technical categories",
            Description = ProjectManagement.Services.Navigation.ModuleNav.AdminNavigationCatalog.GetEntry(ProjectManagement.Services.Navigation.ModuleNav.AdminNavigationKeys.TechnicalCategories).Description,
            Icon = "bi-cpu",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Lists used in forms",
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
