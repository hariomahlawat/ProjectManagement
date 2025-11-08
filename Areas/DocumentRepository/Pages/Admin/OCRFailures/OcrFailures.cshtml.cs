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
        private readonly DocumentOcrService _ocrService;

        public OcrFailuresModel(ApplicationDbContext db, DocumentOcrService ocrService)
        {
            _db = db;
            _ocrService = ocrService;
        }

        public IList<Document> FailedDocuments { get; private set; } = new List<Document>();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync(CancellationToken ct)
        {
            FailedDocuments = await _db.Documents
                .Where(d => d.OcrStatus == DocOcrStatus.Failed && !d.IsDeleted)
                .OrderByDescending(d => d.CreatedAtUtc)
                .ToListAsync(ct);
        }

        public async Task<IActionResult> OnPostRequeueAsync(Guid id, CancellationToken ct)
        {
            await _ocrService.ReprocessAsync(id, ct);
            StatusMessage = "OCR re-queued for selected document.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRequeueAllAsync(CancellationToken ct)
        {
            var count = await _ocrService.ReprocessAllFailedAsync(ct);
            StatusMessage = $"OCR re-queued for {count} documents.";
            return RedirectToPage();
        }
    }
}
