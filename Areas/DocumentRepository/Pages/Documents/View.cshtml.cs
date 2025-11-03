using System.Net;
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
            var doc = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.IsActive, cancellationToken);

            if (doc is null) return NotFound();

            var stream = await _storage.OpenReadAsync(doc.StoragePath, HttpContext.RequestAborted);

            // IMPORTANT: enable range processing so embedded PDF viewers can request byte ranges (206).
            var result = new FileStreamResult(stream, "application/pdf")
            {
                EnableRangeProcessing = true
            };

            // Force inline display (not attachment) and set a sane cache.
            var fileName = string.IsNullOrWhiteSpace(doc.OriginalFileName)
                ? $"Document-{doc.Id}.pdf"
                : doc.OriginalFileName;

            Response.Headers[HeaderNames.ContentDisposition] =
                $"inline; filename=\"{WebUtility.UrlEncode(fileName)}\"";
            Response.Headers[HeaderNames.CacheControl] = "private, max-age=3600";

            return result;
        }
    }
}
