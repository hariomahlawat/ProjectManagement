using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[Authorize(Policy = "DocRepo.EditMetadata")]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public EditModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = null!;

    public List<OfficeCategory> OfficeCategories { get; private set; } = new();
    public List<DocumentCategory> DocumentCategories { get; private set; } = new();

    public class InputModel
    {
        [Required]
        public Guid Id { get; set; }

        [Required, StringLength(256)]
        public string Subject { get; set; } = string.Empty;

        [StringLength(256)]
        public string? ReceivedFrom { get; set; }

        public DateOnly? DocumentDate { get; set; }

        // nullable because UI has an empty option
        public int? OfficeCategoryId { get; set; }

        public int? DocumentCategoryId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var doc = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (doc is null)
            return NotFound();

        Input = new InputModel
        {
            Id = doc.Id,
            Subject = doc.Subject,
            ReceivedFrom = doc.ReceivedFrom,
            DocumentDate = doc.DocumentDate,
            OfficeCategoryId = doc.OfficeCategoryId,
            DocumentCategoryId = doc.DocumentCategoryId
        };

        await LoadLookupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == Input.Id, cancellationToken);

        if (doc is null)
            return NotFound();

        doc.Subject = Input.Subject.Trim();
        doc.ReceivedFrom = string.IsNullOrWhiteSpace(Input.ReceivedFrom)
            ? null
            : Input.ReceivedFrom.Trim();
        doc.DocumentDate = Input.DocumentDate;

        // only overwrite if user actually selected a value
        if (Input.OfficeCategoryId.HasValue)
            doc.OfficeCategoryId = Input.OfficeCategoryId.Value;

        if (Input.DocumentCategoryId.HasValue)
            doc.DocumentCategoryId = Input.DocumentCategoryId.Value;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
        doc.UpdatedAtUtc = DateTime.UtcNow;
        doc.UpdatedByUserId = userId;

        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "Document metadata updated.";
        return RedirectToPage("./Index");
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        OfficeCategories = await _db.OfficeCategories
            .AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.SortOrder)
            .ToListAsync(cancellationToken);

        DocumentCategories = await _db.DocumentCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
    }
}
