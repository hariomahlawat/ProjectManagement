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

public record CalendarHolidayEntryVm(
    int Id,
    string Name,
    string Type,
    bool IsObservedAsOfficeHoliday,
    bool AffectsSchedule,
    string? AuthorityReference);

public record CalendarHolidayDayVm(
    string Date,
    bool IsOfficeClosed,
    string? ClosureType,
    IReadOnlyList<CalendarHolidayEntryVm> Entries);

public record ErpUsageHeartbeatRequest(string ModuleKey);

public record TrashProjectRequest(string Reason);

public record PurgeProjectRequest(bool RemoveAssets);

public partial class Program { }
