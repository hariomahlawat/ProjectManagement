using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories;

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

    public IReadOnlyList<CategoryNode> Nodes { get; private set; } = Array.Empty<CategoryNode>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var nodes = await CategoryHierarchyBuilder.LoadHierarchyAsync(
            _db.TechnicalCategories,
            item => item.Id,
            item => item.ParentId,
            item => item.SortOrder,
            item => item.Name);

        var usageCounts = await _db.Projects
            .Where(project => project.TechnicalCategoryId != null)
            .GroupBy(project => project.TechnicalCategoryId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken);

        CategoryNode Map(CategoryHierarchyBuilder.CategoryNode<TechnicalCategory> node) =>
            new(
                node.Category,
                usageCounts.TryGetValue(node.Category.Id, out var count) ? count : 0,
                node.Children.Select(Map).ToList());

        Nodes = nodes.Select(Map).ToList();
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
        return RedirectToPage();
    }

    public sealed record CategoryNode(TechnicalCategory Category, int ProjectCount, IReadOnlyList<CategoryNode> Children);
}
