using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Linking a birthday or anniversary to the account of the person it is for, so PRISM can wish
/// them on the day.
/// </summary>
public sealed class CelebrationUserLinkTests
{
    private const string Actor = "admin";

    [Fact]
    public async Task Save_LinksTheCelebrationToTheChosenAccount()
    {
        await using var db = CreateDb();
        var sharma = AddUser(db, "u-sharma", "Col", "R Sharma");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Birthday, "Col R Sharma", null, 13, 11, null, Actor, sharma.Id));

        Assert.True(result.Succeeded);
        Assert.Equal(sharma.Id, (await db.Celebrations.SingleAsync()).UserId);
        Assert.Equal(sharma.Id, (await service.GetAsync(result.Value))!.UserId);
    }

    [Fact]
    public async Task Save_WithoutAnAccount_LeavesItNotLinked()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var result = await service.SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Birthday, "Mrs Sharma", null, 2, 3, null, Actor));

        Assert.True(result.Succeeded);
        Assert.Null((await db.Celebrations.SingleAsync()).UserId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("u-disabled")]
    [InlineData("u-service")]
    public async Task Save_RefusesAnAccountThatCannotSignIn(string userId)
    {
        await using var db = CreateDb();
        AddUser(db, "u-disabled", "Maj", "A Rao").IsDisabled = true;
        AddUser(db, "u-service", "", "Kiosk").AccountKind = UserAccountKind.Service;
        await db.SaveChangesAsync();

        var result = await CreateService(db).SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Birthday, "Someone", null, 1, 1, null, Actor, userId));

        Assert.False(result.Succeeded);
        Assert.Equal("CelebrationUserNotFound", result.ErrorCode);
        Assert.Empty(db.Celebrations);
    }

    [Fact]
    public async Task Save_AllowsOneBirthdayAndOneAnniversaryPerAccount()
    {
        await using var db = CreateDb();
        var sharma = AddUser(db, "u-sharma", "Col", "R Sharma");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var birthday = await service.SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Birthday, "Col R Sharma", null, 13, 11, null, Actor, sharma.Id));
        var anniversary = await service.SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Anniversary, "Col R Sharma", "Mrs Sharma", 2, 12, 2001, Actor, sharma.Id));
        var secondBirthday = await service.SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Birthday, "R Sharma", null, 14, 11, null, Actor, sharma.Id));
        // Saving the linked entry again is not a clash with itself.
        var resave = await service.SaveAsync(new CelebrationSaveCommand(
            birthday.Value, CelebrationType.Birthday, "Col R Sharma", null, 13, 11, 1975, Actor, sharma.Id));

        Assert.True(birthday.Succeeded);
        Assert.True(anniversary.Succeeded);
        Assert.True(resave.Succeeded);
        Assert.False(secondBirthday.Succeeded);
        Assert.Equal("CelebrationUserAlreadyLinked", secondBirthday.ErrorCode);
    }

    [Fact]
    public async Task List_ShowsWhoIsLinked_AndSuggestsTheOneAccountThatFits()
    {
        await using var db = CreateDb();
        var sharma = AddUser(db, "u-sharma", "Col", "R Sharma");
        AddUser(db, "u-verma", "Maj", "S Verma");
        AddUser(db, "u-singh-a", "Lt Col", "A Singh");
        AddUser(db, "u-singh-b", "Maj", "B Singh");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Col R Sharma", null, 13, 11, null, Actor, sharma.Id));
        await service.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Maj S Verma", null, 14, 11, null, Actor));
        await service.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Singh", null, 15, 11, null, Actor));
        // Sharma's birthday is already linked, so a second entry in his name is not offered his account.
        await service.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "R Sharma", null, 16, 11, null, Actor));

        var all = await service.ListAsync(new CelebrationDirectoryRequest(null, null, null, 1, 25));
        var unlinked = await service.ListAsync(new CelebrationDirectoryRequest(null, null, null, 1, 25, "unlinked"));

        var rows = all.Rows.ToDictionary(row => row.Name);
        Assert.Equal("Col R Sharma", rows["Col R Sharma"].LinkedUserLabel);
        Assert.Null(rows["Col R Sharma"].SuggestedUser);
        Assert.Null(rows["Maj S Verma"].LinkedUserLabel);
        Assert.Equal("u-verma", rows["Maj S Verma"].SuggestedUser!.Id);
        Assert.Null(rows["Singh"].SuggestedUser); // two accounts fit, so neither is offered
        Assert.Null(rows["R Sharma"].SuggestedUser);
        Assert.Equal(3, unlinked.FilteredCount);
        Assert.DoesNotContain(unlinked.Rows, row => row.Name == "Col R Sharma");
    }

    [Fact]
    public async Task Link_SetsOnlyTheAccount()
    {
        await using var db = CreateDb();
        AddUser(db, "u-verma", "Maj", "S Verma");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var saved = await service.SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Anniversary, "Maj S Verma", "Mrs Verma", 9, 2, 2010, Actor));

        var result = await service.LinkAsync(saved.Value, "u-verma", Actor);

        Assert.True(result.Succeeded);
        var item = await service.GetAsync(saved.Value);
        Assert.Equal("u-verma", item!.UserId);
        Assert.Equal("Mrs Verma", item.SpouseName);
        Assert.Equal((short)2010, item.Year);
        Assert.Equal((byte)9, item.Day);
    }

    // Linking writes the account and nothing else, so it cannot undo a correction saved meanwhile.
    [Fact]
    public async Task Link_ChangesOnlyTheAccount_AndRefusesWhatSaveRefuses()
    {
        await using var db = CreateDb();
        AddUser(db, "u-verma", "Maj", "S Verma");
        AddUser(db, "u-disabled", "Maj", "A Rao").IsDisabled = true;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var first = await service.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Maj S Verma", null, 9, 2, null, Actor));
        var second = await service.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "S Verma", null, 10, 2, null, Actor));

        // Someone corrects the date after the list was shown; the link must not put the old date back.
        await service.SaveAsync(new CelebrationSaveCommand(first.Value, CelebrationType.Birthday, "Maj S Verma", null, 19, 2, null, Actor));
        var linked = await service.LinkAsync(first.Value, "u-verma", Actor);
        var clash = await service.LinkAsync(second.Value, "u-verma", Actor);
        var disabled = await service.LinkAsync(second.Value, "u-disabled", Actor);
        var missing = await service.LinkAsync(Guid.NewGuid(), "u-verma", Actor);

        Assert.True(linked.Succeeded);
        Assert.Equal((byte)19, (await service.GetAsync(first.Value))!.Day);
        Assert.Equal("CelebrationUserAlreadyLinked", clash.ErrorCode);
        Assert.Equal("CelebrationUserNotFound", disabled.ErrorCode);
        Assert.Equal("CelebrationNotFound", missing.ErrorCode);
        Assert.Null((await service.GetAsync(second.Value))!.UserId);
    }

    [Fact]
    public async Task AnAccountThatCanNoLongerSignIn_ReadsAsNotLinked()
    {
        await using var db = CreateDb();
        var rao = AddUser(db, "u-rao", "Maj", "A Rao");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Maj A Rao", null, 1, 1, null, Actor, rao.Id));

        rao.IsDisabled = true;
        await db.SaveChangesAsync();
        var list = await service.ListAsync(new CelebrationDirectoryRequest(null, null, null, 1, 25));

        Assert.Null(list.Rows.Single().LinkedUserLabel);
        Assert.DoesNotContain(await service.ListLinkableUsersAsync(), user => user.Id == rao.Id);
    }

    [Theory]
    [InlineData("Col R Sharma", "u-sharma")]
    [InlineData("r. sharma", "u-sharma")]
    [InlineData("Sharma", "u-sharma")]
    [InlineData("Col R Sharma (Retd)", "u-sharma")]
    [InlineData("Verma", null)]
    [InlineData("", null)]
    public void Matcher_SuggestsOnlyAClearFit(string written, string? expected)
    {
        var users = new[]
        {
            new CelebrationUserOption("u-sharma", "Col R Sharma"),
            new CelebrationUserOption("u-gupta", "Maj K Gupta")
        };

        Assert.Equal(expected, CelebrationUserMatcher.Suggest(written, users)?.Id);
    }

    [Fact]
    public void Label_DoesNotRepeatARankAlreadyInTheName()
    {
        Assert.Equal("Col R Sharma", CelebrationUserMatcher.Label("Col", "R Sharma", "rsharma"));
        Assert.Equal("Col R Sharma", CelebrationUserMatcher.Label("Col", "Col R Sharma", "rsharma"));
        Assert.Equal("rsharma", CelebrationUserMatcher.Label("", " ", "rsharma"));
    }

    private static ApplicationUser AddUser(ApplicationDbContext db, string id, string rank, string fullName)
    {
        var user = new ApplicationUser { Id = id, UserName = id, Rank = rank, FullName = fullName };
        db.Users.Add(user);
        return user;
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CelebrationAdministrationService CreateService(ApplicationDbContext db) =>
        new(db,
            new AdminTimeService(FakeClock.AtUtc(new DateTimeOffset(2026, 11, 13, 6, 0, 0, TimeSpan.Zero))),
            new NoAdminAudit(),
            NullLogger<CelebrationAdministrationService>.Instance);

    private sealed class NoAdminAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
