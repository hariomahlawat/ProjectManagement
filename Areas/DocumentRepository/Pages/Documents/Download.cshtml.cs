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

        public DownloadModel(ApplicationDbContext db, IDocStorage storage, IWebHostEnvironment environment, ILogger<DownloadModel> logger)
        {
            _db = db;
            _storage = storage;
            _environment = environment;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            // SECTION: Document lookup
            var doc = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (doc is null || doc.IsDeleted)
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
