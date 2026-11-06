using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Chat;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;
using ProjectManagement.Tests.Chat;
using Xunit;

namespace ProjectManagement.Tests;

// "Restore deleted items" (docs/ux/admin-module.md, PR 5): one tab strip over the bins, each tab
// only for a page the viewer can open, with what it can still restore.
public sealed class AdminRestoreTabsTests
{
    [Fact]
    public async Task Tabs_ListEveryKind_InOrder_WithWhatCanBeRestored()
    {
        await using var db = CreateDb();
        db.Projects.AddRange(
            new Project { Name = "Deleted one", CreatedByUserId = "po", IsDeleted = true },
            new Project { Name = "Deleted two", CreatedByUserId = "po", IsDeleted = true },
            new Project { Name = "Still here", CreatedByUserId = "po" });
        db.Events.Add(new Event
        {
            Id = Guid.NewGuid(),
            Title = "Removed review",
            StartUtc = DateTimeOffset.UtcNow,
            EndUtc = DateTimeOffset.UtcNow.AddHours(1),
            IsDeleted = true
        });
        await db.SaveChangesAsync();

        var tabs = await CreateService(db, AdminNavigationCatalog.Entries.Select(entry => entry.Key).ToArray())
            .GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(
            new[]
            {
                AdminRestoreTabKeys.Overview,
                AdminRestoreTabKeys.Projects,
                AdminRestoreTabKeys.ProjectDocuments,
                AdminRestoreTabKeys.RepositoryDocuments,
                AdminRestoreTabKeys.CalendarEvents,
                AdminRestoreTabKeys.ChatFiles
            },
            tabs.Select(tab => tab.Key).ToArray());
        var byKey = tabs.ToDictionary(tab => tab.Key);
        Assert.Null(byKey[AdminRestoreTabKeys.Overview].Count);
        Assert.Equal(2, byKey[AdminRestoreTabKeys.Projects].Count);
        Assert.Equal(1, byKey[AdminRestoreTabKeys.CalendarEvents].Count);
        Assert.Equal(0, byKey[AdminRestoreTabKeys.RepositoryDocuments].Count);
    }

    [Fact]
    public async Task Tabs_ForOtherPages_SayTheyOpenAnotherPage_AndChatFilesOpenAtTheUndoList()
    {
        await using var db = CreateDb();

        var tabs = (await CreateService(db, AdminNavigationCatalog.Entries.Select(entry => entry.Key).ToArray())
            .GetAsync(new ClaimsPrincipal(new ClaimsIdentity()))).ToDictionary(tab => tab.Key);

        Assert.True(tabs[AdminRestoreTabKeys.RepositoryDocuments].OpensAnotherPage);
        Assert.True(tabs[AdminRestoreTabKeys.ChatFiles].OpensAnotherPage);
        Assert.False(tabs[AdminRestoreTabKeys.Projects].OpensAnotherPage);
        Assert.Equal("/Admin/Chat/Storage#chatStorageBatches", tabs[AdminRestoreTabKeys.ChatFiles].HrefFrom("/Admin/Chat/Storage"));
        Assert.Equal("/Admin/Projects/Trash", tabs[AdminRestoreTabKeys.Projects].HrefFrom("/Admin/Projects/Trash"));
        Assert.Null(tabs[AdminRestoreTabKeys.ChatFiles].HrefFrom(null));
    }

    [Fact]
    public async Task Tabs_LeaveOutPagesTheViewerCannotOpen()
    {
        await using var db = CreateDb();

        // An administrator without Document Repository purge rights does not see its trash.
        var keys = AdminNavigationCatalog.Entries
            .Select(entry => entry.Key)
            .Where(key => key != AdminNavigationKeys.RepositoryTrash)
            .ToArray();
        var tabs = await CreateService(db, keys).GetAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.DoesNotContain(tabs, tab => tab.Key == AdminRestoreTabKeys.RepositoryDocuments);
        Assert.Contains(tabs, tab => tab.Key == AdminRestoreTabKeys.Projects);
    }

    [Fact]
    public void EveryTabPage_IsReachedFromRestoreDeletedItems()
    {
        // In search and Every admin page, each bin says it is part of Restore deleted items.
        foreach (var key in new[]
                 {
                     AdminNavigationKeys.ProjectTrash,
                     AdminNavigationKeys.DocumentRecycle,
                     AdminNavigationKeys.DeletedEvents,
                     AdminNavigationKeys.RepositoryTrash
                 })
        {
            Assert.Equal(AdminNavigationKeys.RecoveryCentre, AdminNavigationCatalog.GetEntry(key).SidebarParent);
        }
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AdminRestoreTabsService CreateService(ApplicationDbContext db, string[] openableKeys) =>
        new(
            db,
            new ChatStorageService(
                db,
                new ChatAttachmentServiceTests.MutableOptions(),
                new ChatAttachmentServiceTests.ManualClock(),
                new ChatAttachmentServiceTests.RecordingOutbox(),
                new ChatAttachmentServiceTests.RecordingAudit(),
                NullLogger<ChatStorageService>.Instance),
            new FixedAccess(openableKeys),
            NullLogger<AdminRestoreTabsService>.Instance);

    private sealed class FixedAccess(string[] keys) : IAdminNavigationAccess
    {
        public Task<bool> CanOpenAsync(ClaimsPrincipal user, AdminNavigationEntry entry) =>
            Task.FromResult(keys.Contains(entry.Key));

        public Task<IReadOnlyList<AdminNavigationEntry>> GetOpenableEntriesAsync(ClaimsPrincipal user) =>
            Task.FromResult<IReadOnlyList<AdminNavigationEntry>>(AdminNavigationCatalog.Entries.Where(entry => keys.Contains(entry.Key)).ToArray());
    }
}
