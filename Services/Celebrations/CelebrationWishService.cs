using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin.MasterData;

namespace ProjectManagement.Services.Celebrations;

/// <summary>
/// Today's wish for the signed-in person: "Happy Birthday," + "Col R Sharma". <see cref="Line"/>
/// has no trailing comma; <see cref="SignInStamp"/> changes at every sign-in so the page can
/// play the wish again after one.
/// </summary>
public sealed record CelebrationWish(
    CelebrationType Kind,
    string Line,
    string Name,
    string Note,
    DateOnly Date,
    string UserId,
    string SignInStamp)
{
    /// <summary>"Happy Birthday, Col R Sharma": the dashboard greeting on the day.</summary>
    public string Greeting => $"{Line}, {Name}";
}

public interface ICelebrationWishService
{
    /// <summary>
    /// The wish for this account today (IST), or null on every other day and for everyone whose
    /// birthday or anniversary is not linked to their account. Never throws: a wish is a nicety
    /// and must not break the page it sits on.
    /// </summary>
    Task<CelebrationWish?> GetTodayAsync(string? userId, CancellationToken cancellationToken = default);
}

public sealed class CelebrationWishService : ICelebrationWishService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<CelebrationWishService> _logger;

    public CelebrationWishService(ApplicationDbContext db, IClock clock, ILogger<CelebrationWishService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CelebrationWish?> GetTodayAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        try
        {
            // At most two rows: one birthday and one anniversary per account.
            var linked = await _db.Celebrations.AsNoTracking()
                .Where(item => item.DeletedUtc == null && item.UserId == userId)
                .ToListAsync(cancellationToken);
            if (linked.Count == 0) return null;

            var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow.UtcDateTime));
            var celebration = PickForToday(linked, today);
            if (celebration is null) return null;

            var user = await _db.Users.AsNoTracking()
                .Where(item => item.Id == userId)
                .Select(item => new { item.Rank, item.FullName, item.UserName, item.LastLoginUtc })
                .SingleOrDefaultAsync(cancellationToken);
            if (user is null) return null;

            return Build(
                celebration,
                today,
                userId,
                CelebrationUserMatcher.Label(user.Rank, user.FullName, user.UserName),
                user.LastLoginUtc?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Today's celebration wish could not be read for {UserId}.", userId);
            return null;
        }
    }

    /// <summary>The celebration that falls today; a birthday comes before an anniversary on the same day.</summary>
    public static Celebration? PickForToday(IEnumerable<Celebration> linked, DateOnly today) =>
        linked
            .Where(item => item.DeletedUtc == null && CelebrationHelpers.NextOccurrenceLocal(item, today) == today)
            .OrderBy(item => item.EventType)
            .FirstOrDefault();

    public static CelebrationWish Build(Celebration celebration, DateOnly today, string userId, string userLabel, string signInStamp)
    {
        if (celebration.EventType == CelebrationType.Anniversary)
        {
            var years = celebration.Year.HasValue ? today.Year - celebration.Year.Value : 0;
            var spouse = string.IsNullOrWhiteSpace(celebration.SpouseName) ? null : celebration.SpouseName.Trim();
            return new CelebrationWish(
                CelebrationType.Anniversary,
                years >= 1 ? $"Happy {Ordinal(years)} Anniversary" : "Happy Anniversary",
                spouse is null ? userLabel : $"{userLabel} & {spouse}",
                spouse is null ? "Wishing you many more happy years." : "Wishing you both many more happy years.",
                today,
                userId,
                signInStamp);
        }

        // A birthday never states the age, even when the year is on record.
        return new CelebrationWish(
            CelebrationType.Birthday,
            "Happy Birthday",
            userLabel,
            "Wishing you a wonderful year ahead.",
            today,
            userId,
            signInStamp);
    }

    public static string Ordinal(int number)
    {
        var lastTwo = number % 100;
        var suffix = lastTwo is >= 11 and <= 13
            ? "th"
            : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{number}{suffix}";
    }
}
