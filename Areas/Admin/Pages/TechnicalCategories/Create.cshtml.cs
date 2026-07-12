using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminMasterDataCommandService _commands;

    public CreateModel(ApplicationDbContext db, IAdminMasterDataCommandService commands)
    {
        _db = db;
        _commands = commands;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IList<SelectListItem> ParentOptions { get; private set; } = new List<SelectListItem>();

    public async Task OnGetAsync(int? parentId, CancellationToken cancellationToken)
    {
        Input.ParentId = parentId;
        ParentOptions = await LoadParentOptionsAsync(parentId, null);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ParentOptions = await LoadParentOptionsAsync(Input.ParentId, null);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _commands.CreateTechnicalCategoryAsync(
            new CategoryCreateCommand(Input.Name, Input.ParentId, Input.IsActive),
            cancellationToken);

        if (!result.Succeeded)
        {
            var key = result.ErrorCode is "ParentNotFound" or "SelfParent" or "DescendantParent" or "HierarchyCycleDetected"
                ? "Input.ParentId"
                : "Input.Name";
            ModelState.AddModelError(key, result.UserMessage ?? "The technical category could not be created.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("Index");
    }

    private Task<List<SelectListItem>> LoadParentOptionsAsync(int? selectedId, int? excludeId) =>
        CategoryHierarchyBuilder.BuildSelectListAsync(
            _db.TechnicalCategories,
            selectedId,
            excludeId,
            item => item.Id,
            item => item.ParentId,
            item => item.SortOrder,
            item => item.Name);

    public sealed class InputModel
    {
        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
