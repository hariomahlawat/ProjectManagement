using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Plans
{
    // SECTION: Null-object implementation
    public sealed class NullPlanRealignment : IPlanRealignment
    {
        public Task CreateRealignmentDraftAsync(
            int projectId,
            string sourceStageCode,
            int delayDays,
            string triggeredByUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
