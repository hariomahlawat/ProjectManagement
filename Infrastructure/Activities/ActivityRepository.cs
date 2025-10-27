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
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Activities
                .AsNoTracking()
                .Include(x => x.ActivityType)
                .Include(x => x.Attachments)
                .Where(x => x.ActivityTypeId == activityTypeId && !x.IsDeleted)
                .OrderByDescending(x => x.ScheduledStartUtc ?? x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
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
