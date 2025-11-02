using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.DocumentCategories;

[Authorize(Policy = "DocRepo.ManageCategories")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<DocumentCategory> Categories { get; private set; } = Array.Empty<DocumentCategory>();

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    public class CreateInput
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [Range(-999, 9999)]
        public int SortOrder { get; set; } = 100;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Categories = await _db.DocumentCategories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var trimmedName = Input.Name.Trim();
        var exists = await _db.DocumentCategories
            .AnyAsync(c => c.Name == trimmedName, cancellationToken);
        if (exists)
        {
            ModelState.AddModelError(nameof(Input.Name), "A category with this name already exists.");
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var category = new DocumentCategory
        {
            Name = trimmedName,
            SortOrder = Input.SortOrder
        };

        _db.DocumentCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "Document category created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, int sortOrder, CancellationToken cancellationToken)
    {
        var category = await _db.DocumentCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 120)
        {
            ModelState.AddModelError("Edit", "Name is required and must be 120 characters or fewer.");
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var duplicate = await _db.DocumentCategories
            .AnyAsync(c => c.Id != id && c.Name == trimmedName, cancellationToken);
        if (duplicate)
        {
            ModelState.AddModelError("Edit", "Another category already uses this name.");
            await OnGetAsync(cancellationToken);
            return Page();
        }

        category.Name = trimmedName;
        category.SortOrder = sortOrder;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "Document category updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken cancellationToken)
    {
        var category = await _db.DocumentCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = !category.IsActive;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = category.IsActive ? "Category activated." : "Category deactivated.";
        return RedirectToPage();
    }
}
