using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;

namespace ProjectManagement.Services.Scheduling;

public sealed record OfficeCalendarHolidayEntry(
    int Id,
    string Name,
    HolidayType Type,
    bool IsObservedAsOfficeHoliday,
    string? AuthorityReference,
    string? ObservanceRemarks)
{
    public bool AffectsSchedule =>
        Type == HolidayType.Gazetted || IsObservedAsOfficeHoliday;
}

public sealed record OfficeCalendarDay(
    DateOnly Date,
    bool IsOfficeClosed,
    string? ClosureType,
    IReadOnlyList<OfficeCalendarHolidayEntry> Entries);

public interface IOfficeCalendarService
{
    Task<IReadOnlySet<DateOnly>> GetNonWorkingDatesAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfficeCalendarDay>> GetCalendarDaysAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken cancellationToken = default);

    Task<bool> IsOfficeHolidayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authoritative office-holiday query used by project planning, calendar presentation and
/// ERP-usage working-day calculations. Informational Restricted Holidays are returned for
/// display but are excluded from non-working-date results.
/// </summary>
public sealed class OfficeCalendarService : IOfficeCalendarService
{
    private readonly ApplicationDbContext _db;

    public OfficeCalendarService(ApplicationDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlySet<DateOnly>> GetNonWorkingDatesAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startInclusive, endExclusive);

        var dates = await _db.Holidays
            .AsNoTracking()
            .Where(holiday =>
                holiday.Date >= startInclusive
                && holiday.Date < endExclusive
                && (holiday.Type == HolidayType.Gazetted || holiday.IsObservedAsOfficeHoliday))
            .Select(holiday => holiday.Date)
            .Distinct()
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }

    public async Task<IReadOnlyList<OfficeCalendarDay>> GetCalendarDaysAsync(
        DateOnly startInclusive,
        DateOnly endExclusive,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(startInclusive, endExclusive);

        var rows = await _db.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.Date >= startInclusive && holiday.Date < endExclusive)
            .OrderBy(holiday => holiday.Date)
            .ThenBy(holiday => holiday.Type)
            .ThenBy(holiday => holiday.Name)
            .Select(holiday => new
            {
                holiday.Id,
                holiday.Date,
                holiday.Name,
                holiday.Type,
                holiday.IsObservedAsOfficeHoliday,
                holiday.AuthorityReference,
                holiday.ObservanceRemarks
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Date)
            .Select(group =>
            {
                var entries = group
                    .Select(row => new OfficeCalendarHolidayEntry(
                        row.Id,
                        row.Name,
                        row.Type,
                        row.IsObservedAsOfficeHoliday,
                        row.AuthorityReference,
                        row.ObservanceRemarks))
                    .ToList();

                var hasGazetted = entries.Any(entry => entry.Type == HolidayType.Gazetted);
                var hasObservedRestricted = entries.Any(entry =>
                    entry.Type == HolidayType.Restricted && entry.IsObservedAsOfficeHoliday);

                return new OfficeCalendarDay(
                    group.Key,
                    hasGazetted || hasObservedRestricted,
                    hasGazetted
                        ? "Gazetted"
                        : hasObservedRestricted
                            ? "RestrictedObserved"
                            : null,
                    entries);
            })
            .OrderBy(day => day.Date)
            .ToList();
    }

    public Task<bool> IsOfficeHolidayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        _db.Holidays.AsNoTracking().AnyAsync(
            holiday =>
                holiday.Date == date
                && (holiday.Type == HolidayType.Gazetted || holiday.IsObservedAsOfficeHoliday),
            cancellationToken);

    private static void ValidateRange(DateOnly startInclusive, DateOnly endExclusive)
    {
        if (endExclusive <= startInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endExclusive),
                "The office-calendar end date must be after the start date.");
        }
    }
}
