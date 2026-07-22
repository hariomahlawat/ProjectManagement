using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminTimeServiceTests
{
    [Fact]
    public void IstDayBoundaries_AreConvertedToUtcExactly()
    {
        var service = new AdminTimeService(new FixedClock(new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero)));
        var date = new DateOnly(2026, 7, 12);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 11, 18, 30, 0, TimeSpan.Zero),
            service.StartOfIstDayUtc(date));
        Assert.Equal(
            new DateTimeOffset(2026, 7, 12, 18, 30, 0, TimeSpan.Zero),
            service.EndExclusiveOfIstDayUtc(date));
    }

    [Fact]
    public void FormatIst_ConvertsOnlyAtPresentationBoundary()
    {
        var service = new AdminTimeService(new FixedClock(DateTimeOffset.UnixEpoch));

        Assert.Equal(
            "12 Jul 2026, 11:00 IST",
            service.FormatIst(new DateTimeOffset(2026, 7, 12, 5, 30, 0, TimeSpan.Zero)));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
