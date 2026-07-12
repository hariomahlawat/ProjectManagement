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
    public class ActivityTypeRepository : IActivityTypeRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public ActivityTypeRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<ActivityType?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return _dbContext.ActivityTypes
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<ActivityType>> ListAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.ActivityTypes
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(ActivityType activityType, CancellationToken cancellationToken = default)
        {
            await _dbContext.ActivityTypes.AddAsync(activityType, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAsync(ActivityType activityType, byte[]? rowVersion = null, CancellationToken cancellationToken = default)
        {
            if (rowVersion is { Length: > 0 })
            {
                _dbContext.Entry(activityType).Property(x => x.RowVersion).OriginalValue = rowVersion;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
