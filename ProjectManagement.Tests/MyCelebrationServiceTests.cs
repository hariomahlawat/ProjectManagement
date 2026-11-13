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
using ProjectManagement.Services.Celebrations;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// A person's own birthday and anniversary from My account: the same Celebrations entries the
/// managers see, and never anyone else's (docs/ux/celebration-wish.md).
/// </summary>
public sealed class MyCelebrationServiceTests
{
    private const string Sharma = "u-sharma";
    private const string Verma = "u-verma";

    [Fact]
    public async Task AddingMyBirthday_CreatesOneCelebrationsEntry_LinkedAndNamedFromTheAccount()
    {
        await using var db = CreateDb();
        var (mine, admin) = Create(db);

        var result = await mine.SaveAsync(Sharma, CelebrationType.Birthday, 13, 11, null, "ignored for a birthday");

        Assert.True(result.Succeeded);
        var entry = await db.Celebrations.SingleAsync();
        Assert.Equal(Sharma, entry.UserId);
        Assert.Equal("Col R Sharma", entry.Name);
        Assert.Null(entry.SpouseName);
        // The managers' list shows the very same entry.
        var row = (await admin.ListAsync(new CelebrationDirectoryRequest(null, null, null, 1, 25))).Rows.Single();
        Assert.Equal(entry.Id, row.Id);
        Assert.Equal("Col R Sharma", row.LinkedUserLabel);
    }

    [Fact]
    public async Task ChangingMyDate_EditsTheSameEntry_AndKeepsTheNameAsWritten()
    {
        await using var db = CreateDb();
        var (mine, admin) = Create(db);
        var saved = await admin.SaveAsync(new CelebrationSaveCommand(
            null, CelebrationType.Anniversary, "Sharma family", "Anita", 2, 12, 2001, "admin", Sharma));

        var result = await mine.SaveAsync(Sharma, CelebrationType.Anniversary, 3, 12, 2001, "Anita Sharma");

        Assert.True(result.Succeeded);
        var entry = await db.Celebrations.SingleAsync();
        Assert.Equal(saved.Value, entry.Id);
        Assert.Equal((byte)3, entry.Day);
        Assert.Equal("Sharma family", entry.Name);
        Assert.Equal("Anita Sharma", entry.SpouseName);
        var line = (await mine.GetAsync(Sharma)).Single(item => item.Type == CelebrationType.Anniversary);
        Assert.True(line.IsLinked);
        Assert.Equal((byte)3, line.Day);
    }

