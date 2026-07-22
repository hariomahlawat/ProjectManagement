using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.Categories;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminMasterDataCommandService _commands;

    public EditModel(ApplicationDbContext db, IAdminMasterDataCommandService commands)
    {
        _db = db;
        _commands = commands;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IList<SelectListItem> ParentOptions { get; private set; } = new List<SelectListItem>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var category = await _db.ProjectCategories.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = category.Id,
            Name = category.Name,
            ParentId = category.ParentId,
            IsActive = category.IsActive,
            RowVersion = category.RowVersion
        };

        ParentOptions = await CategorySelectListBuilder.BuildAsync(_db, category.ParentId, category.Id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ParentOptions = await CategorySelectListBuilder.BuildAsync(_db, Input.ParentId, Input.Id);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _commands.UpdateProjectCategoryAsync(
            new CategoryUpdateCommand(Input.Id, Input.Name, Input.ParentId, Input.IsActive, Input.RowVersion),
            cancellationToken);

        if (!result.Succeeded)
        {
            if (result.ErrorCode == "NotFound")
            {
                return NotFound();
            }

            var key = result.ErrorCode is "ParentNotFound" or "SelfParent" or "DescendantParent" or "HierarchyCycleDetected"
                ? "Input.ParentId"
                : result.ErrorCode == "DuplicateName" ? "Input.Name" : string.Empty;
            ModelState.AddModelError(key, result.UserMessage ?? "The category could not be updated.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("Index");
    }

    public sealed class InputModel
    {
        [Required]
        public int Id { get; set; }

        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public bool IsActive { get; set; }

        [Required]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
