using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.DeleteRequests
{
    [Authorize(Policy = "DocRepo.DeleteApprove")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db)
        {
            _db = db;
        }

        // list shown on the page
        public List<DocumentDeleteRequest> DeleteRequests { get; private set; } = new();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            DeleteRequests = await _db.DocumentDeleteRequests
                .AsNoTracking()
                .Include(r => r.Document)
                .Where(r => r.ApprovedAtUtc == null)   // only pending
                .OrderBy(r => r.RequestedAtUtc)
                .ToListAsync(cancellationToken);
        }

        // approve = mark request approved (and optionally act on the document)
        public async Task<IActionResult> OnPostApproveAsync(long id, CancellationToken cancellationToken)
        {
            var request = await _db.DocumentDeleteRequests
                .Include(r => r.Document)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

            if (request == null)
            {
                return NotFound();
            }

            request.ApprovedAtUtc = DateTimeOffset.UtcNow;
            request.ApprovedByUserId = User?.Identity?.Name ?? "system";

            // If you want to deactivate the document when a delete is approved, uncomment:
            // if (request.Document != null)
            // {
            //     request.Document.IsActive = false;
            //     request.Document.UpdatedAtUtc = DateTime.UtcNow;
            //     request.Document.UpdatedByUserId = User?.Identity?.Name ?? "system";
            // }

            await _db.SaveChangesAsync(cancellationToken);

            TempData["ToastMessage"] = "Delete request approved.";
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
    }
}
