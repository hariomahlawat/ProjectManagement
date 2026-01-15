using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Approvals;

public interface IApprovalQueueService
{
    // SECTION: Pending approvals list
    Task<IReadOnlyList<ApprovalQueueItemVm>> GetPendingAsync(
        ApprovalQueueQuery query,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    // SECTION: Pending approvals count
    Task<int> GetPendingCountAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    // SECTION: Pending approval detail
    Task<ApprovalQueueDetailVm?> GetDetailAsync(
        ApprovalQueueType type,
        string requestId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
