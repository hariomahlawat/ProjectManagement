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

    /// <summary>Display names for the user ids shown on the page (requesters, the deleter).</summary>
    public IReadOnlyDictionary<string, string> UserNames { get; private set; } = new Dictionary<string, string>();

    public string NameOf(string? userId)
        => string.IsNullOrWhiteSpace(userId) ? "Unknown" : UserNames.TryGetValue(userId, out var name) ? name : userId;

    [BindProperty]
    [StringLength(512, ErrorMessage = "Reason must be 512 characters or fewer.")]
    public string? Reason { get; set; }

    /// <summary>The list (with its filters) to return to after an action; only local URLs are used.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

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
            return BackToList();
        }

        var userId = GetUserId();
        document.IsActive = false;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = userId;

        _audit.Stage(document.Id, userId, "Deactivated", new { document.Id });
        await _db.SaveChangesAsync(cancellationToken);
        TempData["ToastMessage"] = "Document deactivated.";
        return BackToList();
    }

   
    public async Task<IActionResult> OnPostActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null) return NotFound();

        if (document.IsDeleted)
        {
            TempData["ToastMessage"] = "Deleted documents cannot be activated. Restore from trash first.";
            return BackToList();
        }

        var userId = GetUserId();
        document.IsActive = true;
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = userId;

        _audit.Stage(document.Id, userId, "Activated", new { document.Id });
        await _db.SaveChangesAsync(cancellationToken);
        TempData["ToastMessage"] = "Document reactivated.";
        return BackToList();
    }

   
    public async Task<IActionResult> OnPostRequestDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null) return NotFound();

        if (document.IsDeleted)
        {
            TempData["ToastMessage"] = "Document is already in the trash.";
            return BackToList();
        }

        if (!ModelState.IsValid)
        {
            if (!await LoadAsync(id, cancellationToken)) return NotFound();
            return Page();
        }

        var trimmedReason = string.IsNullOrWhiteSpace(Reason) ? string.Empty : Reason.Trim();
        Reason = trimmedReason;
        if (trimmedReason.Length == 0)
        {
            ModelState.AddModelError(nameof(Reason), "Give a reason so the approver knows why the document should go.");
            if (!await LoadAsync(id, cancellationToken)) return NotFound();
            return Page();
        }

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
            return BackToList();
        }

        var userId = GetUserId();

        var request = new DocumentDeleteRequest
        {
            DocumentId = id,
            RequestedByUserId = userId,
            RequestedAtUtc = DateTimeOffset.UtcNow,
            Reason = trimmedReason
        };

        // The request's id is part of its audit row, so both are written in one transaction.
        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            await _db.DocumentDeleteRequests.AddAsync(request, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.WriteAsync(document.Id, userId, "DeleteRequested", new { request.Id, request.Reason }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        TempData["ToastMessage"] = "Delete request submitted.";
        return BackToList();
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

        _audit.Stage(document.Id, userId, DocRepoAuditEventTypes.OcrRequeued, new { document.Id });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["ToastMessage"] = "OCR re-queued.";
        return string.IsNullOrWhiteSpace(ReturnUrl) ? RedirectToPage(new { id }) : BackToList();
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

        var userIds = PendingRequests.Select(r => r.RequestedByUserId)
            .Append(Document.DeletedByUserId ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();
        UserNames = await _db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, Name = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName })
            .ToDictionaryAsync(user => user.Id, user => user.Name ?? user.Id, cancellationToken);

        return true;
    }

    // SECTION: Helpers
    private IActionResult BackToList()
        => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : RedirectToPage("./Index");

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
}
