using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Admin;

public sealed record AdminLoginOverviewUser(
    string UserName,
    DateTimeOffset? LastLoginUtc,
    int LoginCount);

public sealed record AdminLoginOverviewSnapshot(
    int TotalUsers,
    int ActiveUsers,
    int RestrictedUsers,
    IReadOnlyList<(DateOnly Date, int Count)> LoginsPerDay,
    IReadOnlyList<AdminLoginOverviewUser> TopUsers)
{
    public int[] LoginCounts => LoginsPerDay.Select(item => item.Count).ToArray();
}

public interface IAdminLoginOverviewService
{
    Task<AdminLoginOverviewSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the aggregate login overview from the canonical authentication-event
/// stream and the shared administrative account-state resolver.
/// </summary>
public sealed class AdminLoginOverviewService : IAdminLoginOverviewService
{
    private const int OverviewDays = 30;
    private const int TopUserCount = 10;

    private readonly ApplicationDbContext _db;
    private readonly IAdminTimeService _time;
    private readonly IUserAccountStateResolver _states;

    public AdminLoginOverviewService(
        ApplicationDbContext db,
        IAdminTimeService time,
        IUserAccountStateResolver states)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _states = states ?? throw new ArgumentNullException(nameof(states));
    }

    public async Task<AdminLoginOverviewSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _time.UtcNow;
        var users = await _db.Users.AsNoTracking().ToListAsync(cancellationToken);
        var totalUsers = users.Count;
        var activeUsers = users.Count(user => _states.Resolve(user, nowUtc).CanSignIn);

        var lastIstDate = _time.TodayIst;
        var firstIstDate = lastIstDate.AddDays(-(OverviewDays - 1));
        var fromUtc = _time.StartOfIstDayUtc(firstIstDate);
        var toUtcExclusive = _time.EndExclusiveOfIstDayUtc(lastIstDate);

        var loginEvents = await _db.AuthEvents
            .AsNoTracking()
            .Where(authEvent =>
                authEvent.Event == AuthenticationEventNames.LoginSucceeded
                && authEvent.WhenUtc >= fromUtc
                && authEvent.WhenUtc < toUtcExclusive)
            .Select(authEvent => new { authEvent.UserId, authEvent.WhenUtc })
            .ToListAsync(cancellationToken);

        var countsByDate = loginEvents
            .GroupBy(login => DateOnly.FromDateTime(_time.ToIst(login.WhenUtc).DateTime))
            .ToDictionary(group => group.Key, group => group.Count());

        var perDay = new List<(DateOnly Date, int Count)>(OverviewDays);
        for (var index = 0; index < OverviewDays; index++)
        {
            var date = firstIstDate.AddDays(index);
            countsByDate.TryGetValue(date, out var count);
            perDay.Add((date, count));
        }

        var topStats = await _db.AuthEvents
            .AsNoTracking()
            .Where(authEvent => authEvent.Event == AuthenticationEventNames.LoginSucceeded)
            .GroupBy(authEvent => authEvent.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                LoginCount = group.Count(),
                LastLoginUtc = group.Max(authEvent => authEvent.WhenUtc)
            })
            .OrderByDescending(row => row.LoginCount)
            .ThenByDescending(row => row.LastLoginUtc)
            .Take(TopUserCount)
            .ToListAsync(cancellationToken);

        var topUserIds = topStats.Select(row => row.UserId).ToArray();
        var identityMap = topUserIds.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _db.Users
                .AsNoTracking()
                .Where(user => topUserIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    DisplayName = string.IsNullOrWhiteSpace(user.FullName)
                        ? (user.UserName ?? user.Email ?? "(deleted user)")
                        : user.FullName
                })
                .ToDictionaryAsync(
                    user => user.Id,
                    user => user.DisplayName,
                    StringComparer.Ordinal,
                    cancellationToken);

        var topUsers = topStats
            .Select(row => new AdminLoginOverviewUser(
                identityMap.TryGetValue(row.UserId, out var displayName)
                    ? displayName
                    : "(deleted user)",
                row.LastLoginUtc,
                row.LoginCount))
            .ToArray();

        return new AdminLoginOverviewSnapshot(
            totalUsers,
            activeUsers,
            totalUsers - activeUsers,
            perDay,
            topUsers);
    }
}
