using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Plans
{
    public interface IPlanRealignment
    {
        Task CreateRealignmentDraftAsync(
            int projectId,
            string sourceStageCode,
            int delayDays,
            string triggeredByUserId,
            CancellationToken cancellationToken = default);
    }
}
