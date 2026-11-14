using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
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

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task ProliferationEntries_FollowTheirProjectOutOfSearch(bool projectDeleted, bool projectArchived, bool expectedIndexed)
    {
        await using var db = CreateDb();
        db.Projects.Add(Project(1, projectDeleted, projectArchived));
        var id = Guid.NewGuid();
        db.ProliferationGranularEntries.Add(Proliferation(id, projectId: 1, ApprovalStatus.Approved));
        await db.SaveChangesAsync();
        var builder = CreateBuilder(db);

        var single = await builder.BuildEntityAsync("ProliferationGranular", id.ToString(), CancellationToken.None);
        var all = await builder.BuildAllAsync(CancellationToken.None);

        Assert.Equal(expectedIndexed, single.Count == 1);
        Assert.Equal(expectedIndexed, all.Any(entry => entry.EntityType == "ProliferationGranular"));
    }

    [Fact]
    public async Task IprRecord_OfATrashedProject_IsNotShownUnderThatProject()
    {
        await using var db = CreateDb();
        db.Projects.Add(Project(1, deleted: true, archived: false));
        db.IprRecords.Add(new IprRecord
        {
            Id = 5,
            IprFilingNumber = "PAT-0005",
            Title = "Gimbal lock",
            Type = IprType.Patent,
            Status = IprStatus.Filed,
            ProjectId = 1,
            FiledAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var entry = Assert.Single(await CreateBuilder(db).BuildEntityAsync("IprRecord", "5", CancellationToken.None));

        Assert.Null(entry.ParentProjectId);
        Assert.Equal("IprRecord", entry.CanonicalEntityType);
        Assert.DoesNotContain("Pinaka radar upgrade", entry.Subtitle ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectDependents_ListEveryRowThatCarriesTheProject()
    {
        await using var db = CreateDb();
        db.Projects.Add(Project(1, deleted: false, archived: false));
        db.Projects.Add(Project(2, deleted: false, archived: false));
        db.IprRecords.Add(new IprRecord { Id = 5, IprFilingNumber = "PAT-0005", Type = IprType.Patent, Status = IprStatus.Filed, ProjectId = 1 });
        db.IprRecords.Add(new IprRecord { Id = 6, IprFilingNumber = "PAT-0006", Type = IprType.Patent, Status = IprStatus.Filed, ProjectId = 2 });
        var proliferationId = Guid.NewGuid();
        db.ProliferationGranularEntries.Add(Proliferation(proliferationId, projectId: 1, ApprovalStatus.Pending));
        var trainingId = Guid.NewGuid();
        db.TrainingProjects.Add(new TrainingProject { TrainingId = trainingId, ProjectId = 1 });
        await db.SaveChangesAsync();

        var dependents = await CreateBuilder(db).FindProjectDependentsAsync(1, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                new SearchEntityRef("ProjectTot", "1"),
                new SearchEntityRef("IprRecord", "5"),
                new SearchEntityRef("ProliferationGranular", proliferationId.ToString()),
                new SearchEntityRef("Training", trainingId.ToString())
            },
            dependents);
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

    private static ProliferationGranular Proliferation(Guid id, int projectId, ApprovalStatus status)
    {
        var now = DateTime.UtcNow;
        return new ProliferationGranular
        {
            Id = id,
            ProjectId = projectId,
            Source = ProliferationSource.Sdd,
            UnitName = "Field unit",
            ProliferationDate = new DateOnly(2026, 6, 1),
            Quantity = 4,
            ApprovalStatus = status,
            SubmittedByUserId = "submitter",
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = new byte[] { 1 }
        };
    }

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
