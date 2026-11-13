using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services.Accounts;

/// <summary>One successful sign-in, as shown to the account's owner.</summary>
/// <param name="WhenUtc">When the sign-in happened.</param>
/// <param name="Ip">The address it came from, when one was recorded.</param>
/// <param name="Client">Browser and system in words, for example "Chrome 142 on Windows".</param>
public sealed record AccountSignIn(DateTimeOffset WhenUtc, string? Ip, string Client);

/// <summary>
/// A user's own recent successful sign-ins, for the My account page. Administrators have
/// the full sign-in register elsewhere (Admin, Sign-ins); this never returns another
/// account's events.
/// </summary>
public interface IAccountSignInHistoryService
{
    /// <summary>
    /// The latest successful sign-ins of <paramref name="userId"/>, newest first, within
    /// <see cref="AccountSignInHistoryService.LookbackDays"/> days.
    /// </summary>
    Task<IReadOnlyList<AccountSignIn>> GetRecentAsync(string userId, int count, CancellationToken cancellationToken);
}

public sealed class AccountSignInHistoryService : IAccountSignInHistoryService
{
    /// <summary>
    /// How far back the list looks. The window also bounds the read: AuthEvents is indexed
    /// by event and time, not by user, so an account with few sign-ins would otherwise make
    /// the query walk the whole table.
    /// </summary>
    public const int LookbackDays = 90;

    public const int MaximumCount = 20;

    private readonly ApplicationDbContext _db;
    private readonly IAdminClientDescriptorService _clients;
    private readonly TimeProvider _time;

    public AccountSignInHistoryService(
        ApplicationDbContext db,
        IAdminClientDescriptorService clients,
        TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<AccountSignIn>> GetRecentAsync(
        string userId,
        int count,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<AccountSignIn>();
        }

        var take = Math.Clamp(count, 1, MaximumCount);
        var since = _time.GetUtcNow().AddDays(-LookbackDays);

        var events = await _db.AuthEvents
            .AsNoTracking()
            .Where(e => e.Event == AuthenticationEventNames.LoginSucceeded
                && e.WhenUtc >= since
                && e.UserId == userId)
            .OrderByDescending(e => e.WhenUtc)
            .Take(take)
            .Select(e => new { e.WhenUtc, e.Ip, e.UserAgent })
            .ToListAsync(cancellationToken);

        return events
            .Select(e => new AccountSignIn(
                e.WhenUtc,
                string.IsNullOrWhiteSpace(e.Ip) ? null : e.Ip.Trim(),
                _clients.Describe(e.UserAgent).Summary))
            .ToArray();
    }
}
