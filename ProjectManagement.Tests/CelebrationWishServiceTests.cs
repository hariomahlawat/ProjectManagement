using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Celebrations;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>The birthday or anniversary wish for the signed-in person (docs/ux/celebration-wish.md).</summary>
public sealed class CelebrationWishServiceTests
{
    // 13 November 2026, 11:30 IST.
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ABirthdayLinkedToTheAccount_IsWishedOnTheDay_WithoutTheAge()
    {
        await using var db = CreateDb();
        AddUser(db, "u1", "Col", "R Sharma", new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc));
        Add(db, CelebrationType.Birthday, "u1", 13, 11, year: 1975);
        await db.SaveChangesAsync();

        var wish = await Create(db).GetTodayAsync("u1");

        Assert.NotNull(wish);
        Assert.Equal(CelebrationType.Birthday, wish!.Kind);
        Assert.Equal("Happy Birthday, Col R Sharma", wish.Greeting);
        Assert.DoesNotContain("51", wish.Line + wish.Note);
        Assert.Equal(new DateOnly(2026, 11, 13), wish.Date);
        Assert.NotEqual("0", wish.SignInStamp);
    }

    [Fact]
    public async Task AnAnniversary_NamesTheCoupleAndTheYears()
    {
        await using var db = CreateDb();
        AddUser(db, "u1", "Col", "R Sharma");
        Add(db, CelebrationType.Anniversary, "u1", 13, 11, year: 2001, spouse: "Anita");
        await db.SaveChangesAsync();

        var wish = await Create(db).GetTodayAsync("u1");

        Assert.Equal("Happy 25th Anniversary, Col R Sharma & Anita", wish!.Greeting);
        Assert.Equal("Wishing you both many more happy years.", wish.Note);
    }

    [Fact]
    public async Task NoWish_OnAnotherDay_ForAnotherAccount_OrForADeletedOrUnlinkedEntry()
    {
        await using var db = CreateDb();
        AddUser(db, "u1", "Col", "R Sharma");
        AddUser(db, "u2", "Maj", "S Verma");
        Add(db, CelebrationType.Birthday, "u1", 14, 11);
        Add(db, CelebrationType.Birthday, null, 13, 11);
        Add(db, CelebrationType.Anniversary, "u1", 13, 11).DeletedUtc = Now;
        await db.SaveChangesAsync();
        var service = Create(db);

        Assert.Null(await service.GetTodayAsync("u1"));
        Assert.Null(await service.GetTodayAsync("u2"));
        Assert.Null(await service.GetTodayAsync(null));
        Assert.Null(await service.GetTodayAsync(" "));
    }

    [Fact]
    public async Task TheDayIsTheIstDay()
    {
        await using var db = CreateDb();
        AddUser(db, "u1", "Col", "R Sharma");
        Add(db, CelebrationType.Birthday, "u1", 14, 11);
        await db.SaveChangesAsync();

        // 19:00 UTC on the 13th is 00:30 IST on the 14th.
        var lateEvening = new CelebrationWishService(
            db, FakeClock.AtUtc(new DateTimeOffset(2026, 11, 13, 19, 0, 0, TimeSpan.Zero)), NullLogger<CelebrationWishService>.Instance);

        Assert.Null(await Create(db).GetTodayAsync("u1"));
        Assert.NotNull(await lateEvening.GetTodayAsync("u1"));
    }

    [Fact]
    public void ABirthdayComesBeforeAnAnniversaryOnTheSameDay_AndLeapDayFallsOn28FebruaryInOtherYears()
    {
        var birthday = new Celebration { EventType = CelebrationType.Birthday, Day = 13, Month = 11 };
        var anniversary = new Celebration { EventType = CelebrationType.Anniversary, Day = 13, Month = 11 };
        var leap = new Celebration { EventType = CelebrationType.Birthday, Day = 29, Month = 2 };

        Assert.Same(birthday, CelebrationWishService.PickForToday(new[] { anniversary, birthday }, new DateOnly(2026, 11, 13)));
        Assert.Same(leap, CelebrationWishService.PickForToday(new[] { leap }, new DateOnly(2027, 2, 28)));
        Assert.Same(leap, CelebrationWishService.PickForToday(new[] { leap }, new DateOnly(2028, 2, 29)));
        Assert.Null(CelebrationWishService.PickForToday(new[] { leap }, new DateOnly(2028, 2, 28)));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(25, "25th")]
    [InlineData(112, "112th")]
    public void Ordinal_ReadsAsEnglish(int number, string expected) =>
        Assert.Equal(expected, CelebrationWishService.Ordinal(number));

    [Fact]
    public void AnAnniversaryWithoutAYearOrSpouse_StaysSimple()
    {
        var wish = CelebrationWishService.Build(
            new Celebration { EventType = CelebrationType.Anniversary, Day = 13, Month = 11 },
            new DateOnly(2026, 11, 13), "u1", "Col R Sharma", "0");

        Assert.Equal("Happy Anniversary, Col R Sharma", wish.Greeting);
        Assert.Equal("Wishing you many more happy years.", wish.Note);
    }

    // The wish is served by PRISM itself and sits in the shared layout for signed-in people only.
    [Fact]
    public void TheLayoutRendersTheWish_AndItsAssetsAreLocal()
    {
        var root = FindRepositoryRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Pages", "Shared", "_Layout.cshtml"));
        var view = File.ReadAllText(Path.Combine(root, "Pages", "Shared", "Components", "CelebrationWish", "Default.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "components", "prism-wish.js"));

        Assert.Contains("Component.InvokeAsync(\"CelebrationWish\")", layout, StringComparison.Ordinal);
        Assert.Contains("~/js/components/prism-wish.js", view, StringComparison.Ordinal);
        Assert.Contains("~/css/components/prism-wish.css", view, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", view + script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", view + script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("document.fullscreenElement", script, StringComparison.Ordinal);
    }

    private static Celebration Add(ApplicationDbContext db, CelebrationType type, string? userId, byte day, byte month, short? year = null, string? spouse = null)
    {
        var item = new Celebration
        {
            Id = Guid.NewGuid(),
            EventType = type,
            Name = "As written on the calendar",
            SpouseName = spouse,
            Day = day,
            Month = month,
            Year = year,
            UserId = userId,
            CreatedById = "admin"
        };
        db.Celebrations.Add(item);
        return item;
    }

    private static void AddUser(ApplicationDbContext db, string id, string rank, string fullName, DateTime? lastLoginUtc = null) =>
        db.Users.Add(new ApplicationUser { Id = id, UserName = id, Rank = rank, FullName = fullName, LastLoginUtc = lastLoginUtc });

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CelebrationWishService Create(ApplicationDbContext db) =>
        new(db, FakeClock.AtUtc(Now), NullLogger<CelebrationWishService>.Instance);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
