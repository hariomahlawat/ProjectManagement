using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Hubs;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Features.Notifications;

/// <summary>
/// Maps the authenticated notification API used by the notification bell and Notification Centre.
/// Read endpoints are safe GET operations; every state-changing endpoint requires antiforgery
/// validation and returns the same typed mutation contract that is broadcast through SignalR.
/// </summary>
public static class NotificationApi
{
    private static readonly HashSet<string> SupportedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "inbox",
        "all",
        "unread",
        "action",
        "approvals",
        "collaboration",
        "assignments",
        "lifecycle",
        "documents",
        "tasks",
        "muted",
    };

    public static IEndpointRouteBuilder MapNotificationApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/notifications")
            .RequireAuthorization()
            .WithTags("Notifications");

        group.MapGet("", GetPageAsync);
        group.MapGet("/count", GetUnreadCountAsync);

        group.MapPost("/read", MarkManyReadAsync);
        group.MapPost("/unread", MarkManyUnreadAsync);
        group.MapPost("/read-all", MarkAllReadAsync);
        group.MapPost("/seen", MarkSeenAsync);

        group.MapPost("/projects/{projectId:int}/mute", MuteProjectAsync);
        group.MapDelete("/projects/{projectId:int}/mute", UnmuteProjectAsync);

        // Backward-compatible single-item routes. Keep these while older cached clients may still
        // be active; they use the same mutation implementation and security requirements.
        group.MapPost("/{id:int}/read", MarkSingleReadAsync);
        group.MapDelete("/{id:int}/read", MarkSingleUnreadAsync);

        return endpoints;
    }

    private static async Task<IResult> GetPageAsync(
        [AsParameters] NotificationListRequest request,
        HttpContext httpContext,
        UserNotificationService notifications,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not null and not "" and not "all" and not "read" and not "unread")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = new[] { "Status must be all, read or unread." }
            });
        }

        if (request.UnreadOnly == true && status == "read")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = new[] { "The read status cannot be combined with unreadOnly=true." }
            });
        }

        if (request.Limit is <= 0 or > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = new[] { "Limit must be between 1 and 100." }
            });
        }

        if (request.ProjectId is <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["projectId"] = new[] { "Project identifier must be a positive integer." }
            });
        }

        var folder = request.Folder?.Trim();
        if (!string.IsNullOrWhiteSpace(folder) && !SupportedFolders.Contains(folder))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["folder"] = new[] { "The requested notification folder is not supported." }
            });
        }

        if (request.Search is { Length: > 120 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["search"] = new[] { "Search text cannot exceed 120 characters." }
            });
        }

        if (request.Module is { Length: > 64 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["module"] = new[] { "Module cannot exceed 64 characters." }
            });
        }

        if (request.Cursor is { Length: > 256 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["cursor"] = new[] { "The paging cursor is invalid." }
            });
        }

        SetNoStoreHeaders(httpContext.Response);

        var options = new NotificationListOptions
        {
            Limit = request.Limit,
            OnlyUnread = request.UnreadOnly == true || status == "unread",
            OnlyRead = status == "read",
            ProjectId = request.ProjectId,
            Cursor = request.Cursor,
            Search = request.Search,
            Module = request.Module,
            Folder = folder,
            IncludeMuted = request.IncludeMuted ?? false,
            OnlyMuted = string.Equals(folder, "muted", StringComparison.OrdinalIgnoreCase),
            IncludeFilterOptions = request.IncludeFilterOptions ?? false,
        };

        var page = await notifications.ListPageAsync(
            httpContext.User,
            userId,
            options,
            cancellationToken);

        return Results.Ok(page);
    }

    private static async Task<IResult> GetUnreadCountAsync(
        HttpContext httpContext,
        UserNotificationService notifications,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        SetNoStoreHeaders(httpContext.Response);

        var unread = await notifications.CountUnreadAsync(
            httpContext.User,
            userId,
            cancellationToken);

        return Results.Ok(new NotificationCountDto(unread));
    }

    private static Task<IResult> MarkManyReadAsync(
        NotificationIdsRequest request,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
        => CompleteReadMutationAsync(
            request,
            markRead: true,
            httpContext,
            notifications,
            hubContext,
            antiforgery,
            cancellationToken);

    private static Task<IResult> MarkManyUnreadAsync(
        NotificationIdsRequest request,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
        => CompleteReadMutationAsync(
            request,
            markRead: false,
            httpContext,
            notifications,
            hubContext,
            antiforgery,
            cancellationToken);

    private static Task<IResult> MarkSingleReadAsync(
        int id,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
        => CompleteReadMutationAsync(
            new NotificationIdsRequest(new[] { id }),
            markRead: true,
            httpContext,
            notifications,
            hubContext,
            antiforgery,
            cancellationToken);

    private static Task<IResult> MarkSingleUnreadAsync(
        int id,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
        => CompleteReadMutationAsync(
            new NotificationIdsRequest(new[] { id }),
            markRead: false,
            httpContext,
            notifications,
            hubContext,
            antiforgery,
            cancellationToken);

    private static async Task<IResult> CompleteReadMutationAsync(
        NotificationIdsRequest? request,
        bool markRead,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var antiforgeryFailure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (antiforgeryFailure is not null)
        {
            return antiforgeryFailure;
        }

        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        var validation = ValidateIds(request, out var notificationIds);
        if (validation is not null)
        {
            return validation;
        }

        var result = markRead
            ? await notifications.MarkManyReadAsync(
                httpContext.User,
                userId,
                notificationIds,
                cancellationToken)
            : await notifications.MarkManyUnreadAsync(
                httpContext.User,
                userId,
                notificationIds,
                cancellationToken);

        return await MapReadMutationResultAsync(
            result,
            httpContext,
            httpContext.User,
            userId,
            notifications,
            hubContext,
            cancellationToken);
    }

    private static async Task<IResult> MarkAllReadAsync(
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var antiforgeryFailure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (antiforgeryFailure is not null)
        {
            return antiforgeryFailure;
        }

        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await notifications.MarkAllReadAsync(
            httpContext.User,
            userId,
            cancellationToken);

        return await MapReadMutationResultAsync(
            result,
            httpContext,
            httpContext.User,
            userId,
            notifications,
            hubContext,
            cancellationToken);
    }

    private static async Task<IResult> MarkSeenAsync(
        NotificationIdsRequest? request,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var antiforgeryFailure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (antiforgeryFailure is not null)
        {
            return antiforgeryFailure;
        }

        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        var validation = ValidateIds(request, out var notificationIds);
        if (validation is not null)
        {
            return validation;
        }

        var result = await notifications.MarkSeenAsync(
            httpContext.User,
            userId,
            notificationIds,
            cancellationToken);

        return result.Result switch
        {
            NotificationOperationResult.Success => await CompleteSeenMutationAsync(
                result,
                httpContext,
                userId,
                hubContext),
            NotificationOperationResult.NotFound => Results.NotFound(new
            {
                error = "No accessible notification matched the supplied identifiers."
            }),
            NotificationOperationResult.Forbidden => Results.Forbid(),
            _ => Results.BadRequest(new { error = "The notification seen state could not be updated." })
        };
    }

    private static async Task<IResult> CompleteSeenMutationAsync(
        NotificationSeenMutationResult result,
        HttpContext httpContext,
        string userId,
        IHubContext<NotificationsHub, INotificationsClient> hubContext)
    {
        var dto = new NotificationSeenDto(
            result.NotificationIds,
            result.SeenUtc,
            result.NotificationIds.Count);

        await BroadcastBestEffortAsync(
            httpContext,
            userId,
            "seen-state",
            hubContext.Clients.User(userId).ReceiveNotificationSeen(dto));

        return Results.Ok(dto);
    }

    private static Task<IResult> MuteProjectAsync(
        int projectId,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
        => CompleteProjectMuteAsync(
            projectId,
            muted: true,
            httpContext,
            notifications,
            hubContext,
            antiforgery,
            cancellationToken);

    private static Task<IResult> UnmuteProjectAsync(
        int projectId,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
        => CompleteProjectMuteAsync(
            projectId,
            muted: false,
            httpContext,
            notifications,
            hubContext,
            antiforgery,
            cancellationToken);

    private static async Task<IResult> CompleteProjectMuteAsync(
        int projectId,
        bool muted,
        HttpContext httpContext,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var antiforgeryFailure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (antiforgeryFailure is not null)
        {
            return antiforgeryFailure;
        }

        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        if (projectId <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["projectId"] = new[] { "A valid project identifier is required." }
            });
        }

        var result = await notifications.SetProjectMuteDetailedAsync(
            httpContext.User,
            userId,
            projectId,
            muted,
            cancellationToken);

        if (result.Result == NotificationOperationResult.NotFound)
        {
            return Results.NotFound(new { error = "The project was not found." });
        }

        if (result.Result == NotificationOperationResult.Forbidden)
        {
            return Results.Forbid();
        }

        if (result.Result != NotificationOperationResult.Success)
        {
            return Results.BadRequest(new { error = "The project notification preference could not be updated." });
        }

        var unread = await notifications.CountUnreadAsync(
            httpContext.User,
            userId,
            cancellationToken);
        var dto = new NotificationProjectMuteDto(
            result.ProjectId,
            result.IsMuted,
            result.ChangedNotificationIds,
            unread);

        await BroadcastBestEffortAsync(
            httpContext,
            userId,
            "project-mute",
            hubContext.Clients.User(userId).ReceiveProjectMuteChanged(dto),
            hubContext.Clients.User(userId).ReceiveUnreadCount(unread));

        return Results.Ok(dto);
    }

    private static async Task<IResult> MapReadMutationResultAsync(
        NotificationReadMutationResult result,
        HttpContext httpContext,
        ClaimsPrincipal principal,
        string userId,
        UserNotificationService notifications,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        CancellationToken cancellationToken)
    {
        if (result.Result == NotificationOperationResult.NotFound)
        {
            return Results.NotFound(new
            {
                error = "No accessible notification matched the supplied identifiers."
            });
        }

        if (result.Result == NotificationOperationResult.Forbidden)
        {
            return Results.Forbid();
        }

        if (result.Result != NotificationOperationResult.Success)
        {
            return Results.BadRequest(new { error = "The notification state could not be updated." });
        }

        var unread = await notifications.CountUnreadAsync(
            principal,
            userId,
            cancellationToken);
        var dto = new NotificationMutationDto(
            result.NotificationIds,
            result.IsRead,
            result.ReadUtc,
            result.SeenUtc,
            result.AppliesToAll,
            result.AffectedCount,
            unread);

        await BroadcastBestEffortAsync(
            httpContext,
            userId,
            "read-state",
            hubContext.Clients.User(userId).ReceiveNotificationStateChanged(dto),
            hubContext.Clients.User(userId).ReceiveUnreadCount(unread));

        return Results.Ok(dto);
    }

    private static async Task BroadcastBestEffortAsync(
        HttpContext httpContext,
        string userId,
        string mutationType,
        params Task[] broadcasts)
    {
        try
        {
            await Task.WhenAll(broadcasts);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller disconnected after the database mutation committed. There is no client
            // left to receive the HTTP response; polling or the next page load will reconcile state.
        }
        catch (Exception exception)
        {
            // The database mutation is already committed. Realtime delivery is an optimisation;
            // a temporary SignalR/backplane failure must not turn a successful idempotent update
            // into an HTTP 500 that encourages the browser to retry the same mutation.
            var loggerFactory = httpContext.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("ProjectManagement.Features.Notifications.NotificationApi");
            logger.LogWarning(
                exception,
                "Notification {MutationType} mutation succeeded for user {UserId}, but realtime broadcast failed. Polling will reconcile client state.",
                mutationType,
                userId);
        }
    }

    private static async Task<IResult?> ValidateAntiforgeryAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Antiforgery validation failed",
                detail: "The request security token is missing or invalid. Refresh the page and retry the operation.");
        }
    }

    private static IResult? ValidateIds(
        NotificationIdsRequest? request,
        out IReadOnlyCollection<int> notificationIds)
    {
        notificationIds = request?.Ids ?? Array.Empty<int>();

        if (notificationIds.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ids"] = new[] { "At least one notification identifier is required." }
            });
        }

        if (notificationIds.Count > 500)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ids"] = new[] { "A maximum of 500 notification identifiers may be updated in one request." }
            });
        }

        if (notificationIds.Any(id => id <= 0))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ids"] = new[] { "Notification identifiers must be positive integers." }
            });
        }

        notificationIds = notificationIds.Distinct().ToArray();
        return null;
    }

    private static void SetNoStoreHeaders(HttpResponse response)
    {
        response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["Expires"] = "0";
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out string userId)
    {
        userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(userId);
    }
}
