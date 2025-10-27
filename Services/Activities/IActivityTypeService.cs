using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public interface IActivityTypeService
{
    Task<ActivityType> CreateAsync(ActivityTypeInput input, CancellationToken cancellationToken = default);

    Task<ActivityType> UpdateAsync(int activityTypeId, ActivityTypeInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityType>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed record ActivityTypeInput(string Name, string? Description, bool IsActive);
