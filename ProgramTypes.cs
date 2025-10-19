public record CalendarEventVm(
    string Id,
    Guid SeriesId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay,
    string Category,
    string? Location,
    bool IsRecurring,
    bool IsCelebration,
    Guid? CelebrationId,
    string? TaskUrl);

public record CalendarHolidayVm(
    string Date,
    string Name,
    bool? SkipWeekends,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

public record TrashProjectRequest(string Reason);

public record PurgeProjectRequest(bool RemoveAssets);

public partial class Program { }
