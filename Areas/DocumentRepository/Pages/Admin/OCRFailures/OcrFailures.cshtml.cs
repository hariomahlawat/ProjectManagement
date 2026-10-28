using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin
{
    [Authorize(Policy = "DocRepo.DeleteApprove")]   // use your actual admin policy
    public class OcrFailuresModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocumentOcrService _ocrService;

        public OcrFailuresModel(ApplicationDbContext db, IDocumentOcrService ocrService)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        }

        public IList<Document> FailedDocuments { get; private set; } = new List<Document>();

        [TempData]
        public string? StatusMessage { get; set; }

        private string ActorUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

        public async Task OnGetAsync(CancellationToken ct)
        {
            FailedDocuments = await _db.Documents
                .AsNoTracking()
                .Include(d => d.OfficeCategory)
                .Include(d => d.DocumentCategory)
                .Where(d => d.OcrStatus == DocOcrStatus.Failed && !d.IsDeleted)
                .OrderByDescending(d => d.CreatedAtUtc)
                .ToListAsync(ct);
        }

        public async Task<IActionResult> OnPostRequeueAsync(Guid id, CancellationToken ct)
        {
            StatusMessage = await _ocrService.RequeueAsync(id, ActorUserId, ct)
                ? "Queued for OCR. The background worker will process it shortly."
                : "That document no longer exists.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRequeueAllAsync(CancellationToken ct)
        {
            var count = await _ocrService.RequeueAllFailedAsync(ActorUserId, ct);
            StatusMessage = count == 1
                ? "Queued 1 document for OCR. The background worker will process it shortly."
                : $"Queued {count} documents for OCR. The background worker will process them shortly.";
            return RedirectToPage();
        }
    }
}
