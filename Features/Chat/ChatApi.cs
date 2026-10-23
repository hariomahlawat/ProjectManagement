using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Hubs;
using ProjectManagement.Services.Chat;

namespace ProjectManagement.Features.Chat;

/// <summary>
/// Chat endpoints. Reads are GETs scoped to the signed-in user; sending and marking read
/// require antiforgery validation and then push the change to both people through
/// <see cref="ChatHub"/>. Every endpoint answers 404 while chat is switched off.
/// </summary>
public static class ChatApi
{
    public const string SendRateLimitPolicy = "chat-send";

    public const string ChangeRateLimitPolicy = "chat-change";

    public const string SearchRateLimitPolicy = "chat-search";

    /// <summary>
    /// Set on the 404 that every endpoint answers while chat is switched off, so the browser can
    /// tell "chat is off" (stand down) from an ordinary missing resource (show an error).
    /// </summary>
    public const string DisabledHeader = "X-Chat-Disabled";

    public static IEndpointRouteBuilder MapChatApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/chat")
            .RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<ChatOptions>>();
                if (options.CurrentValue.Enabled)
                {
                    return await next(context);
                }

                context.HttpContext.Response.Headers[DisabledHeader] = "true";
                return Results.NotFound();
            })
            .WithTags("Chat");

        group.MapGet("/contacts", GetContactsAsync);
        group.MapGet("/with/{peerUserId}/messages", GetHistoryAsync);
        group.MapGet("/search", SearchAsync).RequireRateLimiting(SearchRateLimitPolicy);
        group.MapGet("/status", GetStatusAsync);
        group.MapPost("/status", SetStatusAsync).RequireRateLimiting(ChangeRateLimitPolicy);
        group.MapPost("/messages/{messageId:long}/unsend", UnsendAsync).RequireRateLimiting(ChangeRateLimitPolicy);
        group.MapGet("/unsent", GetUnsentSinceAsync);

        group.MapGet("/groups", ListGroupsAsync);
        group.MapPost("/groups", CreateGroupAsync).RequireRateLimiting(ChangeRateLimitPolicy);
        group.MapGet("/groups/{groupId:long}", GetGroupAsync);
        group.MapGet("/groups/{groupId:long}/messages", GetGroupHistoryAsync);
        group.MapPost("/groups/{groupId:long}/messages", SendToGroupAsync).RequireRateLimiting(SendRateLimitPolicy);
        group.MapPost("/groups/{groupId:long}/read", MarkGroupReadAsync);
        group.MapPost("/groups/{groupId:long}/members", AddGroupMembersAsync).RequireRateLimiting(ChangeRateLimitPolicy);
        group.MapPost("/groups/{groupId:long}/members/{memberUserId}/remove", RemoveGroupMemberAsync).RequireRateLimiting(ChangeRateLimitPolicy);
        group.MapPost("/groups/{groupId:long}/leave", LeaveGroupAsync).RequireRateLimiting(ChangeRateLimitPolicy);
        group.MapPost("/groups/{groupId:long}/rename", RenameGroupAsync).RequireRateLimiting(ChangeRateLimitPolicy);
        group.MapPost("/messages", SendAsync).RequireRateLimiting(SendRateLimitPolicy);
        group.MapPost("/with/{peerUserId}/read", MarkReadAsync);

        return endpoints;
    }

    /// <summary>Per-user fixed window; anonymous requests never reach here (the group requires sign-in).</summary>
    public static RateLimitPartition<string> GetSendPartition(HttpContext context, ChatOptions options)
        => GetUserPartition(context, options.SendPermitLimit, options.SendWindowSeconds);

    public static RateLimitPartition<string> GetChangePartition(HttpContext context, ChatOptions options)
        => GetUserPartition(context, options.ChangePermitLimit, options.ChangeWindowSeconds);

    public static RateLimitPartition<string> GetSearchPartition(HttpContext context, ChatOptions options)
        => GetUserPartition(context, options.SearchPermitLimit, options.SearchWindowSeconds);

    /// <summary>Any throttled chat request (sends, changes, searches) answers 429.</summary>
    public static bool IsChatRequest(HttpContext context)
        => context.Request.Path.StartsWithSegments("/api/chat", StringComparison.OrdinalIgnoreCase);

    private static RateLimitPartition<string> GetUserPartition(HttpContext context, int permitLimit, int windowSeconds)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    private static async Task<IResult> GetContactsAsync(
        HttpContext httpContext,
        ChatService chat,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        SetNoStoreHeaders(httpContext.Response);
        return Results.Ok(await chat.ListContactsAsync(userId, cancellationToken));
    }

    private static async Task<IResult> GetHistoryAsync(
        string peerUserId,
        [FromQuery(Name = "before")] long? beforeMessageId,
        [FromQuery(Name = "after")] long? afterMessageId,
        [FromQuery(Name = "limit")] int? limit,
        HttpContext httpContext,
        ChatService chat,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        SetNoStoreHeaders(httpContext.Response);
        var history = await chat.GetHistoryAsync(userId, peerUserId, beforeMessageId, limit ?? 40, cancellationToken, afterMessageId);
        return Results.Ok(history);
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext httpContext,
        ChatService chat,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        SetNoStoreHeaders(httpContext.Response);
        return Results.Ok(new ChatStatusDto(userId, await chat.GetStatusAsync(userId, cancellationToken)));
    }

    private static async Task<IResult> SetStatusAsync(
        ChatStatusRequest? request,
        HttpContext httpContext,
        ChatService chat,
        IHubContext<ChatHub, IChatClient> hub,
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

        if (request?.Message is { Length: > 1000 })
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Status not saved", detail: "The status message is too long.");
        }

        var stored = await chat.SetStatusAsync(userId, request?.Message, cancellationToken);
        var status = new ChatStatusDto(userId, stored);
        // Everyone's buddy list shows it, including this user's other tabs.
        await hub.Clients.All.StatusChanged(status);
        return Results.Ok(status);
    }

    private static async Task<IResult> GetUnsentSinceAsync(
        [FromQuery(Name = "since")] DateTimeOffset? since,
        [FromQuery(Name = "before")] long? beforeMessageId,
        HttpContext httpContext,
        ChatService chat,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        if (since is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: "since is required.");
        }

        SetNoStoreHeaders(httpContext.Response);
        return Results.Ok(await chat.ListUnsentSinceAsync(userId, since.Value, beforeMessageId, cancellationToken));
    }

    private static async Task<IResult> UnsendAsync(
        long messageId,
        HttpContext httpContext,
        ChatService chat,
        IHubContext<ChatHub, IChatClient> hub,
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

        var result = await chat.UnsendAsync(userId, messageId, cancellationToken);
        switch (result.Status)
        {
            case ChatUnsendStatus.Unsent:
                var message = result.Message!;
                var unsentTo = hub.Clients.Users(result.NotifyUserIds ?? new[] { message.SenderUserId, message.RecipientUserId });
                await (message.GroupId is null ? unsentTo.MessageUnsent(message) : unsentTo.GroupMessageUnsent(message));
                return Results.Ok(message);
            case ChatUnsendStatus.TooLate:
                var minutes = (int)Math.Round(chat.UnsendWindow.TotalMinutes);
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Too late to unsend",
                    detail: $"A message can only be unsent within {minutes} minute{(minutes == 1 ? string.Empty : "s")} of sending it.");
            default:
                // Not 404: the browser reads 404 from /api/chat as "chat is switched off".
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Cannot unsend",
                    detail: "Only your own messages can be unsent.");
        }
    }

    private static async Task<IResult> SearchAsync(
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "groups")] bool? includeGroups,
        HttpContext httpContext,
        ChatService chat,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        if (query is { Length: > 200 })
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid search", detail: "Search text cannot exceed 200 characters.");
        }

        SetNoStoreHeaders(httpContext.Response);
        return Results.Ok(await chat.SearchAsync(userId, query, limit ?? ChatService.MaxSearchResults, cancellationToken, includeGroups == true));
    }

    private static async Task<IResult> SendAsync(
        ChatSendRequest? request,
        HttpContext httpContext,
        ChatService chat,
        IHubContext<ChatHub, IChatClient> hub,
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

        if (request is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Message not sent", detail: "The message was empty.");
        }

        var result = await chat.SendAsync(userId, request, cancellationToken);
        switch (result.Status)
        {
            case ChatSendStatus.Sent:
                var message = result.Message!;
                // Pushed after the save, so a receiver never sees a message that was rolled back.
                // A duplicate retry pushes again; browsers de-duplicate by message id.
                await hub.Clients.Users(message.SenderUserId, message.RecipientUserId).ReceiveMessage(message);
                return Results.Ok(message);
            case ChatSendStatus.RecipientUnavailable:
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Message not sent", detail: result.Error);
            default:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Message not sent", detail: result.Error);
        }
    }

    private static async Task<IResult> MarkReadAsync(
        string peerUserId,
        ChatMarkReadRequest? request,
        HttpContext httpContext,
        ChatService chat,
        IHubContext<ChatHub, IChatClient> hub,
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

        if (request is null || request.UpToMessageId <= 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: "A message id is required.");
        }

        var marker = await chat.MarkReadAsync(userId, peerUserId, request.UpToMessageId, cancellationToken);
        if (marker is { } upTo)
        {
            await hub.Clients.User(userId).ConversationRead(new ChatReadDto(peerUserId, upTo));
            // Read ticks: the other person learns how far this user has read.
            await hub.Clients.User(peerUserId).PeerRead(new ChatReadDto(userId, upTo));
        }

        return Results.NoContent();
    }

    // ---------------- Groups

    private static async Task<IResult> ListGroupsAsync(HttpContext httpContext, ChatGroupService groups, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        SetNoStoreHeaders(httpContext.Response);
        return Results.Ok(await groups.ListGroupsAsync(userId, cancellationToken));
    }

    private static async Task<IResult> GetGroupAsync(long groupId, HttpContext httpContext, ChatGroupService groups, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        SetNoStoreHeaders(httpContext.Response);
        var group = await groups.GetGroupAsync(userId, groupId, cancellationToken);
        return group is null ? NotInGroup() : Results.Ok(group);
    }

    private static async Task<IResult> GetGroupHistoryAsync(
        long groupId,
        [FromQuery(Name = "before")] long? beforeMessageId,
        [FromQuery(Name = "after")] long? afterMessageId,
        [FromQuery(Name = "limit")] int? limit,
        HttpContext httpContext,
        ChatGroupService groups,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(httpContext.User, out var userId))
        {
            return Results.Unauthorized();
        }

        SetNoStoreHeaders(httpContext.Response);
        var history = await groups.GetHistoryAsync(userId, groupId, beforeMessageId, limit ?? 40, afterMessageId, cancellationToken);
        return history is null ? NotInGroup() : Results.Ok(history);
    }

    private static async Task<IResult> CreateGroupAsync(
        ChatGroupCreateRequest? request,
        HttpContext httpContext,
        ChatGroupService groups,
        IHubContext<ChatHub, IChatClient> hub,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var failure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (failure is not null || !TryGetUserId(httpContext.User, out var userId))
        {
            return failure ?? Results.Unauthorized();
        }

        var change = await groups.CreateAsync(userId, request?.Title, request?.MemberUserIds, cancellationToken);
        return await CompleteGroupChangeAsync(change, hub);
    }

    private static async Task<IResult> AddGroupMembersAsync(
        long groupId,
        ChatGroupMembersRequest? request,
        HttpContext httpContext,
        ChatGroupService groups,
        IHubContext<ChatHub, IChatClient> hub,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var failure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (failure is not null || !TryGetUserId(httpContext.User, out var userId))
        {
            return failure ?? Results.Unauthorized();
        }

        return await CompleteGroupChangeAsync(await groups.AddMembersAsync(userId, groupId, request?.UserIds, cancellationToken), hub);
    }

    private static async Task<IResult> RemoveGroupMemberAsync(
        long groupId,
        string memberUserId,
        HttpContext httpContext,
        ChatGroupService groups,
        IHubContext<ChatHub, IChatClient> hub,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var failure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (failure is not null || !TryGetUserId(httpContext.User, out var userId))
        {
            return failure ?? Results.Unauthorized();
        }

        return await CompleteGroupChangeAsync(await groups.RemoveMemberAsync(userId, groupId, memberUserId, cancellationToken), hub);
    }

    private static async Task<IResult> LeaveGroupAsync(
        long groupId,
        HttpContext httpContext,
        ChatGroupService groups,
        IHubContext<ChatHub, IChatClient> hub,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var failure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (failure is not null || !TryGetUserId(httpContext.User, out var userId))
        {
            return failure ?? Results.Unauthorized();
        }

        return await CompleteGroupChangeAsync(await groups.LeaveAsync(userId, groupId, cancellationToken), hub);
    }

    private static async Task<IResult> RenameGroupAsync(
        long groupId,
        ChatGroupRenameRequest? request,
        HttpContext httpContext,
        ChatGroupService groups,
        IHubContext<ChatHub, IChatClient> hub,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var failure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (failure is not null || !TryGetUserId(httpContext.User, out var userId))
        {
            return failure ?? Results.Unauthorized();
        }

        return await CompleteGroupChangeAsync(await groups.RenameAsync(userId, groupId, request?.Title, cancellationToken), hub);
    }

    private static async Task<IResult> SendToGroupAsync(
        long groupId,
        ChatGroupSendRequest? request,
        HttpContext httpContext,
        ChatGroupService groups,
        IHubContext<ChatHub, IChatClient> hub,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var failure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (failure is not null || !TryGetUserId(httpContext.User, out var userId))
        {
            return failure ?? Results.Unauthorized();
        }

        if (request is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Message not sent", detail: "The message was empty.");
        }

        var result = await groups.SendAsync(userId, groupId, request, cancellationToken);
        switch (result.Status)
        {
            case ChatSendStatus.Sent:
                // Pushed after the save; browsers de-duplicate by message id.
                await hub.Clients.Users(result.MemberUserIds).ReceiveGroupMessage(result.Message!);
                return Results.Ok(result.Message);
            case ChatSendStatus.RecipientUnavailable:
                // The sender is not (or no longer) in the group: 410 like every other group call,
                // so the browser closes the reply box instead of offering Retry.
                return NotInGroup();
            default:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Message not sent", detail: result.Error);
        }
    }

    private static async Task<IResult> MarkGroupReadAsync(
        long groupId,
        ChatMarkReadRequest? request,
        HttpContext httpContext,
        ChatGroupService groups,
        IHubContext<ChatHub, IChatClient> hub,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var failure = await ValidateAntiforgeryAsync(httpContext, antiforgery);
        if (failure is not null || !TryGetUserId(httpContext.User, out var userId))
        {
            return failure ?? Results.Unauthorized();
        }

        if (request is null || request.UpToMessageId <= 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: "A message id is required.");
        }

        var (marker, members) = await groups.MarkReadAsync(userId, groupId, request.UpToMessageId, cancellationToken);
        if (marker is { } upTo)
        {
            // Everyone in the group: the reader's other tabs clear their badge, the rest update "Seen by".
            await hub.Clients.Users(members).GroupRead(new ChatGroupReadDto(groupId, userId, upTo));
        }

        return Results.NoContent();
    }

    /// <summary>Pushes a group change: the new state and its notices to members, removal to those who left.</summary>
    private static async Task<IResult> CompleteGroupChangeAsync(ChatGroupChange change, IHubContext<ChatHub, IChatClient> hub)
    {
        switch (change.Status)
        {
            case ChatGroupStatus.Ok:
                if (change.NotifyUserIds.Count > 0)
                {
                    await hub.Clients.Users(change.NotifyUserIds).GroupChanged(change.Group!);
                    foreach (var notice in change.Events)
                    {
                        await hub.Clients.Users(change.NotifyUserIds).ReceiveGroupMessage(notice);
                    }
                }

                if (change.RemovedUserIds.Count > 0)
                {
                    await hub.Clients.Users(change.RemovedUserIds).GroupRemoved(change.Group!.Id);
                }

                return Results.Ok(change.Group);
            case ChatGroupStatus.NotFound:
                return NotInGroup();
            case ChatGroupStatus.Forbidden:
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not allowed", detail: change.Error);
            default:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Group not changed", detail: change.Error);
        }
    }

    /// <summary>
    /// Not 404: the browser reads 404 from /api/chat as "chat switched off". 410 says the group is
    /// gone for this user (left, removed, or never a member).
    /// </summary>
    private static IResult NotInGroup()
        => Results.Problem(statusCode: StatusCodes.Status410Gone, title: "Not in this group", detail: "You are not in this group.");

    private static async Task<IResult?> ValidateAntiforgeryAsync(HttpContext httpContext, IAntiforgery antiforgery)
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
                detail: "The request security token is missing or invalid. Refresh the page and retry.");
        }
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
