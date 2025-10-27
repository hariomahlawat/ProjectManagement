using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Contracts.Activities
{
    public interface IActivityRepository
    {
        Task<Activity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default);

        Task AddAsync(Activity activity, CancellationToken cancellationToken = default);

        Task UpdateAsync(Activity activity, CancellationToken cancellationToken = default);

        Task DeleteAsync(Activity activity, CancellationToken cancellationToken = default);

        Task<ActivityAttachment?> GetAttachmentByIdAsync(int attachmentId, CancellationToken cancellationToken = default);

        Task AddAttachmentAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default);

        Task RemoveAttachmentAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default);
    }
}
