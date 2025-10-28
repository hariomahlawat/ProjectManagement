using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityDeleteRequestService : IActivityDeleteRequestService
{
    private static readonly string[] RequesterRoles = ActivityRoleLists.ManagerRoles;
    private static readonly string[] ApproverRoles = ActivityRoleLists.DeleteApproverRoles;

    private readonly ApplicationDbContext _dbContext;
    private readonly IActivityService _activityService;
    private readonly IActivityNotificationService _notificationService;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly ILogger<ActivityDeleteRequestService> _logger;

    public ActivityDeleteRequestService(
        ApplicationDbContext dbContext,
        IActivityService activityService,
        IActivityNotificationService notificationService,
        IUserContext userContext,
        IClock clock,
        ILogger<ActivityDeleteRequestService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> RequestAsync(int activityId, string? reason, CancellationToken cancellationToken = default)
    {
        if (activityId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activityId));
        }

        EnsureCanRequest();
        var userId = RequireUserId();

        var activity = await _dbContext.Activities
            .AsNoTracking()
            .Include(x => x.ActivityType)
            .FirstOrDefaultAsync(x => x.Id == activityId && !x.IsDeleted, cancellationToken);

        if (activity is null)
        {
            throw new KeyNotFoundException("Activity not found.");
        }

        var hasPending = await _dbContext.ActivityDeleteRequests
            .AnyAsync(r => r.ActivityId == activityId && r.ApprovedAtUtc == null && r.RejectedAtUtc == null, cancellationToken);

        if (hasPending)
        {
            throw new InvalidOperationException("A delete request is already pending for this activity.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var now = _clock.UtcNow;

        var request = new ActivityDeleteRequest
        {
            ActivityId = activityId,
            RequestedByUserId = userId,
            RequestedAtUtc = now,
            Reason = trimmedReason
        };

        await _dbContext.ActivityDeleteRequests.AddAsync(request, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var requester = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        var context = new ActivityDeleteNotificationContext(
            request.Id,
            activity.Id,
            activity.Title,
            activity.ActivityType?.Name ?? "Activity",
            activity.Location,
            activity.ScheduledStartUtc,
            request.RequestedAtUtc,
            request.RequestedByUserId,
            Normalize(requester?.FullName),
            Normalize(requester?.Email),
            request.Reason);

        await NotifyAsync(() => _notificationService.NotifyDeleteRequestedAsync(context, cancellationToken));

        return request.Id;
    }

    public async Task ApproveAsync(int requestId, CancellationToken cancellationToken = default)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        EnsureCanApprove();
        var approverId = RequireUserId();

        var request = await _dbContext.ActivityDeleteRequests
            .Include(r => r.Activity)
                .ThenInclude(a => a.ActivityType)
            .Include(r => r.RequestedByUser)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null)
        {
            throw new KeyNotFoundException("Delete request not found.");
        }

        if (request.ApprovedAtUtc is not null || request.RejectedAtUtc is not null)
        {
            throw new InvalidOperationException("The delete request is no longer pending.");
        }

        if (request.Activity is null || request.Activity.IsDeleted)
        {
            throw new InvalidOperationException("The associated activity could not be found.");
        }

        await _activityService.DeleteAsync(request.ActivityId, cancellationToken);

        request.ApprovedAtUtc = _clock.UtcNow;
        request.ApprovedByUserId = approverId;
        request.RejectedAtUtc = null;
        request.RejectedByUserId = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var context = CreateNotificationContext(request);
        await NotifyAsync(() => _notificationService.NotifyDeleteApprovedAsync(context, approverId, cancellationToken));
    }

    public async Task RejectAsync(int requestId, string? reason, CancellationToken cancellationToken = default)
    {
        if (requestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        EnsureCanApprove();
        var approverId = RequireUserId();

        var request = await _dbContext.ActivityDeleteRequests
            .Include(r => r.Activity)
                .ThenInclude(a => a.ActivityType)
            .Include(r => r.RequestedByUser)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null)
        {
            throw new KeyNotFoundException("Delete request not found.");
        }

        if (request.ApprovedAtUtc is not null || request.RejectedAtUtc is not null)
        {
            throw new InvalidOperationException("The delete request is no longer pending.");
        }

        request.RejectedAtUtc = _clock.UtcNow;
        request.RejectedByUserId = approverId;
        request.ApprovedAtUtc = null;
        request.ApprovedByUserId = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var context = CreateNotificationContext(request);
        var notes = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        await NotifyAsync(() => _notificationService.NotifyDeleteRejectedAsync(context, approverId, notes ?? string.Empty, cancellationToken));
    }

    public async Task<IReadOnlyList<ActivityDeleteRequestSummary>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _dbContext.ActivityDeleteRequests
            .AsNoTracking()
            .Where(r => r.ApprovedAtUtc == null && r.RejectedAtUtc == null)
            .OrderBy(r => r.RequestedAtUtc)
            .Select(r => new ActivityDeleteRequestSummary(
                r.Id,
                r.ActivityId,
                r.Activity.Title,
                r.Activity.ActivityType != null ? r.Activity.ActivityType.Name : "Activity",
                r.Activity.Location,
                r.Activity.ScheduledStartUtc,
                r.RequestedAtUtc,
                r.RequestedByUserId,
                r.RequestedByUser != null ? Normalize(r.RequestedByUser.FullName) : null,
                r.RequestedByUser != null ? Normalize(r.RequestedByUser.Email) : null,
                r.Reason,
                r.RowVersion))
            .ToListAsync(cancellationToken);

        return pending;
    }

    private async Task NotifyAsync(Func<Task> notification)
    {
        try
        {
            await notification();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish activity delete notification.");
        }
    }

    private void EnsureCanRequest()
    {
        if (!IsInAnyRole(_userContext.User, RequesterRoles))
        {
            throw new ActivityAuthorizationException("You are not authorised to request deletion for this activity.");
        }
    }

    private void EnsureCanApprove()
    {
        if (!IsInAnyRole(_userContext.User, ApproverRoles))
        {
            throw new ActivityAuthorizationException("You are not authorised to decide activity delete requests.");
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

    private static bool IsInAnyRole(ClaimsPrincipal principal, IReadOnlyList<string> roles)
    {
        foreach (var role in roles)
        {
            if (principal.IsInRole(role))
            {
                return true;
            }
        }

        return false;
    }

    private static ActivityDeleteNotificationContext CreateNotificationContext(ActivityDeleteRequest request)
    {
        if (request.Activity is null)
        {
            throw new InvalidOperationException("The request does not include its activity.");
        }

        return new ActivityDeleteNotificationContext(
            request.Id,
            request.ActivityId,
            request.Activity.Title,
            request.Activity.ActivityType?.Name ?? "Activity",
            request.Activity.Location,
            request.Activity.ScheduledStartUtc,
            request.RequestedAtUtc,
            request.RequestedByUserId,
            Normalize(request.RequestedByUser?.FullName),
            Normalize(request.RequestedByUser?.Email),
            request.Reason);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
