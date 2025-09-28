using System;
using ProjectManagement.Services;

namespace ProjectManagement.Tests.Fakes;

public sealed class FakeClock : IClock
{
    private static readonly TimeZoneInfo IndiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private DateTimeOffset _utcNow;

    private FakeClock(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public DateTimeOffset UtcNow => _utcNow;

    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;

    public static FakeClock AtUtc(DateTimeOffset utcNow) => new(utcNow);

    public static FakeClock ForIst(DateTime localIst)
    {
        var unspecified = DateTime.SpecifyKind(localIst, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, IndiaTimeZone);
        return new FakeClock(new DateTimeOffset(utc, TimeSpan.Zero));
    }

    public static FakeClock ForIstDate(DateOnly date)
        => ForIst(date.ToDateTime(TimeOnly.MinValue));

    public static FakeClock ForIstDate(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
        => ForIst(new DateTime(year, month, day, hour, minute, second));
}
