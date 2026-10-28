using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ViewModel> _logger;
        private readonly IDocRepoAccess _access;
        private readonly IDocRepoAuditService _audit;

        public ViewModel(
            ApplicationDbContext db,
            IDocStorage storage,
            IWebHostEnvironment environment,
            ILogger<ViewModel> logger,
            IDocRepoAccess access,
            IDocRepoAuditService audit)
        {
            _db = db;
            _storage = storage;
            _environment = environment;
            _logger = logger;
            _access = access;
            _audit = audit;
        }

        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            // SECTION: Document lookup
            var canViewInactive = await _access.CanViewInactiveAsync(User);
            var doc = await _access.Readable(_db.Documents.AsNoTracking(), canViewInactive)
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (doc is null) return NotFound();

            // SECTION: Storage existence check
            var exists = await _storage.ExistsAsync(doc.StoragePath, HttpContext.RequestAborted);
            if (!exists)
            {
                LogMissingFile(doc.Id, doc.StoragePath);
                return NotFound("Document file is missing from storage.");
            }

            // SECTION: Stream resolution
            Stream stream;
            try
            {
                stream = await _storage.OpenReadAsync(doc.StoragePath, HttpContext.RequestAborted);
            }
            catch (DirectoryNotFoundException)
            {
                LogMissingFile(doc.Id, doc.StoragePath);
                return NotFound("Document file is missing from storage.");
            }
            catch (FileNotFoundException)
            {
                LogMissingFile(doc.Id, doc.StoragePath);
                return NotFound("Document file is missing from storage.");
            }

            // SECTION: Audit
            // The reader page records the same "Viewed" event, so opening a document there and
            // its embedded file counts once; "Open in new tab" is recorded here.
            // The PDF viewer then fetches byte ranges; only the opening request is checked.
            var range = Request.Headers[HeaderNames.Range].ToString();
            if (string.IsNullOrEmpty(range) || range.StartsWith("bytes=0-", StringComparison.OrdinalIgnoreCase))
            {
                await RecordViewAsync(doc.Id, cancellationToken);
            }

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
            // Never keep a copy in the browser cache: these are office documents opened on shared
            // LAN machines, and a deactivated or deleted document must not stay readable there.
            Response.Headers[HeaderNames.CacheControl] = "private, no-store";

            return result;
        }

        private async Task RecordViewAsync(Guid documentId, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            if (!await _audit.WriteAccessOnceAsync(documentId, userId, DocRepoAuditEventTypes.Viewed, DocRepoAuditEventTypes.AccessWindow, cancellationToken))
            {
                _logger.LogWarning("Unable to record document view for DocumentId={DocumentId}", documentId);
            }
        }

        private void LogMissingFile(Guid documentId, string storagePath)
        {
            var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            _logger.LogWarning(
                "DocRepo file missing for View. DocumentId={DocumentId} StoragePath={StoragePath} Environment={Environment} UserId={UserId}",
                documentId,
                storagePath,
                _environment.EnvironmentName,
                userId);
        }
    }
}
