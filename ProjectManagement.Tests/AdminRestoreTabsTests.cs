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

    // Bin counts from one place (docs/ux/admin-review-2-fixes.md, PR 5): Home, Restore deleted
    // items and its tabs give the same figures.
    [Fact]
    public async Task BinCounts_AreTheSameOnHome_TheOverview_AndTheTabs()
    {
        var now = new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);
        await using var db = CreateDb();
        db.Projects.AddRange(
            new Project { Id = 1, Name = "Deleted one", CreatedByUserId = "po", IsDeleted = true, DeletedAt = now.AddDays(-2) },
            new Project { Id = 2, Name = "Deleted two", CreatedByUserId = "po", IsDeleted = true, DeletedAt = now.AddDays(-40) },
            new Project { Id = 3, Name = "Still here", CreatedByUserId = "po" },
            new Project { Id = 4, Name = "Archived", CreatedByUserId = "po", IsArchived = true });
        db.ProjectDocuments.AddRange(
            new ProjectDocument { ProjectId = 3, Title = "Binned", Status = ProjectDocumentStatus.SoftDeleted, FileSize = 10 },
            new ProjectDocument { ProjectId = 3, Title = "Binned too", Status = ProjectDocumentStatus.SoftDeleted, FileSize = 20 },
            new ProjectDocument { ProjectId = 3, Title = "Kept", Status = ProjectDocumentStatus.Published });
        db.Events.AddRange(
            new Event { Id = Guid.NewGuid(), Title = "Removed", StartUtc = now, EndUtc = now.AddHours(1), IsDeleted = true },
            new Event { Id = Guid.NewGuid(), Title = "Kept", StartUtc = now, EndUtc = now.AddHours(1) });
        await db.SaveChangesAsync();

        var time = new ProjectManagement.Services.Admin.AdminTimeService(ProjectManagement.Tests.Fakes.FakeClock.AtUtc(now));
        var home = (await new ProjectManagement.Services.Admin.AdminDashboardService(db, time).GetAsync()).Metrics;
        var overview = await new AdminRecoverySummaryService(
            db,
            time,
            new ProjectManagement.Services.Admin.AuditActionPresentationCatalog(),
            Microsoft.Extensions.Options.Options.Create(new ProjectManagement.Configuration.ProjectRetentionOptions()),
            Microsoft.Extensions.Options.Options.Create(new ProjectManagement.Configuration.AdminRecoveryOptions())).GetAsync();
        var tabs = (await CreateService(db, AdminNavigationCatalog.Entries.Select(entry => entry.Key).ToArray())
            .GetAsync(new ClaimsPrincipal(new ClaimsIdentity()))).ToDictionary(tab => tab.Key);

        Assert.Equal(2, home.TrashedProjects);
        Assert.Equal(home.TrashedProjects, overview.TrashedProjects);
        Assert.Equal(home.TrashedProjects, tabs[AdminRestoreTabKeys.Projects].Count);

        Assert.Equal(2, home.DeletedDocuments);
        Assert.Equal(home.DeletedDocuments, overview.RecycledDocuments);
        Assert.Equal(home.DeletedDocuments, tabs[AdminRestoreTabKeys.ProjectDocuments].Count);

        Assert.Equal(1, home.DeletedEvents);
        Assert.Equal(home.DeletedEvents, overview.DeletedEvents);
        Assert.Equal(home.DeletedEvents, tabs[AdminRestoreTabKeys.CalendarEvents].Count);
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
