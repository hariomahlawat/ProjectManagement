using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Services.Chat;

/// <summary>
/// The chat connections open in this process, with the account and security stamp each was
/// opened with. A SignalR connection is authenticated once, when it opens, and the cookie's
/// periodic re-check (<see cref="Infrastructure.SessionRevalidation"/>) never reaches it, so
/// <see cref="ChatConnectionRevalidator"/> uses this list to close the connections of accounts
/// that were disabled, deleted or had their stamp rotated since. Per process, like
/// <see cref="ChatPresenceTracker"/>.
/// </summary>
public sealed class ChatConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ChatConnection> _connections = new(StringComparer.Ordinal);
    private readonly string _stampClaimType;

    public ChatConnectionRegistry(IOptions<IdentityOptions> identity)
    {
        _stampClaimType = identity?.Value.ClaimsIdentity.SecurityStampClaimType ?? new ClaimsIdentityOptions().SecurityStampClaimType;
    }

    public void Add(string userId, HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stamp = context.User?.FindFirst(_stampClaimType)?.Value;
        _connections[context.ConnectionId] = new ChatConnection(userId, stamp, context);
    }

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlyList<ChatConnection> Snapshot() => _connections.Values.ToList();

    public int Count => _connections.Count;
}

/// <param name="SecurityStamp">The stamp in the sign-in cookie the connection was opened with (null when the cookie carries none).</param>
public sealed record ChatConnection(string UserId, string? SecurityStamp, HubCallerContext Context);
