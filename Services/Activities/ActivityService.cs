using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Models.Activities;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityService : IActivityService
{
    private readonly IActivityRepository _activityRepository;
    private readonly IActivityInputValidator _inputValidator;
    private readonly IActivityAttachmentManager _attachmentManager;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IPrismMediaIngestionCoordinator _mediaIngestion;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(IActivityRepository activityRepository,
                           IActivityInputValidator inputValidator,
                           IActivityAttachmentManager attachmentManager,
                           IUserContext userContext,
                           IClock clock,
                           IPrismMediaIngestionCoordinator mediaIngestion,
                           ILogger<ActivityService> logger)
    {
        _activityRepository = activityRepository;
        _inputValidator = inputValidator;
        _attachmentManager = attachmentManager;
        _userContext = userContext;
        _clock = clock;
        _mediaIngestion = mediaIngestion ?? throw new ArgumentNullException(nameof(mediaIngestion));
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

        if (input.ExpectedRowVersion is { Length: > 0 } expectedRowVersion
            && !activity.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
        {
            throw new ActivityConcurrencyException(
                "This activity was updated by another user after you opened it. Reload the latest version before saving.");
        }

        activity.Title = input.Title.Trim();
        activity.Description = input.Description?.Trim();
        activity.Location = input.Location?.Trim();
        activity.ActivityTypeId = input.ActivityTypeId;
        activity.ScheduledStartUtc = input.ScheduledStartUtc;
        activity.ScheduledEndUtc = input.ScheduledEndUtc;
        activity.LastModifiedByUserId = RequireUserId();
        activity.LastModifiedAtUtc = _clock.UtcNow;

        try
        {
            await _activityRepository.UpdateAsync(activity, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ActivityConcurrencyException(
                "This activity was updated by another user while you were saving. Reload the latest version and try again.",
                ex);
        }

        await ReconcilePhotosAsync($"activity {activity.Id} metadata updated", cancellationToken);
        return activity;
    }

    public async Task DeleteAsync(int activityId, CancellationToken cancellationToken = default)
    {
        var activity = await _activityRepository.GetByIdAsync(activityId, cancellationToken);
        if (activity is null || activity.IsDeleted)
        {
            throw new KeyNotFoundException("Activity not found.");
        }

        EnsureCanDelete();

        var userId = RequireUserId();
        var now = _clock.UtcNow;
        activity.IsDeleted = true;
        activity.DeletedAtUtc = now;
        activity.DeletedByUserId = userId;
        activity.LastModifiedAtUtc = now;
        activity.LastModifiedByUserId = userId;

        // Commit the soft-delete first so the record cannot remain half-active if
        // external file cleanup later encounters an I/O problem. Attachment cleanup
        // is deliberately best-effort after the authoritative database state is safe.
        await _activityRepository.UpdateAsync(activity, cancellationToken);
        await ReconcilePhotosAsync($"activity {activity.Id} deleted", cancellationToken);

        try
        {
            await _attachmentManager.RemoveAllAsync(activity, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Activity {ActivityId} was deleted, but one or more attachment records could not be cleaned up immediately.",
                activity.Id);
        }
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

    public Task<ActivityReviewSummaryResult> GetReviewSummaryAsync(ActivityListRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // SECTION: Full-result review summary
        return _activityRepository.GetReviewSummaryAsync(request, cancellationToken);
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

        // Activity and attachment share the repository DbContext. Stamp the Activity before
        // the attachment save so the attachment row and audit metadata commit together in
        // the same SaveChanges call rather than leaving a stale Last modified timestamp.
        activity.LastModifiedByUserId = userId;
        activity.LastModifiedAtUtc = _clock.UtcNow;

        var attachment = await _attachmentManager.AddAsync(activity, upload, userId, cancellationToken);

        if (ActivityAttachmentClassifier.IsPhoto(attachment.OriginalFileName, attachment.ContentType))
        {
            await ReconcilePhotosAsync(
                $"activity photo {attachment.Id} added to activity {activity.Id}",
                cancellationToken);
        }

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
        var removedPhoto = ActivityAttachmentClassifier.IsPhoto(
            attachment.OriginalFileName,
            attachment.ContentType);

        // The tracked Activity is saved by RemoveAsync together with the attachment
        // deletion, keeping audit metadata and attachment state transactionally aligned.
        activity.LastModifiedByUserId = RequireUserId();
        activity.LastModifiedAtUtc = _clock.UtcNow;

        try
        {
            await _attachmentManager.RemoveAsync(attachment, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove attachment {AttachmentId} for activity {ActivityId}.", attachment.Id, activity.Id);
            throw;
        }

        if (removedPhoto)
        {
            await ReconcilePhotosAsync(
                $"activity photo {attachment.Id} removed from activity {activity.Id}",
                cancellationToken);
        }
    }

    private async Task ReconcilePhotosAsync(string reason, CancellationToken cancellationToken)
    {
        var result = await _mediaIngestion.ReconcileAfterSourceChangeAsync(reason, cancellationToken);
        if (!result.Succeeded && !string.Equals(result.Status, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Activity operation completed, but Photos ingestion was deferred. Reason={Reason}; Status={Status}; Error={Error}",
                reason,
                result.Status,
                result.Error);
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

    private void EnsureCanDelete()
    {
        var principal = _userContext.User;
        RequireUserId();

        if (!ActivityAuthorizationPolicy.CanDelete(principal))
        {
            throw new ActivityAuthorizationException("You are not authorised to delete this activity.");
        }
    }

    private void EnsureCanManage(Activity activity)
    {
        var userId = RequireUserId();
        if (!ActivityAuthorizationPolicy.CanManage(activity, _userContext.User, userId))
        {
            throw new ActivityAuthorizationException("You are not authorised to manage this activity.");
        }
    }

    private void EnsureCanManageAttachment(Activity activity, ActivityAttachment attachment)
    {
        var userId = RequireUserId();
        if (!ActivityAuthorizationPolicy.CanManageAttachment(activity, attachment, _userContext.User, userId))
        {
            throw new ActivityAuthorizationException("You are not authorised to manage this attachment.");
        }
    }

}
