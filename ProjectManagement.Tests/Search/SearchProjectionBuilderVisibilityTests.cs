using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Query;
using ProjectManagement.Services.SearchV2.Security;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchProjectionBuilderVisibilityTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task ProjectDocuments_FollowTheirProjectOutOfSearch(bool projectDeleted, bool projectArchived, bool expectedIndexed)
    {
        await using var db = CreateDb();
        db.Projects.Add(Project(1, projectDeleted, projectArchived));
        db.ProjectDocuments.Add(Document(10, projectId: 1));
        await db.SaveChangesAsync();
        var builder = CreateBuilder(db);

        var single = await builder.BuildEntityAsync("ProjectDocument", "10", CancellationToken.None);
        var projectReindex = await builder.BuildEntityAsync("Project", "1", CancellationToken.None);

        Assert.Equal(expectedIndexed, single.Any(entry => entry.EntityType == "ProjectDocument"));
        Assert.Equal(expectedIndexed, projectReindex.Any(entry => entry.EntityType == "ProjectDocument"));
    }

    [Theory]
    [InlineData(ApprovalStatus.Approved, true)]
    [InlineData(ApprovalStatus.Pending, false)]
    [InlineData(ApprovalStatus.Rejected, false)]
    public async Task ProliferationEntries_AreIndexedOnlyOnceApproved(ApprovalStatus status, bool expectedIndexed)
    {
        await using var db = CreateDb();
        db.Projects.Add(Project(1, deleted: false, archived: false));
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = id,
            ProjectId = 1,
            Source = ProliferationSource.Sdd,
            UnitName = "Field unit",
            ProliferationDate = new DateOnly(2026, 6, 1),
            Quantity = 4,
            ApprovalStatus = status,
            SubmittedByUserId = "submitter",
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();

        var entries = await CreateBuilder(db).BuildEntityAsync("ProliferationGranular", id.ToString(), CancellationToken.None);

        Assert.Equal(expectedIndexed, entries.Count == 1);
    }

    [Fact]
    public async Task EveryIndexedEntry_CarriesThePolicyFromTheSharedSourceMap()
    {
        await using var db = CreateDb();
        db.Projects.Add(Project(1, deleted: false, archived: false));
        db.ProjectDocuments.Add(Document(10, projectId: 1));
        await db.SaveChangesAsync();

        var entries = await CreateBuilder(db).BuildEntityAsync("Project", "1", CancellationToken.None);

        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.Equal(SearchSourcePolicies.RequiredPolicyFor(entry.SourceModule), entry.RequiredPolicy));
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SearchProjectionBuilder CreateBuilder(ApplicationDbContext db) =>
        new(db, new UrlBuilder(), new SearchQueryNormalizer(), Options.Create(new SearchV2Options()));

    private static Project Project(int id, bool deleted, bool archived) => new()
    {
        Id = id,
        Name = "Pinaka radar upgrade",
        CreatedByUserId = "creator",
        CaseFileNumber = $"PR-{id}",
        LifecycleStatus = ProjectLifecycleStatus.Active,
        RowVersion = Guid.NewGuid().ToByteArray(),
        IsDeleted = deleted,
        IsArchived = archived
    };

    private static ProjectDocument Document(int id, int projectId) => new()
    {
        Id = id,
        ProjectId = projectId,
        Title = "Radar acceptance report",
        StorageKey = $"docs/{id}.pdf",
        OriginalFileName = "acceptance.pdf",
        ContentType = "application/pdf",
        Status = ProjectDocumentStatus.Published,
        UploadedByUserId = "uploader",
        FileStamp = 1
    };
}
