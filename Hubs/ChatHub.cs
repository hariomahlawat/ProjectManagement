using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Services.Chat;

namespace ProjectManagement.Hubs;

/// <summary>
/// Push-only channel for chat. Browsers never send messages through the hub: sending and
/// marking read go through the antiforgery-protected /api/chat endpoints, which then push
/// here. The hub itself only tracks who is online.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub<IChatClient>
{
    private const string PresenceItemKey = "chat.presence";

    private readonly ChatPresenceTracker _presence;
    private readonly IOptionsMonitor<ChatOptions> _options;

    public ChatHub(ChatPresenceTracker presence, IOptionsMonitor<ChatOptions> options)
    {
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!_options.CurrentValue.Enabled || string.IsNullOrWhiteSpace(userId))
        {
            Context.Abort();
            return;
        }

        Context.Items[PresenceItemKey] = true;
        if (_presence.Connect(userId))
        {
            await Clients.Others.PresenceChanged(new ChatPresenceDto(userId, true));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        // Only connections that were counted in are counted out, so an aborted connection
        // cannot mark a user offline while their other tabs are still open.
        if (!string.IsNullOrWhiteSpace(userId)
            && Context.Items.ContainsKey(PresenceItemKey)
            && _presence.Disconnect(userId))
        {
            await Clients.Others.PresenceChanged(new ChatPresenceDto(userId, false));
        }

        await base.OnDisconnectedAsync(exception);
    }
}

public interface IChatClient
{
    Task ReceiveMessage(ChatMessageDto message);

    Task ConversationRead(ChatReadDto read);

    Task PresenceChanged(ChatPresenceDto presence);
}
