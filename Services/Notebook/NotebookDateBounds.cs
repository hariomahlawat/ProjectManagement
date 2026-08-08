using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Notebook;

/// <summary>
/// Canonical local-day boundaries for Notebook reminder classification.
/// Persistence remains UTC; only day classification is performed in IST.
/// </summary>
public readonly record struct NotebookDateBounds(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

    public static NotebookDateBounds For(DateTimeOffset nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, Ist);
        var startLocal = localNow.Date;
        var endLocal = startLocal.AddDays(1);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified),
            Ist);

        var endUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified),
            Ist);

        return new NotebookDateBounds(
            new DateTimeOffset(startUtc, TimeSpan.Zero),
            new DateTimeOffset(endUtc, TimeSpan.Zero));
    }

    public bool IsToday(DateTimeOffset? reminderAtUtc)
        => reminderAtUtc >= StartUtc && reminderAtUtc < EndUtc;

    public bool IsOverdue(DateTimeOffset? reminderAtUtc)
        => reminderAtUtc < StartUtc;
}
