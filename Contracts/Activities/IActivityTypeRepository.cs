using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Contracts.Activities
{
    public interface IActivityTypeRepository
    {
        Task<ActivityType?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ActivityType>> ListAsync(CancellationToken cancellationToken = default);

        Task AddAsync(ActivityType activityType, CancellationToken cancellationToken = default);

        Task UpdateAsync(ActivityType activityType, CancellationToken cancellationToken = default);
    }
}
