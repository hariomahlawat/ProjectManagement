using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public class ReaderModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocStorage _storage;
        private readonly ILogger<ReaderModel> _logger;

        // SECTION: Constructor
        public ReaderModel(ApplicationDbContext db, IDocStorage storage, ILogger<ReaderModel> logger)
        {
            _db = db;
            _storage = storage;
            _logger = logger;
        }

        // SECTION: View data
        public string DocumentTitle { get; private set; } = "Document";

        public string ViewUrl { get; private set; } = string.Empty;

        public string DownloadUrl { get; private set; } = string.Empty;

        public bool IsFileMissing { get; private set; }

        // SECTION: Handlers
        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            var doc = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

            if (doc == null || doc.IsDeleted)
            {
                return NotFound();
            }

            DocumentTitle = string.IsNullOrWhiteSpace(doc.Subject)
                ? "Document"
                : doc.Subject;

            // these two pages already exist in your module
            ViewUrl = Url.Page("./View", new { id }) ?? string.Empty;
            DownloadUrl = Url.Page("./Download", new { id }) ?? string.Empty;

            // SECTION: Storage existence
            IsFileMissing = !await _storage.ExistsAsync(doc.StoragePath, HttpContext.RequestAborted);
            if (IsFileMissing)
            {
                _logger.LogWarning(
                    "DocRepo file missing for Reader. DocumentId={DocumentId} StoragePath={StoragePath}",
                    doc.Id,
                    doc.StoragePath);
            }

            return Page();
        }
    }
}
