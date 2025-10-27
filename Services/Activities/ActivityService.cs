using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityService : IActivityService
{
    private readonly IActivityRepository _activityRepository;
    private readonly IActivityInputValidator _inputValidator;
    private readonly IActivityAttachmentValidator _attachmentValidator;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(IActivityRepository activityRepository,
                           IActivityInputValidator inputValidator,
                           IActivityAttachmentValidator attachmentValidator,
                           IUserContext userContext,
                           IClock clock,
                           IUploadRootProvider uploadRootProvider,
                           ILogger<ActivityService> logger)
    {
        _activityRepository = activityRepository;
        _inputValidator = inputValidator;
        _attachmentValidator = attachmentValidator;
        _userContext = userContext;
        _clock = clock;
        _uploadRootProvider = uploadRootProvider;
        _logger = logger;
    }

    public async Task<Activity> CreateAsync(ActivityInput input, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await _inputValidator.ValidateAsync(input, existing: null, cancellationToken);

        var now = _clock.UtcNow;
        var activity = new Activity
        {
            Title = input.Title.Trim(),
            Description = input.Description?.Trim(),
            Location = input.Location?.Trim(),
            ActivityTypeId = input.ActivityTypeId,
            ScheduledStartUtc = input.ScheduledStartUtc,
            ScheduledEndUtc = input.ScheduledEndUtc,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            LastModifiedByUserId = userId,
            LastModifiedAtUtc = now,
            IsDeleted = false
        };

        await _activityRepository.AddAsync(activity, cancellationToken);
        return activity;
    }

    public async Task<Activity> UpdateAsync(int activityId, ActivityInput input, CancellationToken cancellationToken = default)
    {
        var activity = await _activityRepository.GetByIdAsync(activityId, cancellationToken);
        if (activity is null || activity.IsDeleted)
        {
            throw new KeyNotFoundException("Activity not found.");
        }

        EnsureCanManage(activity);
        await _inputValidator.ValidateAsync(input, activity, cancellationToken);

        activity.Title = input.Title.Trim();
        activity.Description = input.Description?.Trim();
        activity.Location = input.Location?.Trim();
        activity.ActivityTypeId = input.ActivityTypeId;
        activity.ScheduledStartUtc = input.ScheduledStartUtc;
        activity.ScheduledEndUtc = input.ScheduledEndUtc;
        activity.LastModifiedByUserId = RequireUserId();
        activity.LastModifiedAtUtc = _clock.UtcNow;

        await _activityRepository.UpdateAsync(activity, cancellationToken);
        return activity;
    }

    public async Task DeleteAsync(int activityId, CancellationToken cancellationToken = default)
    {
        var activity = await _activityRepository.GetByIdAsync(activityId, cancellationToken);
        if (activity is null || activity.IsDeleted)
        {
            throw new KeyNotFoundException("Activity not found.");
        }

        EnsureCanManage(activity);

        var userId = RequireUserId();
        var now = _clock.UtcNow;
        activity.IsDeleted = true;
        activity.DeletedAtUtc = now;
        activity.DeletedByUserId = userId;
        activity.LastModifiedAtUtc = now;
        activity.LastModifiedByUserId = userId;

        await _activityRepository.UpdateAsync(activity, cancellationToken);
    }

    public async Task<Activity?> GetAsync(int activityId, CancellationToken cancellationToken = default)
    {
        var activity = await _activityRepository.GetByIdAsync(activityId, cancellationToken);
        if (activity is null || activity.IsDeleted)
        {
            return null;
        }

        return activity;
    }

    public Task<IReadOnlyList<Activity>> ListByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default)
    {
        return _activityRepository.ListByTypeAsync(activityTypeId, cancellationToken);
    }

    public async Task<ActivityAttachment> AddAttachmentAsync(int activityId,
                                                             ActivityAttachmentUpload upload,
                                                             CancellationToken cancellationToken = default)
    {
        var activity = await _activityRepository.GetByIdAsync(activityId, cancellationToken);
        if (activity is null || activity.IsDeleted)
        {
            throw new KeyNotFoundException("Activity not found.");
        }

        EnsureCanManage(activity);
        _attachmentValidator.Validate(upload);

        var userId = RequireUserId();
        var storageKey = BuildStorageKey(activityId, upload.FileName);
        var fullPath = ResolveAbsolutePath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await upload.Content.CopyToAsync(fileStream, cancellationToken);
        }

        var attachment = new ActivityAttachment
        {
            ActivityId = activity.Id,
            StorageKey = storageKey,
            OriginalFileName = upload.FileName,
            ContentType = upload.ContentType,
            FileSize = upload.Length,
            UploadedByUserId = userId,
            UploadedAtUtc = _clock.UtcNow
        };

        await _activityRepository.AddAttachmentAsync(attachment, cancellationToken);
        return attachment;
    }

    public async Task RemoveAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _activityRepository.GetAttachmentByIdAsync(attachmentId, cancellationToken);
        if (attachment is null)
        {
            throw new KeyNotFoundException("Attachment not found.");
        }

        var activity = attachment.Activity;
        if (activity is null)
        {
            throw new KeyNotFoundException("Attachment activity not found.");
        }

        EnsureCanManageAttachment(activity, attachment);

        await _activityRepository.RemoveAttachmentAsync(attachment, cancellationToken);

        try
        {
            var fullPath = ResolveAbsolutePath(attachment.StorageKey);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete attachment file for activity {ActivityId}.", activity.Id);
        }
    }

    private string RequireUserId()
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ActivityAuthorizationException("A signed-in user is required.");
        }

        return userId;
    }

    private void EnsureCanManage(Activity activity)
    {
        var principal = _userContext.User;
        var userId = RequireUserId();

        if (IsAdminOrHod(principal))
        {
            return;
        }

        if (!string.Equals(activity.CreatedByUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActivityAuthorizationException("You are not authorised to manage this activity.");
        }
    }

    private void EnsureCanManageAttachment(Activity activity, ActivityAttachment attachment)
    {
        var principal = _userContext.User;
        var userId = RequireUserId();

        if (IsAdminOrHod(principal))
        {
            return;
        }

        if (!string.Equals(activity.CreatedByUserId, userId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(attachment.UploadedByUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ActivityAuthorizationException("You are not authorised to manage this attachment.");
        }
    }

    private static bool IsAdminOrHod(ClaimsPrincipal principal)
    {
        return principal.IsInRole("Admin") || principal.IsInRole("HoD");
    }

    private string BuildStorageKey(int activityId, string originalFileName)
    {
        var sanitized = ActivityAttachmentValidator.SanitizeFileName(originalFileName);
        var fileName = $"{Guid.NewGuid():N}_{sanitized}";
        return $"activities/{activityId}/{fileName}";
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        var relativePath = storageKey.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_uploadRootProvider.RootPath, relativePath);
    }
}
