using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.DeleteRequests
{
    [Authorize(Policy = "DocRepo.DeleteApprove")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocRepoAuditService _audit;

        // SECTION: Constructor
        public IndexModel(ApplicationDbContext db, IDocRepoAuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        // list shown on the page
        public List<DocumentDeleteRequest> DeleteRequests { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            DeleteRequests = await _db.DocumentDeleteRequests
                .AsNoTracking()
                .Include(r => r.Document)
                .Where(r => r.ApprovedAtUtc == null)
                .Where(r => r.Document != null && !r.Document.IsDeleted)
                .OrderBy(r => r.RequestedAtUtc)
                .ToListAsync(cancellationToken);
        }

        // approve = mark request approved AND deactivate the document
        public async Task<IActionResult> OnPostApproveAsync(long id, CancellationToken cancellationToken)
        {
            var request = await _db.DocumentDeleteRequests
                .Include(r => r.Document)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            if (request == null)
            {
                return NotFound();
            }

            // in case it's already processed
            if (request.ApprovedAtUtc != null)
            {
                return RedirectToPage();
            }

            var actorId = GetActorUserId();
            request.ApprovedAtUtc = DateTimeOffset.UtcNow;
            request.ApprovedByUserId = actorId;

            if (request.Document != null)
            {
                // SECTION: Soft delete update
                request.Document.IsDeleted = true;
                request.Document.IsActive = false;
                request.Document.DeletedAtUtc = DateTime.UtcNow;
                request.Document.DeletedByUserId = actorId;
                request.Document.DeleteReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
                request.Document.UpdatedAtUtc = DateTime.UtcNow;
                request.Document.UpdatedByUserId = actorId;
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (request.Document != null)
            {
                await _audit.WriteAsync(
                    request.Document.Id,
                    actorId,
                    "SoftDeleted",
                    new { request.Id, request.Reason },
                    cancellationToken);
            }

            TempData["ToastMessage"] = "Document moved to trash.";
            return RedirectToPage();
        }

        // reject = just remove the pending request
        public async Task<IActionResult> OnPostRejectAsync(long id, CancellationToken cancellationToken)
        {
            var request = await _db.DocumentDeleteRequests
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            if (request == null)
            {
                return NotFound();
            }

            _db.DocumentDeleteRequests.Remove(request);
            await _db.SaveChangesAsync(cancellationToken);

            TempData["ToastMessage"] = "Delete request rejected.";
            return RedirectToPage();
        }

        private string GetActorUserId() =>
            User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.Identity?.Name ?? "system";
    }
}
