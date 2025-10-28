using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Data;
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

            var query = _dbContext.Activities
                .AsNoTracking()
                .Where(x => !x.IsDeleted);

            if (request.ActivityTypeId.HasValue)
            {
                query = query.Where(x => x.ActivityTypeId == request.ActivityTypeId.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.CreatedByUserId))
            {
                query = query.Where(x => x.CreatedByUserId == request.CreatedByUserId);
            }

            if (request.FromDate.HasValue)
            {
                var from = new DateTimeOffset(request.FromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                query = query.Where(x => (x.ScheduledStartUtc ?? x.CreatedAtUtc) >= from);
            }

            if (request.ToDate.HasValue)
            {
                var toExclusiveDate = request.ToDate.Value.AddDays(1);
                var toExclusive = new DateTimeOffset(toExclusiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                query = query.Where(x => (x.ScheduledStartUtc ?? x.CreatedAtUtc) < toExclusive);
            }

            query = request.AttachmentType switch
            {
                ActivityAttachmentTypeFilter.Pdf => query.Where(x => x.Attachments.Any(a =>
                    a.ContentType == "application/pdf" ||
                    a.OriginalFileName.EndsWith(".pdf"))),
                ActivityAttachmentTypeFilter.Photo => query.Where(x => x.Attachments.Any(a => a.ContentType.StartsWith("image/"))),
                ActivityAttachmentTypeFilter.Video => query.Where(x => x.Attachments.Any(a => a.ContentType.StartsWith("video/"))),
                _ => query
            };

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
                    x.ScheduledStartUtc,
                    x.ScheduledEndUtc,
                    x.CreatedAtUtc,
                    x.CreatedByUserId,
                    x.CreatedByUser != null ? x.CreatedByUser.FullName : null,
                    x.CreatedByUser != null ? x.CreatedByUser.Email : null,
                    x.Attachments.Count,
                    x.Attachments.Count(a => a.ContentType == "application/pdf" || a.OriginalFileName.EndsWith(".pdf")),
                    x.Attachments.Count(a => a.ContentType.StartsWith("image/")),
                    x.Attachments.Count(a => a.ContentType.StartsWith("video/"))))
                .ToListAsync(cancellationToken);

            return new ActivityListResult(items, total, page, pageSize > 0 ? pageSize : total, request.Sort, request.SortDescending);
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
