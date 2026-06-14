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
                    x.Attachments.Count(a => a.ContentType == "application/pdf" || a.OriginalFileName.EndsWith(".pdf")),
                    x.Attachments.Count(a => a.ContentType.StartsWith("image/")),
                    x.Attachments.Count(a => a.ContentType.StartsWith("video/")),
                    x.Attachments
                        .OrderByDescending(a => a.ContentType.StartsWith("image/"))
                        .ThenByDescending(a => a.ContentType.StartsWith("video/"))
                        .ThenByDescending(a => a.ContentType == "application/pdf" || a.OriginalFileName.EndsWith(".pdf"))
                        .ThenByDescending(a => a.UploadedAtUtc)
                        .Take(3)
                        .Select(a => new ActivityMediaPreview(
                            a.Id,
                            a.OriginalFileName,
                            a.ContentType,
                            a.ContentType.StartsWith("image/") ? "Photo" : a.ContentType.StartsWith("video/") ? "Video" : "Document",
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
            var photos = await query.SelectMany(x => x.Attachments).CountAsync(a => a.ContentType.StartsWith("image/"), cancellationToken);
            var documents = await query.SelectMany(x => x.Attachments).CountAsync(a =>
                a.ContentType == "application/pdf" ||
                a.OriginalFileName.EndsWith(".pdf") ||
                a.ContentType.Contains("document") ||
                a.ContentType.Contains("spreadsheet") ||
                a.ContentType.Contains("presentation"), cancellationToken);
            var videos = await query.SelectMany(x => x.Attachments).CountAsync(a => a.ContentType.StartsWith("video/"), cancellationToken);

            return new ActivityReviewSummaryResult(all, withMedia, photos, documents, videos);
        }

        private IQueryable<Activity> CreateBaseQuery()
        {
            return _dbContext.Activities
                .AsNoTracking()
                .Where(x => !x.IsDeleted);
        }

        private static IQueryable<Activity> ApplyBaseReviewFilters(IQueryable<Activity> query, ActivityListRequest request)
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
                var term = request.Search.Trim();
                query = query.Where(x =>
                    x.Title.Contains(term) ||
                    (x.Description != null && x.Description.Contains(term)) ||
                    (x.Location != null && x.Location.Contains(term)) ||
                    x.ActivityType.Name.Contains(term));
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
                ActivityAttachmentTypeFilter.Pdf => query.Where(x => x.Attachments.Any(a =>
                    a.ContentType == "application/pdf" ||
                    a.OriginalFileName.EndsWith(".pdf"))),
                ActivityAttachmentTypeFilter.Photo => query.Where(x => x.Attachments.Any(a => a.ContentType.StartsWith("image/"))),
                ActivityAttachmentTypeFilter.Video => query.Where(x => x.Attachments.Any(a => a.ContentType.StartsWith("video/"))),
                _ => query
            };
        }

        private static IQueryable<Activity> ApplyMediaFilter(IQueryable<Activity> query, ActivityMediaFilter mediaFilter)
        {
            // SECTION: Media availability row filters
            return mediaFilter switch
            {
                ActivityMediaFilter.WithMedia => query.Where(x => x.Attachments.Any()),
                ActivityMediaFilter.WithoutMedia => query.Where(x => !x.Attachments.Any()),
                ActivityMediaFilter.Photos => query.Where(x => x.Attachments.Any(a => a.ContentType.StartsWith("image/"))),
                ActivityMediaFilter.Videos => query.Where(x => x.Attachments.Any(a => a.ContentType.StartsWith("video/"))),
                ActivityMediaFilter.Documents => query.Where(x => x.Attachments.Any(a =>
                    a.ContentType == "application/pdf" ||
                    a.OriginalFileName.EndsWith(".pdf") ||
                    a.ContentType.Contains("document") ||
                    a.ContentType.Contains("spreadsheet") ||
                    a.ContentType.Contains("presentation"))),
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
