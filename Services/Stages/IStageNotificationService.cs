using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Stages;

public interface IStageNotificationService
{
    Task NotifyStageStatusChangedAsync(
        ProjectStage stage,
        Project project,
        StageStatus previousStatus,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
