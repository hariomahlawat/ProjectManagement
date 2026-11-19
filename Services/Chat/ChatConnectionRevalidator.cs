using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Chat;

/// <summary>
/// Closes open chat connections whose account may no longer use PRISM: deleted, disabled,
/// pending deletion, or whose security stamp changed (disabling, a password reset, a role
/// change) since the connection opened, which is the rule the sign-in cookie is re-checked by.
/// Without it, a person disabled with a chat tab open kept receiving every group message.
/// A closed tab reconnects on its own: with a valid cookie it is back at once (for example
/// after the person changed their own password); otherwise the sign-in is refused.
/// </summary>
public sealed class ChatConnectionRevalidator
{
    private const int Batch = 500;

    private readonly ApplicationDbContext _db;
    private readonly ChatConnectionRegistry _registry;
    private readonly ILogger<ChatConnectionRevalidator> _logger;

    public ChatConnectionRevalidator(ApplicationDbContext db, ChatConnectionRegistry registry, ILogger<ChatConnectionRevalidator> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <returns>How many connections were closed.</returns>
    public async Task<int> RevalidateAsync(CancellationToken cancellationToken = default)
    {
        var connections = _registry.Snapshot();
        if (connections.Count == 0)
        {
            return 0;
        }

        var userIds = connections.Select(c => c.UserId).Distinct(StringComparer.Ordinal).ToList();
        var accounts = new Dictionary<string, (string? Stamp, bool Usable)>(StringComparer.Ordinal);
        for (var start = 0; start < userIds.Count; start += Batch)
        {
            var slice = userIds.Skip(start).Take(Batch).ToList();
            var rows = await _db.Users
                .AsNoTracking()
                .Where(u => slice.Contains(u.Id))
                .Select(u => new { u.Id, u.SecurityStamp, u.IsDisabled, u.PendingDeletion })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                accounts[row.Id] = (row.SecurityStamp, !row.IsDisabled && !row.PendingDeletion);
            }
        }

        var closed = 0;
        foreach (var connection in connections)
        {
            var keep = accounts.TryGetValue(connection.UserId, out var account)
                && account.Usable
                && (connection.SecurityStamp is null || string.Equals(connection.SecurityStamp, account.Stamp, StringComparison.Ordinal));
            if (keep)
            {
                continue;
            }

            _registry.Remove(connection.Context.ConnectionId);
            connection.Context.Abort();
            closed++;
        }

        if (closed > 0)
        {
            _logger.LogInformation("Closed {Count} chat connection(s) of accounts that were disabled, deleted or signed out since they connected.", closed);
        }

        return closed;
    }
}
