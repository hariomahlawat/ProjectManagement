using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.DocumentRepository.Pages.Documents;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.Fakes;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>
/// Who may read a repository document, and what is recorded when they do. Deactivated
/// documents are readable only by the people who manage them.
/// </summary>
public sealed class DocRepoAccessAndAuditTests
{
    [Fact]
    public void InactiveViewers_AreEveryoneWhoCanDeactivateOrEditDocuments()
    {
        var expected = Policies.Documents.UploadAndSoftDeleteRoles
            .Concat(Policies.Documents.MetadataEditorRoles)
            .Distinct()
            .OrderBy(role => role);

        Assert.Equal(expected, Policies.Documents.InactiveViewerRoles.OrderBy(role => role));
        Assert.DoesNotContain(RoleNames.Comdt, Policies.Documents.InactiveViewerRoles);
    }

    [Fact]
    public async Task Readable_HidesDeletedDocumentsFromEveryone_AndInactiveOnesFromReaders()
    {
        await using var db = CreateDb();
        var active = SeedDocument(db, "Active");
        var inactive = SeedDocument(db, "Inactive", isActive: false);
        SeedDocument(db, "Deleted", isDeleted: true);
        await db.SaveChangesAsync();
        var access = Access();

        Assert.True(await access.CanViewInactiveAsync(Manager));
        Assert.False(await access.CanViewInactiveAsync(Reader));

        var forManager = await access.Readable(db.Documents, canViewInactive: true).Select(d => d.Id).ToListAsync();
        var forReader = await access.Readable(db.Documents, canViewInactive: false).Select(d => d.Id).ToListAsync();

        Assert.Equal(new[] { active.Id, inactive.Id }.OrderBy(id => id), forManager.OrderBy(id => id));
        Assert.Equal(new[] { active.Id }, forReader);
    }

    [Fact]
    public async Task Download_OfAnInactiveDocument_IsNotFoundForReaders_AndRecordedForManagers()
    {
        await using var db = CreateDb();
        var inactive = SeedDocument(db, "Superseded circular", isActive: false);
        await db.SaveChangesAsync();
        var storage = new RecordingDocStorage();
        storage.Put(inactive.StoragePath, PdfBytes());

        var asReader = CreateDownload(db, storage, Reader);
        Assert.IsType<NotFoundResult>(await asReader.OnGetAsync(inactive.Id, CancellationToken.None));

        var asManager = CreateDownload(db, storage, Manager);
        Assert.IsType<FileStreamResult>(await asManager.OnGetAsync(inactive.Id, CancellationToken.None));
        // A second click within the access window is the same download.
        Assert.IsType<FileStreamResult>(await CreateDownload(db, storage, Manager).OnGetAsync(inactive.Id, CancellationToken.None));

        var audits = await db.DocRepoAudits.AsNoTracking().ToListAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(DocRepoAuditEventTypes.Downloaded, audit.EventType);
        Assert.Equal(ManagerId, audit.ActorUserId);
        Assert.Equal(inactive.Id, audit.DocumentId);
    }

    [Fact]
    public async Task View_IsRecordedAndNeverCached()
    {
        await using var db = CreateDb();
        var document = SeedDocument(db, "Minutes");
        await db.SaveChangesAsync();
        var storage = new RecordingDocStorage();
        storage.Put(document.StoragePath, PdfBytes());

        var page = new ViewModel(db, storage, new TestWebHostEnvironment(), NullLogger<ViewModel>.Instance, Access(), new DocRepoAuditService(db));
        ConfigurePage(page, Reader);

        var result = await page.OnGetAsync(document.Id, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.True(file.EnableRangeProcessing);
        Assert.Equal("private, no-store", page.Response.Headers.CacheControl.ToString());
        var audit = Assert.Single(await db.DocRepoAudits.AsNoTracking().ToListAsync());
        Assert.Equal(DocRepoAuditEventTypes.Viewed, audit.EventType);
    }

    [Fact]
    public async Task View_OfAnInactiveDocument_IsAllowedForManagers()
    {
        await using var db = CreateDb();
        var inactive = SeedDocument(db, "Old policy", isActive: false);
        await db.SaveChangesAsync();
        var storage = new RecordingDocStorage();
        storage.Put(inactive.StoragePath, PdfBytes());

        var asReader = new ViewModel(db, storage, new TestWebHostEnvironment(), NullLogger<ViewModel>.Instance, Access(), new DocRepoAuditService(db));
        ConfigurePage(asReader, Reader);
        var asManager = new ViewModel(db, storage, new TestWebHostEnvironment(), NullLogger<ViewModel>.Instance, Access(), new DocRepoAuditService(db));
        ConfigurePage(asManager, Manager);

        Assert.IsType<NotFoundResult>(await asReader.OnGetAsync(inactive.Id, CancellationToken.None));
        Assert.IsType<FileStreamResult>(await asManager.OnGetAsync(inactive.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    [InlineData(-5)]
    public async Task Index_IgnoresAYearOutsideTheSupportedRange(int year)
    {
        await using var db = CreateDb();
        SeedLookups(db);
        SeedDocument(db, "Dated");
        await db.SaveChangesAsync();
        var page = CreateIndex(db, Reader);
        page.Year = year;

        await page.OnGetAsync();

        Assert.Null(page.Year);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Index_IncludeInactive_IsIgnoredForReaders()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        SeedDocument(db, "Active");
        SeedDocument(db, "Inactive", isActive: false);
        await db.SaveChangesAsync();

        var reader = CreateIndex(db, Reader);
        reader.IncludeInactive = true;
        await reader.OnGetAsync();

        var manager = CreateIndex(db, Manager);
        manager.IncludeInactive = true;
        await manager.OnGetAsync();

        Assert.False(reader.CanViewInactive);
        Assert.False(reader.IncludeInactive);
        Assert.Equal(1, reader.TotalCount);
        Assert.True(manager.CanViewInactive);
        Assert.Equal(2, manager.TotalCount);
    }

    private static DownloadModel CreateDownload(ProjectManagement.Data.ApplicationDbContext db, RecordingDocStorage storage, System.Security.Claims.ClaimsPrincipal user)
    {
        var page = new DownloadModel(db, storage, new TestWebHostEnvironment(), NullLogger<DownloadModel>.Instance, Access(), new DocRepoAuditService(db));
        ConfigurePage(page, user);
        return page;
    }

    private static IndexModel CreateIndex(ProjectManagement.Data.ApplicationDbContext db, System.Security.Claims.ClaimsPrincipal user)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new System.Collections.Generic.KeyValuePair<string, string?>("DocRepo:EnableListViewUxUpgrade", "true") })
            .Build();
        var page = new IndexModel(db, new DocumentSearchService(), configuration, Access());
        ConfigurePage(page, user);
        return page;
    }
}
