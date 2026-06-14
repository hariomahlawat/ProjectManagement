using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Infrastructure.Activities
{
    public class ActivityRepository : IActivityRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ActivityRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<Activity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return _dbContext.Activities
                .Include(x => x.ActivityType)
                .Include(x => x.Attachments)
                .Include(x => x.CreatedByUser)
                .Include(x => x.LastModifiedByUser)
                .Include(x => x.DeletedByUser)
                .Include(x => x.DeleteRequests)
                    .ThenInclude(x => x.RequestedByUser)
                .Include(x => x.DeleteRequests)
                    .ThenInclude(x => x.ApprovedByUser)
                .Include(x => x.DeleteRequests)
                    .ThenInclude(x => x.RejectedByUser)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Activities
                .AsNoTracking()
                .Include(x => x.ActivityType)
                .Include(x => x.Attachments)
                .Include(x => x.CreatedByUser)
                .Include(x => x.LastModifiedByUser)
                .Include(x => x.DeletedByUser)
                .Where(x => x.ActivityTypeId == activityTypeId && !x.IsDeleted)
                .OrderByDescending(x => x.ScheduledStartUtc ?? x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
        }

        public Task<bool> ExistsByTypeAndTitleAsync(int activityTypeId, string title, int? excludingActivityId, CancellationToken cancellationToken = default)
        {
            // SECTION: Provider-aware duplicate activity title lookup
            var normalizedTitle = title.Trim();
            var query = _dbContext.Activities
                .AsNoTracking()
                .Where(x => x.ActivityTypeId == activityTypeId && !x.IsDeleted);

            if (excludingActivityId.HasValue)
            {
                query = query.Where(x => x.Id != excludingActivityId.Value);
            }

            var providerName = _dbContext.Database.ProviderName ?? string.Empty;

            if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return query.AnyAsync(x => EF.Functions.ILike(x.Title, normalizedTitle), cancellationToken);
            }

            var normalizedComparisonTitle = normalizedTitle.ToLower();

            return query.AnyAsync(x => x.Title.ToLower() == normalizedComparisonTitle, cancellationToken);
        }

        public async Task<ActivityListResult> ListAsync(ActivityListRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var page = request.Page <= 0 ? 1 : request.Page;
            var pageSize = request.PageSize < 0 ? 0 : request.PageSize;

            var query = ApplyMediaFilter(ApplyBaseReviewFilters(CreateBaseQuery(), request), request.MediaFilter);

            var total = await query.CountAsync(cancellationToken);

            IOrderedQueryable<Activity> ordered = request.Sort switch
            {
                ActivityListSort.CreatedAt when request.SortDescending => query.OrderByDescending(x => x.CreatedAtUtc),
                ActivityListSort.CreatedAt => query.OrderBy(x => x.CreatedAtUtc),
                ActivityListSort.Title when request.SortDescending => query.OrderByDescending(x => x.Title),
                ActivityListSort.Title => query.OrderBy(x => x.Title),
                ActivityListSort.ActivityType when request.SortDescending => query.OrderByDescending(x => x.ActivityType.Name),
                ActivityListSort.ActivityType => query.OrderBy(x => x.ActivityType.Name),
                ActivityListSort.ScheduledStart when request.SortDescending => query.OrderByDescending(x => x.ScheduledStartUtc ?? x.CreatedAtUtc),
                _ => query.OrderBy(x => x.ScheduledStartUtc ?? x.CreatedAtUtc)
            };

            ordered = request.Sort switch
            {
                ActivityListSort.ActivityType when request.SortDescending => ordered.ThenByDescending(x => x.Title),
                ActivityListSort.ActivityType => ordered.ThenBy(x => x.Title),
                ActivityListSort.Title when request.SortDescending => ordered.ThenByDescending(x => x.Id),
                ActivityListSort.Title => ordered.ThenBy(x => x.Id),
                ActivityListSort.CreatedAt when request.SortDescending => ordered.ThenByDescending(x => x.Id),
                ActivityListSort.CreatedAt => ordered.ThenBy(x => x.Id),
                ActivityListSort.ScheduledStart when request.SortDescending => ordered.ThenByDescending(x => x.Id),
                _ => ordered.ThenBy(x => x.Id)
            };

            IQueryable<Activity> pagedQuery = ordered;

            if (pageSize > 0)
            {
                pagedQuery = ordered.Skip((page - 1) * pageSize).Take(pageSize);
            }

            var items = await pagedQuery
                .Select(x => new ActivityListItem(
                    x.Id,
                    x.Title,
                    x.ActivityType.Name,
                    x.ActivityTypeId,
                    x.Location,
                    x.Description,
                    x.ScheduledStartUtc,
                    x.ScheduledEndUtc,
                    x.CreatedAtUtc,
                    x.CreatedByUserId,
                    x.CreatedByUser != null ? x.CreatedByUser.FullName : null,
                    x.CreatedByUser != null ? x.CreatedByUser.Email : null,
                    x.Attachments.Count,
                    x.Attachments.AsQueryable().Count(ActivityAttachmentClassifier.IsPdfExpression),
                    x.Attachments.AsQueryable().Count(ActivityAttachmentClassifier.IsDocumentExpression),
                    x.Attachments.AsQueryable().Count(ActivityAttachmentClassifier.IsPhotoExpression),
                    x.Attachments.AsQueryable().Count(ActivityAttachmentClassifier.IsVideoExpression),
                    x.Attachments
                        .OrderByDescending(a => a.ContentType != null && a.ContentType.ToLower().StartsWith("image/"))
                        .ThenByDescending(a => a.ContentType != null && a.ContentType.ToLower().StartsWith("video/"))
                        .ThenByDescending(a => (a.ContentType != null && a.ContentType.ToLower() == "application/pdf") || (a.OriginalFileName != null && a.OriginalFileName.ToLower().EndsWith(".pdf")))
                        .ThenByDescending(a => (a.ContentType != null && (a.ContentType.ToLower().Contains("document") || a.ContentType.ToLower().Contains("spreadsheet") || a.ContentType.ToLower().Contains("presentation") || a.ContentType.ToLower().Contains("wordprocessingml") || a.ContentType.ToLower().Contains("spreadsheetml") || a.ContentType.ToLower().Contains("presentationml") || a.ContentType.ToLower().Contains("officedocument"))) || (a.OriginalFileName != null && (a.OriginalFileName.ToLower().EndsWith(".doc") || a.OriginalFileName.ToLower().EndsWith(".docx") || a.OriginalFileName.ToLower().EndsWith(".xls") || a.OriginalFileName.ToLower().EndsWith(".xlsx") || a.OriginalFileName.ToLower().EndsWith(".ppt") || a.OriginalFileName.ToLower().EndsWith(".pptx"))))
                        .ThenByDescending(a => a.UploadedAtUtc)
                        .Take(3)
                        .Select(a => new ActivityMediaPreview(
                            a.Id,
                            a.OriginalFileName,
                            a.ContentType,
                            a.ContentType != null && a.ContentType.ToLower().StartsWith("image/") ? ActivityAttachmentClassifier.PhotoLabel : a.ContentType != null && a.ContentType.ToLower().StartsWith("video/") ? ActivityAttachmentClassifier.VideoLabel : ((a.ContentType != null && a.ContentType.ToLower() == "application/pdf") || (a.OriginalFileName != null && a.OriginalFileName.ToLower().EndsWith(".pdf"))) ? ActivityAttachmentClassifier.PdfLabel : ((a.ContentType != null && (a.ContentType.ToLower().Contains("document") || a.ContentType.ToLower().Contains("spreadsheet") || a.ContentType.ToLower().Contains("presentation") || a.ContentType.ToLower().Contains("wordprocessingml") || a.ContentType.ToLower().Contains("spreadsheetml") || a.ContentType.ToLower().Contains("presentationml") || a.ContentType.ToLower().Contains("officedocument"))) || (a.OriginalFileName != null && (a.OriginalFileName.ToLower().EndsWith(".doc") || a.OriginalFileName.ToLower().EndsWith(".docx") || a.OriginalFileName.ToLower().EndsWith(".xls") || a.OriginalFileName.ToLower().EndsWith(".xlsx") || a.OriginalFileName.ToLower().EndsWith(".ppt") || a.OriginalFileName.ToLower().EndsWith(".pptx")))) ? ActivityAttachmentClassifier.DocumentLabel : ActivityAttachmentClassifier.OtherLabel,
                            a.StorageKey,
                            a.FileSize))
                        .ToList(),
                    x.DeleteRequests.Any(r => r.ApprovedAtUtc == null && r.RejectedAtUtc == null)))
                .ToListAsync(cancellationToken);

            return new ActivityListResult(items, total, page, pageSize > 0 ? pageSize : total, request.Sort, request.SortDescending);
        }

        public async Task<ActivityReviewSummaryResult> GetReviewSummaryAsync(ActivityListRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // SECTION: Full-result review summary
            // MediaFilter is intentionally ignored so the summary chips remain stable
            // navigation buckets while search, activity type, dates, creator, and attachment
            // type filters continue to scope the aggregate.
            var query = ApplyBaseReviewFilters(CreateBaseQuery(), request);

            var all = await query.CountAsync(cancellationToken);
            var withMedia = await query.CountAsync(x => x.Attachments.Any(), cancellationToken);
            var photos = await query.SelectMany(x => x.Attachments).CountAsync(ActivityAttachmentClassifier.IsPhotoExpression, cancellationToken);
            var documents = await query.SelectMany(x => x.Attachments).CountAsync(ActivityAttachmentClassifier.IsDocumentExpression, cancellationToken);
            var videos = await query.SelectMany(x => x.Attachments).CountAsync(ActivityAttachmentClassifier.IsVideoExpression, cancellationToken);

            return new ActivityReviewSummaryResult(all, withMedia, photos, documents, videos);
        }

        private IQueryable<Activity> CreateBaseQuery()
        {
            return _dbContext.Activities
                .AsNoTracking()
                .Where(x => !x.IsDeleted);
        }

        private IQueryable<Activity> ApplyBaseReviewFilters(IQueryable<Activity> query, ActivityListRequest request)
        {
            // SECTION: Base review filters shared by rows and summary
            if (request.ActivityTypeId.HasValue)
            {
                query = query.Where(x => x.ActivityTypeId == request.ActivityTypeId.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.CreatedByUserId))
            {
                query = query.Where(x => x.CreatedByUserId == request.CreatedByUserId);
            }

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                query = ApplySearchFilter(query, request.Search);
            }

            if (request.FromDate.HasValue)
            {
                var from = IstClock.StartOfDayIstToUtc(request.FromDate.Value);
                query = query.Where(x => (x.ScheduledStartUtc ?? x.CreatedAtUtc) >= from);
            }

            if (request.ToDate.HasValue)
            {
                var toExclusive = IstClock.ExclusiveEndOfDayIstToUtc(request.ToDate.Value);
                query = query.Where(x => (x.ScheduledStartUtc ?? x.CreatedAtUtc) < toExclusive);
            }

            return request.AttachmentType switch
            {
                ActivityAttachmentTypeFilter.Pdf => query.Where(x => x.Attachments.AsQueryable().Any(ActivityAttachmentClassifier.IsPdfExpression)),
                ActivityAttachmentTypeFilter.Photo => query.Where(x => x.Attachments.AsQueryable().Any(ActivityAttachmentClassifier.IsPhotoExpression)),
                ActivityAttachmentTypeFilter.Video => query.Where(x => x.Attachments.AsQueryable().Any(ActivityAttachmentClassifier.IsVideoExpression)),
                _ => query
            };
        }

        private IQueryable<Activity> ApplySearchFilter(IQueryable<Activity> query, string search)
        {
            // SECTION: Provider-aware case-insensitive activity search
            var term = search.Trim();
            var like = BuildEscapedLikePattern(term);
            var providerName = _dbContext.Database.ProviderName ?? string.Empty;

            if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return query.Where(x =>
                    EF.Functions.ILike(x.Title, like, "\\") ||
                    (x.Description != null && EF.Functions.ILike(x.Description, like, "\\")) ||
                    (x.Location != null && EF.Functions.ILike(x.Location, like, "\\")) ||
                    EF.Functions.ILike(x.ActivityType.Name, like, "\\"));
            }

            if (providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedTerm = term.ToLowerInvariant();

                return query.Where(x =>
                    x.Title.ToLower().Contains(normalizedTerm) ||
                    (x.Description != null && x.Description.ToLower().Contains(normalizedTerm)) ||
                    (x.Location != null && x.Location.ToLower().Contains(normalizedTerm)) ||
                    x.ActivityType.Name.ToLower().Contains(normalizedTerm));
            }

            var normalizedLike = like.ToLowerInvariant();

            return query.Where(x =>
                EF.Functions.Like(x.Title.ToLower(), normalizedLike, "\\") ||
                (x.Description != null && EF.Functions.Like(x.Description.ToLower(), normalizedLike, "\\")) ||
                (x.Location != null && EF.Functions.Like(x.Location.ToLower(), normalizedLike, "\\")) ||
                EF.Functions.Like(x.ActivityType.Name.ToLower(), normalizedLike, "\\"));
        }

        private static string BuildEscapedLikePattern(string term)
        {
            // SECTION: Escape LIKE wildcard characters so search keeps literal contains semantics
            return $"%{term.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
        }

        private static IQueryable<Activity> ApplyMediaFilter(IQueryable<Activity> query, ActivityMediaFilter mediaFilter)
        {
            // SECTION: Media availability row filters
            return mediaFilter switch
            {
                ActivityMediaFilter.WithMedia => query.Where(x => x.Attachments.Any()),
                ActivityMediaFilter.WithoutMedia => query.Where(x => !x.Attachments.Any()),
                ActivityMediaFilter.Photos => query.Where(x => x.Attachments.AsQueryable().Any(ActivityAttachmentClassifier.IsPhotoExpression)),
                ActivityMediaFilter.Videos => query.Where(x => x.Attachments.AsQueryable().Any(ActivityAttachmentClassifier.IsVideoExpression)),
                ActivityMediaFilter.Documents => query.Where(x => x.Attachments.AsQueryable().Any(ActivityAttachmentClassifier.IsDocumentExpression)),
                _ => query
            };
        }

        public async Task AddAsync(Activity activity, CancellationToken cancellationToken = default)
        {
            await _dbContext.Activities.AddAsync(activity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAsync(Activity activity, CancellationToken cancellationToken = default)
        {
            _dbContext.Activities.Update(activity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(Activity activity, CancellationToken cancellationToken = default)
        {
            _dbContext.Activities.Remove(activity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task<ActivityAttachment?> GetAttachmentByIdAsync(int attachmentId, CancellationToken cancellationToken = default)
        {
            return _dbContext.ActivityAttachments
                .Include(x => x.Activity)
                .FirstOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        }

        public async Task AddAttachmentAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default)
        {
            await _dbContext.ActivityAttachments.AddAsync(attachment, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task RemoveAttachmentAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default)
        {
            _dbContext.ActivityAttachments.Remove(attachment);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
