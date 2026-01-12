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
using Npgsql;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Models;

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
                    OfficeName = d.OfficeCategory.Name,
                    DocumentCategoryName = d.DocumentCategory.Name,
                    d.IsActive,
                    d.OcrStatus,
                    d.OcrFailureReason,
                    d.CreatedAtUtc,
                    d.CreatedByUserId,
                    d.UpdatedAtUtc,
                    d.IsAots,
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
            ToggleFavouriteUrl = Url.Page("./Index", "ToggleFavourite", new { area = "DocumentRepository" }) ?? string.Empty;

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

            // SECTION: AOTS view tracking
            if (doc.IsAots && !IsFileMissing && !string.IsNullOrWhiteSpace(userId))
            {
                IsAotsSeen = await EnsureAotsViewLoggedAsync(id, userId, cancellationToken);
            }

            return Page();
        }

        // SECTION: AOTS logging helpers
        private async Task<bool> EnsureAotsViewLoggedAsync(Guid documentId, string userId, CancellationToken cancellationToken)
        {
            var hasExisting = await _db.DocRepoAotsViews
                .AsNoTracking()
                .AnyAsync(view => view.DocumentId == documentId && view.UserId == userId, cancellationToken);

            if (hasExisting)
            {
                return true;
            }

            _db.DocRepoAotsViews.Add(new DocRepoAotsView
            {
                DocumentId = documentId,
                UserId = userId,
                FirstViewedAtUtc = DateTime.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to record AOTS view for DocumentId={DocumentId}", documentId);
                return false;
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        {
            return exception.InnerException is PostgresException postgres &&
                   postgres.SqlState == PostgresErrorCodes.UniqueViolation;
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
