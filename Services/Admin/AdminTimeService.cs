using System.Globalization;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Admin;

public interface IAdminTimeService
{
    DateTimeOffset UtcNow { get; }
    DateOnly TodayIst { get; }
    DateTimeOffset StartOfIstDayUtc(DateOnly date);
    DateTimeOffset EndExclusiveOfIstDayUtc(DateOnly date);
    DateTime ToIst(DateTime utc);
    DateTimeOffset ToIst(DateTimeOffset utc);
    string FormatIst(DateTime? utc, string fallback = "—");
    string FormatIst(DateTimeOffset? utc, string fallback = "—");
}

public sealed class AdminTimeService : IAdminTimeService
{
    private readonly IClock _clock;

    public AdminTimeService(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public DateTimeOffset UtcNow => _clock.UtcNow;

    public DateOnly TodayIst => DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);

    public DateTimeOffset StartOfIstDayUtc(DateOnly date) => IstClock.StartOfDayIstToUtc(date);

    public DateTimeOffset EndExclusiveOfIstDayUtc(DateOnly date) => IstClock.ExclusiveEndOfDayIstToUtc(date);

    public DateTime ToIst(DateTime utc) => IstClock.ToIst(utc);

    public DateTimeOffset ToIst(DateTimeOffset utc) => IstClock.ToIst(utc);

    public string FormatIst(DateTime? utc, string fallback = "—") =>
        utc.HasValue
            ? ToIst(utc.Value).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " IST"
            : fallback;

    public string FormatIst(DateTimeOffset? utc, string fallback = "—") =>
        utc.HasValue
            ? ToIst(utc.Value).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " IST"
            : fallback;
}
