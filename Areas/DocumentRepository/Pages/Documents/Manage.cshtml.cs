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

[Authorize(Policy = "DocRepo.Upload")]
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
        {
            return NotFound();
        }

        return Page();
    }

    [Authorize(Policy = "DocRepo.SoftDelete")]
    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
        document.IsActive = false;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(document.Id, userId, "Deactivated", new { document.Id }, cancellationToken);
        TempData["ToastMessage"] = "Document deactivated.";
        return RedirectToPage("./Index");
    }

    [Authorize(Policy = "DocRepo.SoftDelete")]
    public async Task<IActionResult> OnPostActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
        document.IsActive = true;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(document.Id, userId, "Activated", new { document.Id }, cancellationToken);
        TempData["ToastMessage"] = "Document reactivated.";
        return RedirectToPage("./Index");
    }

    [Authorize(Policy = "DocRepo.SoftDelete")]
    public async Task<IActionResult> OnPostRequestDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            if (!await LoadAsync(id, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }

        var trimmedReason = string.IsNullOrWhiteSpace(Reason) ? string.Empty : Reason.Trim();
        Reason = trimmedReason;
        if (trimmedReason.Length > 512)
        {
            ModelState.AddModelError(nameof(Reason), "Reason must be 512 characters or fewer.");
            if (!await LoadAsync(id, cancellationToken))
            {
                return NotFound();
            }

            return Page();
        }

        var hasPending = await _db.DocumentDeleteRequests
            .AnyAsync(r => r.DocumentId == id && r.ApprovedAtUtc == null, cancellationToken);

        if (hasPending)
        {
            TempData["ToastMessage"] = "A delete request is already pending for this document.";
            return RedirectToPage("./Index");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
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

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        Document = await _db.Documents
            .Include(d => d.OfficeCategory)
            .Include(d => d.DocumentCategory)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (Document is null)
        {
            return false;
        }

        PendingRequests = await _db.DocumentDeleteRequests
            .Where(r => r.DocumentId == id && r.ApprovedAtUtc == null)
            .OrderBy(r => r.RequestedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return true;
    }
}
