using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents;

[ValidateAntiForgeryToken]
[Authorize(Policy = "DocRepo.SoftDelete")] // <-- single policy; this page is for deactivate/activate/request-delete
public class ManageModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocRepoAuditService _audit;

    public ManageModel(ApplicationDbContext db, IDocRepoAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Document? Document { get; private set; }

    public IReadOnlyList<DocumentDeleteRequest> PendingRequests { get; private set; } = Array.Empty<DocumentDeleteRequest>();

    [BindProperty]
    [StringLength(512, ErrorMessage = "Reason must be 512 characters or fewer.")]
    public string? Reason { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
            return NotFound();

        return Page();
    }

    
    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null) return NotFound();

        if (document.IsDeleted)
        {
            TempData["ToastMessage"] = "Document is already deleted and cannot be deactivated.";
            return RedirectToPage("./Index");
        }

        var userId = GetUserId();
        document.IsActive = false;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = userId;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(document.Id, userId, "Deactivated", new { document.Id }, cancellationToken);
        TempData["ToastMessage"] = "Document deactivated.";
        return RedirectToPage("./Index");
    }

   
    public async Task<IActionResult> OnPostActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null) return NotFound();

        if (document.IsDeleted)
        {
            TempData["ToastMessage"] = "Deleted documents cannot be activated. Restore from trash first.";
            return RedirectToPage("./Index");
        }

        var userId = GetUserId();
        document.IsActive = true;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = userId;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(document.Id, userId, "Activated", new { document.Id }, cancellationToken);
        TempData["ToastMessage"] = "Document reactivated.";
        return RedirectToPage("./Index");
    }

   
    public async Task<IActionResult> OnPostRequestDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null) return NotFound();

        if (document.IsDeleted)
        {
            TempData["ToastMessage"] = "Document is already in the trash.";
            return RedirectToPage("./Index");
        }

        if (!ModelState.IsValid)
        {
            if (!await LoadAsync(id, cancellationToken)) return NotFound();
            return Page();
        }

        var trimmedReason = string.IsNullOrWhiteSpace(Reason) ? string.Empty : Reason.Trim();
        Reason = trimmedReason;

        if (trimmedReason.Length > 512)
        {
            ModelState.AddModelError(nameof(Reason), "Reason must be 512 characters or fewer.");
            if (!await LoadAsync(id, cancellationToken)) return NotFound();
            return Page();
        }

        var hasPending = await _db.DocumentDeleteRequests
            .AnyAsync(r => r.DocumentId == id && r.ApprovedAtUtc == null, cancellationToken);

        if (hasPending)
        {
            TempData["ToastMessage"] = "A delete request is already pending for this document.";
            return RedirectToPage("./Index");
        }

        var userId = GetUserId();

        var request = new DocumentDeleteRequest
        {
            DocumentId = id,
            RequestedByUserId = userId,
            RequestedAtUtc = DateTimeOffset.UtcNow,
            Reason = trimmedReason
        };

        await _db.DocumentDeleteRequests.AddAsync(request, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(document.Id, userId, "DeleteRequested", new { request.Id, request.Reason }, cancellationToken);
        TempData["ToastMessage"] = "Delete request submitted.";
        return RedirectToPage("./Index");
    }

    // SECTION: OCR management handlers
    public async Task<IActionResult> OnPostRetryOcrAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents
            .Include(d => d.DocumentText)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null || document.IsDeleted)
        {
            return NotFound();
        }

        var userId = GetUserId();
        document.OcrStatus = DocOcrStatus.Pending;
        document.OcrFailureReason = null;
        document.OcrLastTriedUtc = null;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = userId;

        if (document.DocumentText is not null)
        {
            document.DocumentText.OcrText = null;
            document.DocumentText.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "OCR re-queued.";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        Document = await _db.Documents
            .Include(d => d.OfficeCategory)
            .Include(d => d.DocumentCategory)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (Document is null) return false;

        PendingRequests = await _db.DocumentDeleteRequests
            .Where(r => r.DocumentId == id && r.ApprovedAtUtc == null)
            .OrderBy(r => r.RequestedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return true;
    }

    // SECTION: Helpers
    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
}
