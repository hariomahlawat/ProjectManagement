using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public sealed class ViewModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocStorage _storage;

        public ViewModel(ApplicationDbContext db, IDocStorage storage)
        {
            _db = db;
            _storage = storage;
        }

        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            // SECTION: Document lookup
            var doc = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.IsActive && !d.IsDeleted, cancellationToken);

            if (doc is null) return NotFound();

            // SECTION: Stream resolution
            var stream = await _storage.OpenReadAsync(doc.StoragePath, HttpContext.RequestAborted);

            // IMPORTANT: enable range processing so embedded PDF viewers can request byte ranges (206).
            var result = new FileStreamResult(stream, doc.MimeType)
            {
                EnableRangeProcessing = true
            };

            // SECTION: Content headers
            // Force inline display (not attachment) and set a sane cache.
            var fileName = string.IsNullOrWhiteSpace(doc.OriginalFileName)
                ? $"Document-{doc.Id}"
                : doc.OriginalFileName;

            var safeFileName = Path.GetFileName(fileName).Replace("\"", string.Empty);
            var disposition = new ContentDispositionHeaderValue("inline")
            {
                FileNameStar = safeFileName,
                FileName = safeFileName
            };

            Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
            Response.Headers[HeaderNames.CacheControl] = "private, max-age=3600";

            return result;
        }
    }
}
