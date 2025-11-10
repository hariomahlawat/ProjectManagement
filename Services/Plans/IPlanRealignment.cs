using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Plans
{
    // SECTION: Interface definition
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
