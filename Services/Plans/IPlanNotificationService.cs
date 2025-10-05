using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Services.Plans;

public interface IPlanNotificationService
{
    Task NotifyPlanSubmittedAsync(
        PlanVersion plan,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task NotifyPlanApprovedAsync(
        PlanVersion plan,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task NotifyPlanRejectedAsync(
        PlanVersion plan,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
