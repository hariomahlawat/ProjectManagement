using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public class ReaderModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocStorage _storage;
        private readonly ILogger<ReaderModel> _logger;
        private readonly IAuthorizationService _authorizationService;
        private readonly IAotsUnreadService _aotsUnreadService;
        private readonly IDocRepoAuditService _audit;
        private readonly IDocRepoAccess _access;

        // SECTION: Constructor
        public ReaderModel(
            ApplicationDbContext db,
            IDocStorage storage,
            ILogger<ReaderModel> logger,
            IAuthorizationService authorizationService,
            IAotsUnreadService aotsUnreadService,
            IDocRepoAuditService audit,
            IDocRepoAccess access)
        {
            _access = access;
            _db = db;
            _storage = storage;
            _logger = logger;
            _authorizationService = authorizationService;
            _aotsUnreadService = aotsUnreadService;
            _audit = audit;
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

        public bool IsActive { get; private set; }

        public string[] Tags { get; private set; } = Array.Empty<string>();

        public DocOcrStatus OcrStatus { get; private set; }

        public string? OcrFailureReason { get; private set; }

        public DateTime? CreatedAtUtc { get; private set; }

        public string? CreatedByDisplay { get; private set; }

        public DateTime? UpdatedAtUtc { get; private set; }

        public bool IsFavourite { get; private set; }

        public string ToggleFavouriteUrl { get; private set; } = string.Empty;

        public bool IsAots { get; private set; }

        public bool IsAotsSeen { get; private set; }

        public bool CanViewAotsViews { get; private set; }

        public int AotsViewsCount { get; private set; }

        public string AotsViewsUrl { get; private set; } = string.Empty;

        public Guid DocumentId { get; private set; }

        public DateOnly? DocumentDate { get; private set; }

        public string OriginalFileName { get; private set; } = string.Empty;

        public long FileSizeBytes { get; private set; }

        public int DocumentCategoryId { get; private set; }

        public bool CanEdit { get; private set; }

        public bool CanManage { get; private set; }

        public string ManageUrl { get; private set; } = string.Empty;

        // SECTION: Query
        [FromQuery(Name = "returnUrl")]
        public string? ReturnUrlQuery { get; set; }

        // SECTION: Handlers
        public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
        {
            var canViewInactive = await _access.CanViewInactiveAsync(User);
            var doc = await _access.Readable(_db.Documents.AsNoTracking(), canViewInactive)
                .Where(d => d.Id == id)
                .Select(d => new
                {
                    d.Subject,
                    d.ReceivedFrom,
                    OfficeName = d.OfficeCategory.Name,
                    DocumentCategoryName = d.DocumentCategory.Name,
                    d.IsActive,
                    d.OcrStatus,
                    d.OcrFailureReason,
                    d.CreatedAtUtc,
                    d.CreatedByUserId,
                    d.UpdatedAtUtc,
                    d.IsAots,
                    d.DocumentDate,
                    d.OriginalFileName,
                    d.FileSizeBytes,
                    d.DocumentCategoryId,
                    Tags = d.DocumentTags
                        .Select(tag => tag.Tag.Name)
                        .OrderBy(tag => tag)
                        .ToArray(),
                    d.StoragePath
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (doc == null)
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
            ToggleFavouriteUrl = Url.Page("./Index", "ToggleFavourite", new { area = "DocumentRepository" }) ?? string.Empty;

            DocumentId = id;
            DocumentDate = doc.DocumentDate;
            OriginalFileName = doc.OriginalFileName;
            FileSizeBytes = doc.FileSizeBytes;
            DocumentCategoryId = doc.DocumentCategoryId;
            CanEdit = (await _authorizationService.AuthorizeAsync(User, Policies.Documents.EditMetadata)).Succeeded;
            CanManage = (await _authorizationService.AuthorizeAsync(User, Policies.Documents.SoftDelete)).Succeeded;
            ManageUrl = Url.Page("./Manage", new { id }) ?? string.Empty;
            OfficeName = doc.OfficeName;
            DocumentCategoryName = doc.DocumentCategoryName;
            ReceivedFrom = doc.ReceivedFrom;
            IsActive = doc.IsActive;
            Tags = doc.Tags;
            OcrStatus = doc.OcrStatus;
            OcrFailureReason = doc.OcrFailureReason;
            CreatedAtUtc = doc.CreatedAtUtc;
            UpdatedAtUtc = doc.UpdatedAtUtc;
            IsAots = doc.IsAots;

            // SECTION: Audit users
            var auditUsers = await GetAuditUserLookupAsync(doc.CreatedByUserId, cancellationToken);
            CreatedByDisplay = ResolveUserDisplay(auditUsers, doc.CreatedByUserId);

            // SECTION: Favourite state
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                IsFavourite = await _db.DocRepoFavourites
                    .AsNoTracking()
                    .AnyAsync(favourite => favourite.UserId == userId && favourite.DocumentId == id, cancellationToken);
            }

            // SECTION: Storage existence
            IsFileMissing = !await _storage.ExistsAsync(doc.StoragePath, HttpContext.RequestAborted);
            if (IsFileMissing)
            {
                _logger.LogWarning(
                    "DocRepo file missing for Reader. DocumentId={DocumentId} StoragePath={StoragePath}",
                    id,
                    doc.StoragePath);
            }

            // SECTION: AOTS viewer access
            if (doc.IsAots)
            {
                var authorizationResult = await _authorizationService.AuthorizeAsync(User, "DocRepo.EditMetadata");
                CanViewAotsViews = authorizationResult.Succeeded;

                if (CanViewAotsViews)
                {
                    AotsViewsCount = await _db.DocRepoAotsViews
                        .AsNoTracking()
                        .CountAsync(view => view.DocumentId == id, cancellationToken);

                    var readerUrl = Url.Page("./Reader", new { id, returnUrl = ReturnUrl }) ?? string.Empty;
                    AotsViewsUrl = Url.Page("./AotsViews", new { id, returnUrl = readerUrl }) ?? string.Empty;
                }
            }

            // SECTION: AOTS view tracking
            if (doc.IsAots && !IsFileMissing && !string.IsNullOrWhiteSpace(userId))
            {
                IsAotsSeen = await _aotsUnreadService.MarkAsReadAsync(id, userId, cancellationToken);
            }

            if (!IsFileMissing && !string.IsNullOrWhiteSpace(userId))
            {
                await RecordDocumentViewAsync(id, userId, cancellationToken);
            }

            return Page();
        }

        private async Task RecordDocumentViewAsync(
            Guid documentId,
            string userId,
            CancellationToken cancellationToken)
        {
            if (!await _audit.WriteAccessOnceAsync(documentId, userId, DocRepoAuditEventTypes.Viewed, DocRepoAuditEventTypes.AccessWindow, cancellationToken))
            {
                _logger.LogWarning("Unable to record recent document view for DocumentId={DocumentId}", documentId);
            }
        }

        // SECTION: Audit helpers
        private async Task<Dictionary<string, ApplicationUser>> GetAuditUserLookupAsync(
            string createdByUserId,
            CancellationToken cancellationToken)
        {
            var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                createdByUserId
            };

            return await _db.Users
                .AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, cancellationToken);
        }

        private static string? ResolveUserDisplay(
            IReadOnlyDictionary<string, ApplicationUser> lookup,
            string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            if (lookup.TryGetValue(userId, out var user))
            {
                if (!string.IsNullOrWhiteSpace(user.FullName))
                {
                    return user.FullName;
                }

                if (!string.IsNullOrWhiteSpace(user.UserName))
                {
                    return user.UserName;
                }
            }

            return userId;
        }
    }
}
