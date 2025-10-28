using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Activities;

public interface IActivityDeleteRequestService
{
    Task<int> RequestAsync(int activityId, string? reason, CancellationToken cancellationToken = default);

    Task ApproveAsync(int requestId, CancellationToken cancellationToken = default);

    Task RejectAsync(int requestId, string? reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityDeleteRequestSummary>> GetPendingAsync(CancellationToken cancellationToken = default);
}
