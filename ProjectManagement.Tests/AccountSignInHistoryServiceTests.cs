using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Accounts;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AccountSignInHistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReturnsOnlyTheUsersOwnSuccessfulSignIns_NewestFirst()
    {
        await using var db = CreateContext();
        db.AuthEvents.AddRange(
            SignIn("me", Now.AddDays(-3), "10.0.0.1"),
            SignIn("me", Now.AddHours(-1), "10.0.0.2"),
            SignIn("someone-else", Now.AddMinutes(-5), "10.0.0.9"),
            new AuthEvent { UserId = "me", WhenUtc = Now.AddMinutes(-2), Event = "SomethingElse", Ip = "10.0.0.7" });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecentAsync("me", 5, CancellationToken.None);

        Assert.Equal(new[] { "10.0.0.2", "10.0.0.1" }, result.Select(row => row.Ip).ToArray());
        Assert.Equal(Now.AddHours(-1), result[0].WhenUtc);
    }

    [Fact]
    public async Task LeavesOutSignInsOlderThanTheLookback_AndKeepsToTheCount()
    {
        await using var db = CreateContext();
        db.AuthEvents.Add(SignIn("me", Now.AddDays(-(AccountSignInHistoryService.LookbackDays + 1)), "10.0.0.50"));
        for (var i = 1; i <= 7; i++)
        {
            db.AuthEvents.Add(SignIn("me", Now.AddDays(-i), $"10.0.0.{i}"));
        }
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecentAsync("me", 5, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.DoesNotContain(result, row => row.Ip == "10.0.0.50");
        Assert.Equal("10.0.0.1", result[0].Ip);
    }

    [Fact]
    public async Task DescribesTheBrowserInWords_AndTreatsABlankAddressAsNotRecorded()
    {
        await using var db = CreateContext();
        var signIn = SignIn("me", Now.AddHours(-2), "  ");
        signIn.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";
        db.AuthEvents.Add(signIn);
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetRecentAsync("me", 5, CancellationToken.None);

        var row = Assert.Single(result);
        Assert.Null(row.Ip);
        Assert.Contains("Chrome 142", row.Client, StringComparison.Ordinal);
        Assert.Contains("Windows", row.Client, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlankUserId_ReturnsNothing()
    {
        await using var db = CreateContext();
        db.AuthEvents.Add(SignIn("me", Now.AddHours(-2), "10.0.0.1"));
        await db.SaveChangesAsync();

        Assert.Empty(await CreateService(db).GetRecentAsync(" ", 5, CancellationToken.None));
    }

    private static AuthEvent SignIn(string userId, DateTimeOffset when, string ip) => new()
    {
        UserId = userId,
        WhenUtc = when,
        Event = AuthenticationEventNames.LoginSucceeded,
        Ip = ip
    };

    private static AccountSignInHistoryService CreateService(ApplicationDbContext db)
        => new(db, new AdminClientDescriptorService(), new FixedTime(Now));

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTime(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
