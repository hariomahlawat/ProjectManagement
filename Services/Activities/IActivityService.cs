using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public interface IActivityService
{
    Task<Activity> CreateAsync(ActivityInput input, CancellationToken cancellationToken = default);

    Task<Activity> UpdateAsync(int activityId, ActivityInput input, CancellationToken cancellationToken = default);

    Task DeleteAsync(int activityId, CancellationToken cancellationToken = default);

    Task<Activity?> GetAsync(int activityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityAttachmentMetadata>> GetAttachmentMetadataAsync(int activityId,
                                                                              CancellationToken cancellationToken = default);

    Task<ActivityAttachment> AddAttachmentAsync(int activityId,
                                                ActivityAttachmentUpload upload,
                                                CancellationToken cancellationToken = default);

    Task RemoveAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default);
}

public sealed record ActivityInput(
    string Title,
    string? Description,
    string? Location,
    int ActivityTypeId,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc);

public sealed record ActivityAttachmentUpload(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);
