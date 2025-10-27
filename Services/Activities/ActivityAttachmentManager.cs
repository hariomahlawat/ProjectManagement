using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityAttachmentManager : IActivityAttachmentManager
{
    public const int MaxAttachmentsPerActivity = 10;

    private readonly IActivityRepository _activityRepository;
    private readonly IActivityAttachmentStorage _storage;
    private readonly IActivityAttachmentValidator _validator;
    private readonly IClock _clock;

    public ActivityAttachmentManager(IActivityRepository activityRepository,
                                     IActivityAttachmentStorage storage,
                                     IActivityAttachmentValidator validator,
                                     IClock clock)
    {
        _activityRepository = activityRepository ?? throw new ArgumentNullException(nameof(activityRepository));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ActivityAttachment> AddAsync(Activity activity,
                                                   ActivityAttachmentUpload upload,
                                                   string userId,
                                                   CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        _validator.Validate(upload);

        if (activity.Attachments.Count >= MaxAttachmentsPerActivity)
        {
            throw CreateCountExceededException();
        }

        var storageResult = await _storage.SaveAsync(activity.Id, upload, cancellationToken);
        var now = _clock.UtcNow;
        var attachment = new ActivityAttachment
        {
            ActivityId = activity.Id,
            StorageKey = storageResult.StorageKey,
            OriginalFileName = storageResult.FileName,
            ContentType = upload.ContentType,
            FileSize = storageResult.FileSize,
            UploadedByUserId = userId,
            UploadedAtUtc = now
        };

        await _activityRepository.AddAttachmentAsync(attachment, cancellationToken);
        activity.Attachments.Add(attachment);
        return attachment;
    }

    public async Task RemoveAsync(ActivityAttachment attachment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        await _activityRepository.RemoveAttachmentAsync(attachment, cancellationToken);
        await _storage.DeleteAsync(attachment.StorageKey, cancellationToken);

        if (attachment.Activity is { Attachments: { } attachments })
        {
            attachments.Remove(attachment);
        }
    }

    public async Task RemoveAllAsync(Activity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Attachments.Count == 0)
        {
            return;
        }

        var attachments = activity.Attachments.ToList();
        foreach (var attachment in attachments)
        {
            await RemoveAsync(attachment, cancellationToken);
        }
    }

    public IReadOnlyList<ActivityAttachmentMetadata> CreateMetadata(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Attachments.Count == 0)
        {
            return Array.Empty<ActivityAttachmentMetadata>();
        }

        return activity.Attachments
            .OrderByDescending(a => a.UploadedAtUtc)
            .Select(CreateMetadata)
            .ToList();
    }

    private ActivityAttachmentMetadata CreateMetadata(ActivityAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        var fileName = ActivityAttachmentValidator.SanitizeFileName(attachment.OriginalFileName);
        var downloadUrl = _storage.GetDownloadUrl(attachment.StorageKey);

        return new ActivityAttachmentMetadata(
            attachment.Id,
            fileName,
            attachment.ContentType,
            attachment.FileSize,
            downloadUrl,
            attachment.StorageKey,
            attachment.UploadedAtUtc,
            attachment.UploadedByUserId);
    }

    private static ActivityValidationException CreateCountExceededException()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Activity.Attachments)] =
            [
                $"A maximum of {MaxAttachmentsPerActivity} attachments is allowed per activity."
            ]
        };

        return new ActivityValidationException(errors);
    }
}
