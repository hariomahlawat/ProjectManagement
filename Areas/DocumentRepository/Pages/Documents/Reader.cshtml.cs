using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data.DocRepo;
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

        public string ReturnUrl { get; private set; } = string.Empty;

        public string OfficeName { get; private set; } = string.Empty;

        public string DocumentCategoryName { get; private set; } = string.Empty;

        public string? ReceivedFrom { get; private set; }

        public DateOnly? DocumentDate { get; private set; }

        public bool IsActive { get; private set; }

        public string[] Tags { get; private set; } = Array.Empty<string>();

        public DocOcrStatus OcrStatus { get; private set; }

        public string? OcrFailureReason { get; private set; }

        public DateTime? CreatedAtUtc { get; private set; }

        // SECTION: Query
        [FromQuery(Name = "returnUrl")]
        public string? ReturnUrlQuery { get; set; }

        // SECTION: Handlers
        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            var doc = await _db.Documents
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new
                {
                    d.Subject,
                    d.ReceivedFrom,
                    d.DocumentDate,
                    OfficeName = d.OfficeCategory.Name,
                    DocumentCategoryName = d.DocumentCategory.Name,
                    d.IsActive,
                    d.OcrStatus,
                    d.OcrFailureReason,
                    d.CreatedAtUtc,
                    Tags = d.DocumentTags
                        .Select(tag => tag.Tag.Name)
                        .OrderBy(tag => tag)
                        .ToArray(),
                    d.StoragePath,
                    d.IsDeleted
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (doc == null || doc.IsDeleted)
            {
                return NotFound();
            }

            DocumentTitle = string.IsNullOrWhiteSpace(doc.Subject)
                ? "Document"
                : doc.Subject;

            // SECTION: Return URL
            var fallbackUrl = Url.Page("/Documents/Index", new { area = "DocumentRepository" }) ?? "/DocumentRepository/Documents";
            ReturnUrl = Url.IsLocalUrl(ReturnUrlQuery) ? ReturnUrlQuery! : fallbackUrl;

            // these two pages already exist in your module
            ViewUrl = Url.Page("./View", new { id }) ?? string.Empty;
            DownloadUrl = Url.Page("./Download", new { id }) ?? string.Empty;

            OfficeName = doc.OfficeName;
            DocumentCategoryName = doc.DocumentCategoryName;
            ReceivedFrom = doc.ReceivedFrom;
            DocumentDate = doc.DocumentDate;
            IsActive = doc.IsActive;
            Tags = doc.Tags;
            OcrStatus = doc.OcrStatus;
            OcrFailureReason = doc.OcrFailureReason;
            CreatedAtUtc = doc.CreatedAtUtc;

            // SECTION: Storage existence
            IsFileMissing = !await _storage.ExistsAsync(doc.StoragePath, HttpContext.RequestAborted);
            if (IsFileMissing)
            {
                _logger.LogWarning(
                    "DocRepo file missing for Reader. DocumentId={DocumentId} StoragePath={StoragePath}",
                    id,
                    doc.StoragePath);
            }

            return Page();
        }
    }
}
