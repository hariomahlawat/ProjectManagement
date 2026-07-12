using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories;

[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAdminMasterDataCommandService _commands;

    public DeleteModel(ApplicationDbContext db, IAdminMasterDataCommandService commands)
    {
        _db = db;
        _commands = commands;
    }

    public TechnicalCategory? Category { get; private set; }

    [BindProperty]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Category = await _db.TechnicalCategories
            .Include(item => item.Parent)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (Category is null)
        {
            return NotFound();
        }

        RowVersion = Category.RowVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _commands.DeleteTechnicalCategoryAsync(id, RowVersion, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.ErrorCode == "NotFound")
            {
                return NotFound();
            }

            Category = await _db.TechnicalCategories
                .Include(item => item.Parent)
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            ModelState.AddModelError(string.Empty, result.UserMessage ?? "The technical category could not be deleted.");
            return Page();
        }

        TempData[FlashMessageKeys.AdminMasterDataSuccess] = result.UserMessage;
        return RedirectToPage("Index");
    }
}
