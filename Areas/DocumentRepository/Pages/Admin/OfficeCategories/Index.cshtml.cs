using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.OfficeCategories;

[Authorize(Policy = "DocRepo.ManageCategories")]
public class IndexModel : PageModel
{
    private const string CategoryKind = "office";

    private readonly ApplicationDbContext _db;
    private readonly IDocRepoAuditService _audit;
    private readonly IListOrderNoticeService _order;

    public IndexModel(ApplicationDbContext db, IDocRepoAuditService audit, IListOrderNoticeService order)
    {
        _db = db;
        _audit = audit;
        _order = order;
    }

    /// <summary>Shown when positions have gaps or repeats (for example every row at 100).</summary>
    public ListOrderNotice OrderNotice { get; private set; } = ListOrderNotice.None(AdminMasterDataIntegrityService.OfficeCategoryOrderKey);

    private string ActorUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

    public IReadOnlyList<OfficeCategory> Categories { get; private set; } = Array.Empty<OfficeCategory>();

    /// <summary>Documents filed under each category (trash excluded), so nobody retires one in use without knowing.</summary>
    public IReadOnlyDictionary<int, int> DocumentCounts { get; private set; } = new Dictionary<int, int>();

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    public class CreateInput
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Blank puts the new entry at the end of the list.</summary>
        [Range(-999, 9999)]
        public int? SortOrder { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Categories = await _db.OfficeCategories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        DocumentCounts = await _db.Documents
            .AsNoTracking()
            .Where(d => !d.IsDeleted)
            .GroupBy(d => d.OfficeCategoryId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);

        OrderNotice = await _order.GetAsync(User, AdminMasterDataIntegrityService.OfficeCategoryOrderKey, cancellationToken);
    }

    public async Task<IActionResult> OnPostRenumberOrderAsync(CancellationToken cancellationToken)
    {
        var result = await _order.RenumberAsync(User, AdminMasterDataIntegrityService.OfficeCategoryOrderKey, cancellationToken);
        TempData["ToastMessage"] = result.UserMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var trimmedName = Input.Name.Trim();
        var exists = await _db.OfficeCategories
            .AnyAsync(c => c.Name == trimmedName, cancellationToken);
        if (exists)
        {
            ModelState.AddModelError(nameof(Input.Name), "A category with this name already exists.");
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var category = new OfficeCategory
        {
            Name = trimmedName,
            // A new entry goes to the end unless a position is typed. The old default of 100
            // gave every entry the same position, so the list had no real order.
            SortOrder = Input.SortOrder ?? (await _db.OfficeCategories
                .MaxAsync(c => (int?)c.SortOrder, cancellationToken) ?? 0) + 1
        };

        _db.OfficeCategories.Add(category);
        _audit.Stage(null, ActorUserId, DocRepoAuditEventTypes.CategoryChanged,
            new { kind = CategoryKind, action = "Created", category.Name, category.SortOrder });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "Office category created.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, int sortOrder, CancellationToken cancellationToken)
    {
        var category = await _db.OfficeCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
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

        var duplicate = await _db.OfficeCategories
            .AnyAsync(c => c.Id != id && c.Name == trimmedName, cancellationToken);
        if (duplicate)
        {
            ModelState.AddModelError("Edit", "Another category already uses this name.");
            await OnGetAsync(cancellationToken);
            return Page();
        }

        _audit.Stage(null, ActorUserId, DocRepoAuditEventTypes.CategoryChanged,
            new { kind = CategoryKind, action = "Updated", category.Id, from = new { category.Name, category.SortOrder }, to = new { Name = trimmedName, SortOrder = sortOrder } });
        category.Name = trimmedName;
        category.SortOrder = sortOrder;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "Office category updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken cancellationToken)
    {
        var category = await _db.OfficeCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = !category.IsActive;
        _audit.Stage(null, ActorUserId, DocRepoAuditEventTypes.CategoryChanged,
            new { kind = CategoryKind, action = category.IsActive ? "Activated" : "Deactivated", category.Id, category.Name });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = category.IsActive ? "Category activated." : "Category deactivated.";
        return RedirectToPage();
    }
}
