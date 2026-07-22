using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Services.Scheduling;

namespace ProjectManagement.Tests.Scheduling;

public sealed class OfficeCalendarServiceTests
{
    [Fact]
    public async Task Calendar_ReturnsEveryEntry_ButOnlyClosuresAsNonWorkingDates()
    {
        await using var db = CreateContext();
        db.Holidays.AddRange(
            new Holiday
            {
                Id = 1,
                Date = new DateOnly(2026, 8, 15),
                Name = "Independence Day",
                Type = HolidayType.Gazetted,
                IsObservedAsOfficeHoliday = true
            },
            new Holiday
            {
                Id = 2,
                Date = new DateOnly(2026, 8, 20),
                Name = "Informational RH",
                Type = HolidayType.Restricted,
                IsObservedAsOfficeHoliday = false
            },
            new Holiday
            {
                Id = 3,
                Date = new DateOnly(2026, 8, 26),
                Name = "Observed RH",
                Type = HolidayType.Restricted,
                IsObservedAsOfficeHoliday = true
            });
        await db.SaveChangesAsync();

        var service = new OfficeCalendarService(db);
        var days = await service.GetCalendarDaysAsync(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 1));
        var nonWorking = await service.GetNonWorkingDatesAsync(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 1));

        Assert.Equal(3, days.Count);
        Assert.True(days.Single(day => day.Date.Day == 15).IsOfficeClosed);
        Assert.False(days.Single(day => day.Date.Day == 20).IsOfficeClosed);
        Assert.Equal("RestrictedObserved", days.Single(day => day.Date.Day == 26).ClosureType);
        Assert.Contains(new DateOnly(2026, 8, 15), nonWorking);
        Assert.Contains(new DateOnly(2026, 8, 26), nonWorking);
        Assert.DoesNotContain(new DateOnly(2026, 8, 20), nonWorking);
    }

    [Fact]
    public async Task Calendar_GroupsMultipleEntriesOnOneDate_AndGazettedTakesClosurePrecedence()
    {
        await using var db = CreateContext();
        db.Holidays.AddRange(
            new Holiday
            {
                Id = 1,
                Date = new DateOnly(2026, 1, 26),
                Name = "Republic Day",
                Type = HolidayType.Gazetted,
                IsObservedAsOfficeHoliday = true
            },
            new Holiday
            {
                Id = 2,
                Date = new DateOnly(2026, 1, 26),
                Name = "Additional RH",
                Type = HolidayType.Restricted,
                IsObservedAsOfficeHoliday = false
            });
        await db.SaveChangesAsync();

        var service = new OfficeCalendarService(db);
        var day = Assert.Single(await service.GetCalendarDaysAsync(
            new DateOnly(2026, 1, 26),
            new DateOnly(2026, 1, 27)));

        Assert.True(day.IsOfficeClosed);
        Assert.Equal("Gazetted", day.ClosureType);
        Assert.Equal(2, day.Entries.Count);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
