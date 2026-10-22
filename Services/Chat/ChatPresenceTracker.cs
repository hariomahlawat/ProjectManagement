using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ProjectManagement.Services.Chat;

/// <summary>
/// In-memory online status: a user is online while at least one browser tab holds a chat
/// connection. Held per process, which matches the single-server LAN deployment; running
/// several app servers would need a shared store (and a SignalR backplane) instead.
/// </summary>
public sealed class ChatPresenceTracker
{
    private readonly ConcurrentDictionary<string, int> _connections = new(System.StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <returns>True when this is the user's first open connection (they just came online).</returns>
    public bool Connect(string userId)
    {
        lock (_gate)
        {
            var count = _connections.AddOrUpdate(userId, 1, (_, current) => current + 1);
            return count == 1;
        }
    }

    /// <returns>True when this was the user's last open connection (they just went offline).</returns>
    public bool Disconnect(string userId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(userId, out var current))
            {
                return false;
            }

            if (current <= 1)
            {
                _connections.TryRemove(userId, out _);
                return true;
            }

            _connections[userId] = current - 1;
            return false;
        }
    }

    public bool IsOnline(string userId) => _connections.ContainsKey(userId);

    public IReadOnlySet<string> OnlineUserIds() => new HashSet<string>(_connections.Keys, System.StringComparer.Ordinal);
}
