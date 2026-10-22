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
/// Live channel for chat. Anything that writes data (sending, marking read) goes through the
/// antiforgery-protected /api/chat endpoints, which then push here. Browsers may only invoke
/// the two ephemeral signals below, which write nothing: typing and away. (The auth cookie is
/// SameSite=Strict, so another site cannot open this connection as the user.)
/// </summary>
[Authorize]
public sealed class ChatHub : Hub<IChatClient>
{
    private const string PresenceItemKey = "chat.presence";
    private const string TypingItemPrefix = "chat.typing.";
    private static readonly TimeSpan TypingInterval = TimeSpan.FromSeconds(2);

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
        await BroadcastIfChanged(userId, _presence.Connect(userId, Context.ConnectionId));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        // Only connections that were counted in are counted out, so an aborted connection
        // cannot mark a user offline while their other tabs are still open.
        if (!string.IsNullOrWhiteSpace(userId) && Context.Items.ContainsKey(PresenceItemKey))
        {
            await BroadcastIfChanged(userId, _presence.Disconnect(userId, Context.ConnectionId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>This tab's idle state. The user shows as away only when every open tab is idle.</summary>
    public async Task SetAway(bool away)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId) || !Context.Items.ContainsKey(PresenceItemKey))
        {
            return;
        }

        await BroadcastIfChanged(userId, _presence.SetAway(userId, Context.ConnectionId, away));
    }

    /// <summary>
    /// "I am typing to <paramref name="peerUserId"/>". Forwarded to that person's tabs only, at
    /// most once per two seconds per connection and person; nothing is stored.
    /// </summary>
    public async Task Typing(string peerUserId)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(peerUserId)
            || peerUserId.Length > 450
            || string.Equals(peerUserId, userId, StringComparison.Ordinal)
            || !Context.Items.ContainsKey(PresenceItemKey))
        {
            return;
        }

        var key = TypingItemPrefix + peerUserId;
        var now = DateTimeOffset.UtcNow;
        if (Context.Items.TryGetValue(key, out var last) && last is DateTimeOffset previous && now - previous < TypingInterval)
        {
            return;
        }

        Context.Items[key] = now;
        await Clients.User(peerUserId).Typing(new ChatTypingDto(userId));
    }

    private Task BroadcastIfChanged(string userId, (ChatPresence Before, ChatPresence After) change)
    {
        if (change.Before == change.After)
        {
            return Task.CompletedTask;
        }

        return Clients.Others.PresenceChanged(new ChatPresenceDto(
            userId,
            IsOnline: change.After != ChatPresence.Offline,
            IsAway: change.After == ChatPresence.Away));
    }
}

public interface IChatClient
{
    Task ReceiveMessage(ChatMessageDto message);

    Task ConversationRead(ChatReadDto read);

    Task PeerRead(ChatReadDto read);

    Task PresenceChanged(ChatPresenceDto presence);

    Task Typing(ChatTypingDto typing);
}
