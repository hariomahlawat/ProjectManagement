using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// "Lists used in forms" (docs/ux/admin-module.md, PR 6): every list a form offers, grouped by
// where it is used, including the Document Repository and social media lists.
public sealed class ListsUsedInFormsTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Overview_CountsTheDocumentRepositoryLists()
    {
        await using (var seed = new ApplicationDbContext(_options))
        {
            seed.DocumentCategories.AddRange(
                new DocumentCategory { Name = "Policy" },
                new DocumentCategory { Name = "Minutes" },
                new DocumentCategory { Name = "Old form", IsActive = false });
            seed.OfficeCategories.Add(new OfficeCategory { Name = "Training" });
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var overview = await new MasterDataAdministrationQueryService(db, new AuditActionPresentationCatalog()).GetOverviewAsync(2026);

        var types = overview.Domains.Single(domain => domain.Key == "document-types");
        Assert.Equal((3, 2, 1, 0), (types.Total, types.Active, types.Inactive, types.InUse));
        Assert.Equal("DocumentRepository", types.Area);
        Assert.Equal(1, overview.Domains.Single(domain => domain.Key == "document-offices").Total);
        Assert.Equal(0, overview.Domains.Single(domain => domain.Key == "social-media-platforms").Total);
    }

    [Fact]
    public async Task EveryList_HasAGroupAndSaysWhichFormUsesIt()
    {
        await using var db = new ApplicationDbContext(_options);
        var overview = await new MasterDataAdministrationQueryService(db, new AuditActionPresentationCatalog()).GetOverviewAsync(2026);

        Assert.Equal(13, overview.Domains.Count);
        Assert.All(overview.Domains, domain =>
        {
            Assert.Contains(domain.Group, MasterDataGroups.Ordered);
            Assert.False(string.IsNullOrWhiteSpace(domain.UsedIn));
            Assert.False(string.IsNullOrWhiteSpace(domain.Policy));
        });
        Assert.All(MasterDataGroups.Ordered, group => Assert.Contains(overview.Domains, domain => domain.Group == group));
    }

    [Fact]
    public async Task RecentChanges_ShowThePersonsName_WhenTheLogKeptOnlyTheirId()
    {
        await using (var seed = new ApplicationDbContext(_options))
        {
            seed.Users.Add(new ApplicationUser { Id = "u-1", UserName = "admin.rao", FullName = "Maj S. Rao" });
            seed.AuditLogs.Add(new AuditLog
            {
                TimeUtc = DateTime.UtcNow,
                Level = "Info",
                Action = "MasterData.SponsoringUnitCreated",
                UserId = "u-1"
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(_options);
        var overview = await new MasterDataAdministrationQueryService(db, new AuditActionPresentationCatalog()).GetOverviewAsync(2026);

        Assert.Equal("Maj S. Rao", Assert.Single(overview.RecentChanges).Actor);
    }
}
