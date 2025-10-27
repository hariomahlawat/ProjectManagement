using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public interface IActivityAttachmentManager
{
    Task<ActivityAttachment> AddAsync(Activity activity,
                                      ActivityAttachmentUpload upload,
                                      string userId,
                                      CancellationToken cancellationToken = default);

    Task RemoveAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default);

    Task RemoveAllAsync(Activity activity, CancellationToken cancellationToken = default);

    IReadOnlyList<ActivityAttachmentMetadata> CreateMetadata(Activity activity);
}
