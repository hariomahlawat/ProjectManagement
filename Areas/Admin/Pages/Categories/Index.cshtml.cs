using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.Categories;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminMasterDataCommandService _commands;

    public IndexModel(ApplicationDbContext db, IAdminMasterDataCommandService commands)
    {
        _db = db;
        _commands = commands;
    }

    [TempData(Key = FlashMessageKeys.AdminMasterDataSuccess)]
    public string? StatusMessage { get; set; }

    [TempData(Key = FlashMessageKeys.AdminMasterDataError)]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<CategoryHierarchyBuilder.CategoryNode<ProjectCategory>> Nodes { get; private set; }
        = Array.Empty<CategoryHierarchyBuilder.CategoryNode<ProjectCategory>>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Nodes = await CategoryHierarchyBuilder.LoadHierarchyAsync(
            _db.ProjectCategories,
            item => item.Id,
            item => item.ParentId,
            item => item.SortOrder,
            item => item.Name);
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _commands.ToggleProjectCategoryAsync(id, cancellationToken);
        return RedirectWithResult(result);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int offset, CancellationToken cancellationToken)
    {
        var result = await _commands.MoveProjectCategoryAsync(id, offset, cancellationToken);
        return RedirectWithResult(result);
    }

    private IActionResult RedirectWithResult(AdminOperationResult result)
    {
        TempData[result.Succeeded ? FlashMessageKeys.AdminMasterDataSuccess : FlashMessageKeys.AdminMasterDataError] = result.UserMessage;
        return RedirectToPage();
    }
}
