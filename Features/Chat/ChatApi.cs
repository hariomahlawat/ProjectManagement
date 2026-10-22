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

    public static IEndpointRouteBuilder MapChatApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/chat")
            .RequireAuthorization()
            .AddEndpointFilter(async (context, next) =>
            {
                var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<ChatOptions>>();
                return options.CurrentValue.Enabled ? await next(context) : Results.NotFound();
            })
            .WithTags("Chat");

        group.MapGet("/contacts", GetContactsAsync);
        group.MapGet("/with/{peerUserId}/messages", GetHistoryAsync);
        group.MapGet("/search", SearchAsync);
        group.MapGet("/status", GetStatusAsync);
        group.MapPost("/status", SetStatusAsync);
        group.MapPost("/messages/{messageId:long}/unsend", UnsendAsync);
        group.MapPost("/messages", SendAsync).RequireRateLimiting(SendRateLimitPolicy);
        group.MapPost("/with/{peerUserId}/read", MarkReadAsync);

        return endpoints;
    }

    /// <summary>Per-user fixed window; anonymous requests never reach here (the group requires sign-in).</summary>
    public static RateLimitPartition<string> GetSendPartition(HttpContext context, ChatOptions options)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, options.SendPermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, options.SendWindowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    public static bool IsSendRequest(HttpContext context)
        => HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.Equals("/api/chat/messages", StringComparison.OrdinalIgnoreCase);

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
                await hub.Clients.Users(message.SenderUserId, message.RecipientUserId).MessageUnsent(message);
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
        return Results.Ok(await chat.SearchAsync(userId, query, limit ?? ChatService.MaxSearchResults, cancellationToken));
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
