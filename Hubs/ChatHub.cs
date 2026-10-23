using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string TypingItemKey = "chat.typing";
    private static readonly TimeSpan TypingInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Conversations one connection may signal typing to within <see cref="TypingInterval"/>. A real
    /// tab types into one or two; the cap keeps a misbehaving client from growing the connection's
    /// state (and, for groups, its membership lookups) without bound.
    /// </summary>
    internal const int MaxTypingTargets = 16;

    private readonly ChatPresenceTracker _presence;
    private readonly IOptionsMonitor<ChatOptions> _options;
    private readonly ChatGroupService _groups;

    public ChatHub(ChatPresenceTracker presence, IOptionsMonitor<ChatOptions> options, ChatGroupService groups)
    {
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _groups = groups ?? throw new ArgumentNullException(nameof(groups));
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

        if (!TryClaimTypingSignal(peerUserId))
        {
            return;
        }

        await Clients.User(peerUserId).Typing(new ChatTypingDto(userId));
    }

    /// <summary>
    /// "I am typing in group <paramref name="groupId"/>". Only a member may signal it, and it goes
    /// to the other members only; throttled like <see cref="Typing"/>; nothing is stored.
    /// </summary>
    public async Task TypingInGroup(long groupId)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId) || groupId <= 0 || !Context.Items.ContainsKey(PresenceItemKey))
        {
            return;
        }

        if (!TryClaimTypingSignal("g:" + groupId))
        {
            return;
        }

        var others = await _groups.OtherMembersAsync(userId, groupId, Context.ConnectionAborted);
        if (others is { Count: > 0 })
        {
            await Clients.Users(others).GroupTyping(new ChatGroupTypingDto(groupId, userId));
        }
    }

    /// <summary>
    /// True when this connection may signal typing to <paramref name="target"/> now: at most once per
    /// <see cref="TypingInterval"/> per target, and to at most <see cref="MaxTypingTargets"/> targets
    /// within that interval.
    /// </summary>
    private bool TryClaimTypingSignal(string target)
    {
        if (!Context.Items.TryGetValue(TypingItemKey, out var stored) || stored is not Dictionary<string, DateTimeOffset> signals)
        {
            signals = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            Context.Items[TypingItemKey] = signals;
        }

        var now = DateTimeOffset.UtcNow;
        if (signals.TryGetValue(target, out var previous) && now - previous < TypingInterval)
        {
            return false;
        }

        if (!signals.ContainsKey(target) && signals.Count >= MaxTypingTargets)
        {
            foreach (var stale in signals.Where(pair => now - pair.Value >= TypingInterval).Select(pair => pair.Key).ToList())
            {
                signals.Remove(stale);
            }

            if (signals.Count >= MaxTypingTargets)
            {
                return false;
            }
        }

        signals[target] = now;
        return true;
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

    Task MessageUnsent(ChatMessageDto message);

    Task StatusChanged(ChatStatusDto status);

    /// <summary>
    /// A group message or notice. Separate from <see cref="ReceiveMessage"/> on purpose: a page
    /// loaded before group chats existed treats every ReceiveMessage as a direct message from its
    /// sender, so during an overlapping deployment it must never receive group traffic there.
    /// </summary>
    Task ReceiveGroupMessage(ChatMessageDto message);

    /// <summary>A group message was unsent (separate from MessageUnsent for the same reason).</summary>
    Task GroupMessageUnsent(ChatMessageDto message);

    /// <summary>A group the user is in was created or changed (members, name, manager).</summary>
    Task GroupChanged(ChatGroupDto group);

    /// <summary>The user is no longer in this group (left, or was removed).</summary>
    Task GroupRemoved(long groupId);

    Task GroupRead(ChatGroupReadDto read);

    Task GroupTyping(ChatGroupTypingDto typing);
}
