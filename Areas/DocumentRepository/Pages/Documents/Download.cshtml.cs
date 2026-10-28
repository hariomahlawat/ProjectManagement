using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public sealed class DownloadModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocStorage _storage;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DownloadModel> _logger;
        private readonly IDocRepoAccess _access;
        private readonly IDocRepoAuditService _audit;

        public DownloadModel(
            ApplicationDbContext db,
            IDocStorage storage,
            IWebHostEnvironment environment,
            ILogger<DownloadModel> logger,
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

            if (doc is null)
            {
                return NotFound();
            }

            // SECTION: Stream resolution
            var exists = await _storage.ExistsAsync(doc.StoragePath, HttpContext.RequestAborted);
            if (!exists)
            {
                LogMissingFile(doc.Id, doc.StoragePath);
                return NotFound("Document file is missing from storage.");
            }

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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId) &&
                !await _audit.WriteAccessOnceAsync(doc.Id, userId, DocRepoAuditEventTypes.Downloaded, DocRepoAuditEventTypes.AccessWindow, cancellationToken))
            {
                _logger.LogWarning("Unable to record document download for DocumentId={DocumentId}", doc.Id);
            }

            Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] = "private, no-store";
            var downloadName = string.IsNullOrWhiteSpace(doc.OriginalFileName)
                ? $"Document-{doc.Id}"
                : doc.OriginalFileName;

            return File(stream, doc.MimeType, downloadName);
        }

        private void LogMissingFile(Guid documentId, string storagePath)
        {
            var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            _logger.LogWarning(
                "DocRepo file missing for Download. DocumentId={DocumentId} StoragePath={StoragePath} Environment={Environment} UserId={UserId}",
                documentId,
                storagePath,
                _environment.EnvironmentName,
                userId);
        }
    }
}
