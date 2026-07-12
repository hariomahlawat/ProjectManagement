using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class WorkspaceUpcomingEventQueryTests
{
    [Fact]
    public async Task LoadAsync_ReturnsSortedBoundedEventsAndCelebrations()
    {
        await using var db = CreateContext();
        var now = new DateTime(2026, 7, 2, 4, 0, 0, DateTimeKind.Utc);

        db.Users.Add(new ApplicationUser
        {
            Id = "po-1",
            UserName = "po-1",
            FullName = "Project Officer",
            ShowCelebrationsInCalendar = true
        });
        db.Events.AddRange(
            NewEvent("Later event", now.AddDays(4), now.AddDays(4).AddHours(1)),
            NewEvent("Tomorrow event", now.AddDays(1), now.AddDays(1).AddHours(1)));
        db.Celebrations.Add(new Celebration
        {
            Id = Guid.NewGuid(),
            EventType = CelebrationType.Birthday,
            Name = "Test Officer",
            Day = 3,
            Month = 7,
            CreatedById = "seed"
        });
        await db.SaveChangesAsync();

        var result = await WorkspaceUpcomingEventQuery.LoadAsync(
            db,
            "po-1",
            now,
            default,
            windowDays: 14,
            maxItems: 2);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].StartUtc <= result[1].StartUtc);
        Assert.Contains(result, item => item.IsCelebration && item.CategoryLabel == "Birthday");
        Assert.All(result, item => Assert.Equal("/Calendar", item.OpenUrl));
    }

    [Fact]
    public async Task LoadAsync_RespectsUserCelebrationPreference()
    {
        await using var db = CreateContext();
        var now = new DateTime(2026, 7, 2, 4, 0, 0, DateTimeKind.Utc);

        db.Users.Add(new ApplicationUser
        {
            Id = "po-2",
            UserName = "po-2",
            FullName = "Project Officer",
            ShowCelebrationsInCalendar = false
        });
        db.Events.Add(NewEvent("Conference", now.AddDays(1), now.AddDays(1).AddHours(2)));
        db.Celebrations.Add(new Celebration
        {
            Id = Guid.NewGuid(),
            EventType = CelebrationType.Anniversary,
            Name = "Officer",
            SpouseName = "Spouse",
            Day = 3,
            Month = 7,
            CreatedById = "seed"
        });
        await db.SaveChangesAsync();

        var result = await WorkspaceUpcomingEventQuery.LoadAsync(
            db,
            "po-2",
            now,
            default);

        Assert.Single(result);
        Assert.False(result[0].IsCelebration);
        Assert.Equal("Conference", result[0].Title);
    }

    [Fact]
    public async Task LoadAsync_ExcludesPastAndOutOfWindowEvents()
    {
        await using var db = CreateContext();
        var now = new DateTime(2026, 7, 2, 4, 0, 0, DateTimeKind.Utc);

        db.Users.Add(new ApplicationUser
        {
            Id = "po-3",
            UserName = "po-3",
            FullName = "Project Officer"
        });
        db.Events.AddRange(
            NewEvent("Past", now.AddDays(-2), now.AddDays(-2).AddHours(1)),
            NewEvent("In window", now.AddDays(5), now.AddDays(5).AddHours(1)),
            NewEvent("Too late", now.AddDays(20), now.AddDays(20).AddHours(1)));
        await db.SaveChangesAsync();

        var result = await WorkspaceUpcomingEventQuery.LoadAsync(
            db,
            "po-3",
            now,
            default,
            windowDays: 14);

        var item = Assert.Single(result);
        Assert.Equal("In window", item.Title);
    }

    private static Event NewEvent(string title, DateTime startUtc, DateTime endUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Category = EventCategory.Conference,
            StartUtc = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)),
            EndUtc = new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
