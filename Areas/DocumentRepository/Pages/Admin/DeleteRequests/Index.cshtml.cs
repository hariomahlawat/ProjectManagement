using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.DeleteRequests;

[Authorize(Policy = "DocRepo.ManageCategories")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;
    private readonly IDocRepoAuditService _audit;

    public IndexModel(ApplicationDbContext db, IDocStorage storage, IDocRepoAuditService audit)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
    }

    public IList<DocumentDeleteRequest> Pending { get; private set; } = new List<DocumentDeleteRequest>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Pending = await _db.DocumentDeleteRequests
            .Include(r => r.Document)
            .Where(r => r.ApprovedAtUtc == null)
            .OrderBy(r => r.RequestedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostApproveAsync(long id, CancellationToken cancellationToken)
    {
        var request = await _db.DocumentDeleteRequests
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null || request.ApprovedAtUtc.HasValue)
        {
            return RedirectToPage();
        }

        var document = request.Document;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

        if (!string.IsNullOrWhiteSpace(document.StoragePath))
        {
            await _storage.DeleteAsync(document.StoragePath, cancellationToken);
        }

        _db.Documents.Remove(document);
        request.ApprovedAtUtc = DateTimeOffset.UtcNow;
        request.ApprovedByUserId = userId;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(request.DocumentId, userId, "DeleteApproved", new { request.Id }, cancellationToken);

        TempData["ToastMessage"] = "Delete request approved and document removed.";
        return RedirectToPage();
    }
}
