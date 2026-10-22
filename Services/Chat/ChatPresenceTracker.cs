using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Chat;

public enum ChatPresence
{
    Offline,
    Away,
    Online,
}

/// <summary>
/// In-memory online status, per browser tab (SignalR connection). A user is online while any
/// tab is active, away while every open tab reports the person idle, and offline with no tab
/// open. Held per process, which matches the single-server LAN deployment; running several app
/// servers would need a shared store (and a SignalR backplane) instead.
/// </summary>
public sealed class ChatPresenceTracker
{
    // userId -> (connectionId -> away)
    private readonly Dictionary<string, Dictionary<string, bool>> _users = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <returns>The user's status before and after, so callers broadcast only real changes.</returns>
    public (ChatPresence Before, ChatPresence After) Connect(string userId, string connectionId)
        => Change(userId, connections => connections[connectionId] = false);

    public (ChatPresence Before, ChatPresence After) SetAway(string userId, string connectionId, bool away)
        => Change(userId, connections =>
        {
            // Only a connection that is still registered can report; a late call after
            // disconnect must not bring the user back.
            if (connections.ContainsKey(connectionId))
            {
                connections[connectionId] = away;
            }
        });

    public (ChatPresence Before, ChatPresence After) Disconnect(string userId, string connectionId)
        => Change(userId, connections => connections.Remove(connectionId));

    public ChatPresence StatusOf(string userId)
    {
        lock (_gate)
        {
            return Evaluate(_users.GetValueOrDefault(userId));
        }
    }

    public bool IsOnline(string userId) => StatusOf(userId) != ChatPresence.Offline;

    /// <summary>Everyone with an open tab, with their status.</summary>
    public IReadOnlyDictionary<string, ChatPresence> Snapshot()
    {
        lock (_gate)
        {
            return _users.ToDictionary(pair => pair.Key, pair => Evaluate(pair.Value), StringComparer.Ordinal);
        }
    }

    private (ChatPresence Before, ChatPresence After) Change(string userId, Action<Dictionary<string, bool>> mutate)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(userId, out var connections))
            {
                connections = new Dictionary<string, bool>(StringComparer.Ordinal);
                _users[userId] = connections;
            }

            var before = Evaluate(connections);
            mutate(connections);
            var after = Evaluate(connections);
            if (connections.Count == 0)
            {
                _users.Remove(userId);
            }

            return (before, after);
        }
    }

    private static ChatPresence Evaluate(Dictionary<string, bool>? connections)
    {
        if (connections is null || connections.Count == 0)
        {
            return ChatPresence.Offline;
        }

        return connections.Values.All(away => away) ? ChatPresence.Away : ChatPresence.Online;
    }
}
