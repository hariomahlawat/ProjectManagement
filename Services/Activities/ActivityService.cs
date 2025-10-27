using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityService : IActivityService
{
    private readonly IActivityRepository _activityRepository;
    private readonly IActivityInputValidator _inputValidator;
    private readonly IActivityAttachmentManager _attachmentManager;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(IActivityRepository activityRepository,
                           IActivityInputValidator inputValidator,
                           IActivityAttachmentManager attachmentManager,
                           IUserContext userContext,
                           IClock clock,
                           ILogger<ActivityService> logger)
    {
        _activityRepository = activityRepository;
        _inputValidator = inputValidator;
        _attachmentManager = attachmentManager;
        _userContext = userContext;
        _clock = clock;
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

        await _attachmentManager.RemoveAllAsync(activity, cancellationToken);

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

    public Task<ActivityListResult> ListAsync(ActivityListRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize;
        if (pageSize > 0)
        {
            pageSize = Math.Min(pageSize, 100);
        }

        var normalized = request with
        {
            Page = page,
            PageSize = pageSize
        };

        return _activityRepository.ListAsync(normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityAttachmentMetadata>> GetAttachmentMetadataAsync(int activityId,
                                                                                           CancellationToken cancellationToken = default)
    {
        var activity = await _activityRepository.GetByIdAsync(activityId, cancellationToken);
        if (activity is null || activity.IsDeleted)
        {
            return Array.Empty<ActivityAttachmentMetadata>();
        }

        return _attachmentManager.CreateMetadata(activity);
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
        var userId = RequireUserId();

        var attachment = await _attachmentManager.AddAsync(activity, upload, userId, cancellationToken);

        activity.LastModifiedByUserId = userId;
        activity.LastModifiedAtUtc = _clock.UtcNow;
        await _activityRepository.UpdateAsync(activity, cancellationToken);

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

        try
        {
            await _attachmentManager.RemoveAsync(attachment, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove attachment {AttachmentId} for activity {ActivityId}.", attachment.Id, activity.Id);
            throw;
        }

        activity.LastModifiedByUserId = RequireUserId();
        activity.LastModifiedAtUtc = _clock.UtcNow;
        await _activityRepository.UpdateAsync(activity, cancellationToken);
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
}