    // The name is held once, on the account: a promotion shows on the list without editing the entry.
    [Fact]
    public async Task ALinkedEntry_IsListedUnderTheAccountsCurrentRankAndName()
    {
        await using var db = CreateDb();
        var (_, admin) = Create(db);
        await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Anniversary, "Lt Col Sharma", "Anita", 2, 12, null, "admin", Sharma));
        await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Mrs Verma", null, 3, 12, null, "admin"));

        var user = await db.Users.SingleAsync(item => item.Id == Sharma);
        user.Rank = "Brig";
        await db.SaveChangesAsync();
        var rows = (await admin.ListAsync(new CelebrationDirectoryRequest(null, null, null, 1, 25))).Rows;

        Assert.Contains(rows, row => row.DisplayName == "Brig R Sharma & Anita" && row.Name == "Lt Col Sharma");
        Assert.Contains(rows, row => row.DisplayName == "Mrs Verma");
    }

    [Fact]
    public async Task AnEntryThatFitsOnlyMyName_IsOfferedToMe_AndToNoOneElse()
    {
        await using var db = CreateDb();
        var (mine, admin) = Create(db);
        var entry = await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "R Sharma", null, 13, 11, null, "admin"));

        var sharma = (await mine.GetAsync(Sharma)).Single(item => item.Type == CelebrationType.Birthday);
        var verma = (await mine.GetAsync(Verma)).Single(item => item.Type == CelebrationType.Birthday);

        Assert.Equal(entry.Value, sharma.Suggestion!.Id);
        Assert.False(sharma.IsLinked);
        Assert.Null(verma.Suggestion);
        Assert.Null((await mine.GetAsync(Sharma)).Single(item => item.Type == CelebrationType.Anniversary).Suggestion);
    }

    [Fact]
    public async Task Claim_LinksTheOfferedEntry_AndRefusesAnyOther()
    {
        await using var db = CreateDb();
        var (mine, admin) = Create(db);
        var sharmaEntry = await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "R Sharma", null, 13, 11, null, "admin"));
        var vermaEntry = await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Maj S Verma", null, 14, 11, null, "admin"));

        var someoneElses = await mine.ClaimAsync(Sharma, vermaEntry.Value);
        var missing = await mine.ClaimAsync(Sharma, Guid.NewGuid());
        var own = await mine.ClaimAsync(Sharma, sharmaEntry.Value);
        var again = await mine.ClaimAsync(Verma, sharmaEntry.Value);

        Assert.Equal("CelebrationNotSuggested", someoneElses.ErrorCode);
        Assert.Equal("CelebrationNotSuggested", missing.ErrorCode);
        Assert.True(own.Succeeded);
        Assert.Equal("CelebrationNotSuggested", again.ErrorCode);
        Assert.Equal(Sharma, (await admin.GetAsync(sharmaEntry.Value))!.UserId);
        Assert.Null((await admin.GetAsync(vermaEntry.Value))!.UserId);
    }

    // The write itself refuses an entry that already has an owner, whatever was offered earlier.
    [Fact]
    public async Task LinkOnlyIfNotLinked_RefusesAnEntryThatAlreadyBelongsToSomeone_ButNotOneWhoseAccountIsGone()
    {
        await using var db = CreateDb();
        var (_, admin) = Create(db);
        var vermas = await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "S Verma", null, 14, 11, null, "admin", Verma));
        var orphan = await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Anniversary, "Sharma", null, 2, 12, null, "admin"));
        (await db.Celebrations.SingleAsync(item => item.Id == orphan.Value)).UserId = "account-that-was-deleted";
        await db.SaveChangesAsync();

        var taken = await admin.LinkAsync(vermas.Value, Sharma, Sharma, onlyIfNotLinked: true);
        var free = await admin.LinkAsync(orphan.Value, Sharma, Sharma, onlyIfNotLinked: true);
        // A manager may still move a link deliberately.
        var managerMove = await admin.LinkAsync(vermas.Value, Sharma, "admin");

        Assert.Equal("CelebrationAlreadyLinked", taken.ErrorCode);
        Assert.True(free.Succeeded);
        Assert.True(managerMove.Succeeded);
        Assert.Equal(Sharma, (await admin.GetAsync(vermas.Value))!.UserId);
    }

    // A change or unlink is refused once the entry no longer belongs to the person, and writes nothing.
    [Fact]
    public async Task ChangeOwnAndUnlinkOwn_RefuseAnEntryThatIsNoLongerMine()
    {
        await using var db = CreateDb();
        var (_, admin) = Create(db);
        var entry = await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "R Sharma", null, 13, 11, null, "admin", Sharma));
        // A manager moves the entry to another account after My account was shown.
        await admin.LinkAsync(entry.Value, Verma, "admin");

        var change = await admin.ChangeOwnAsync(entry.Value, Sharma, 1, 1, null, null);
        var unlink = await admin.UnlinkOwnAsync(entry.Value, Sharma);
        var badDate = await admin.ChangeOwnAsync(entry.Value, Verma, 31, 2, null, null);
        var ownersChange = await admin.ChangeOwnAsync(entry.Value, Verma, 20, 11, 1980, "ignored for a birthday");

        Assert.Equal("CelebrationNotYours", change.ErrorCode);
        Assert.Equal("CelebrationNotYours", unlink.ErrorCode);
        Assert.Equal("InvalidCelebrationDate", badDate.ErrorCode);
        Assert.True(ownersChange.Succeeded);
        var item = await admin.GetAsync(entry.Value);
        Assert.Equal(Verma, item!.UserId);
        Assert.Equal((byte)20, item.Day);
        Assert.Equal("R Sharma", item.Name);
        Assert.Null(item.SpouseName);
    }

    [Fact]
    public async Task ICannotHaveTwoBirthdays_AndSavingNeverTouchesAnotherPersonsEntry()
    {
        await using var db = CreateDb();
        var (mine, admin) = Create(db);
        var vermaEntry = await admin.SaveAsync(new CelebrationSaveCommand(null, CelebrationType.Birthday, "Maj S Verma", null, 14, 11, null, "admin", Verma));

        await mine.SaveAsync(Sharma, CelebrationType.Birthday, 13, 11, null, null);
        await mine.SaveAsync(Sharma, CelebrationType.Birthday, 15, 11, null, null);

        Assert.Equal(2, await db.Celebrations.CountAsync());
        Assert.Equal((byte)15, (await db.Celebrations.SingleAsync(item => item.UserId == Sharma)).Day);
        Assert.Equal((byte)14, (await admin.GetAsync(vermaEntry.Value))!.Day);
    }

    [Fact]
    public async Task Unlink_StopsTheWish_AndLeavesTheEntryOnTheCalendar()
    {
        await using var db = CreateDb();
        var (mine, _) = Create(db);
        await mine.SaveAsync(Sharma, CelebrationType.Birthday, 13, 11, null, null);

        var result = await mine.UnlinkAsync(Sharma, CelebrationType.Birthday);
        var nothingToUnlink = await mine.UnlinkAsync(Sharma, CelebrationType.Anniversary);

        Assert.True(result.Succeeded);
        Assert.Equal("CelebrationNotFound", nothingToUnlink.ErrorCode);
        var entry = await db.Celebrations.SingleAsync();
        Assert.Null(entry.UserId);
        Assert.Null(entry.DeletedUtc);
    }

    [Fact]
    public async Task AnAccountThatIsNotAPersons_HasNoSection_AndCannotSave()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser { Id = "kiosk", UserName = "kiosk", FullName = "Kiosk", AccountKind = UserAccountKind.Service });
        await db.SaveChangesAsync();
        var (mine, _) = Create(db);

        Assert.Empty(await mine.GetAsync("kiosk"));
        Assert.Empty(await mine.GetAsync("nobody"));
        Assert.False((await mine.SaveAsync("kiosk", CelebrationType.Birthday, 1, 1, null, null)).Succeeded);
        Assert.Empty(db.Celebrations);
    }

    [Fact]
    public async Task AnImpossibleDate_IsRefusedWithTheSameRuleAsTheManagersForm()
    {
        await using var db = CreateDb();
        var (mine, _) = Create(db);

        var result = await mine.SaveAsync(Sharma, CelebrationType.Birthday, 31, 2, null, null);

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidCelebrationDate", result.ErrorCode);
        Assert.Empty(db.Celebrations);
    }

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Users.Add(new ApplicationUser { Id = Sharma, UserName = "rsharma", Rank = "Col", FullName = "R Sharma" });
        db.Users.Add(new ApplicationUser { Id = Verma, UserName = "sverma", Rank = "Maj", FullName = "S Verma" });
        db.SaveChanges();
        return db;
    }

    private static (MyCelebrationService Mine, CelebrationAdministrationService Admin) Create(ApplicationDbContext db)
    {
        var admin = new CelebrationAdministrationService(
            db,
            new AdminTimeService(FakeClock.AtUtc(new DateTimeOffset(2026, 11, 13, 6, 0, 0, TimeSpan.Zero))),
            new NoAdminAudit(),
            NullLogger<CelebrationAdministrationService>.Instance);
        return (new MyCelebrationService(db, admin), admin);
    }

    private sealed class NoAdminAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
