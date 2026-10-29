using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Areas.DocumentRepository.Pages.Admin;
using ProjectManagement.Areas.DocumentRepository.Pages.Documents;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Services.DocRepo;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>Behaviour behind the redesigned repository pages.</summary>
public sealed class DocRepoPagesTests
{
    [Fact]
    public async Task Library_FilterCountsApplyEveryOtherFilter()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        db.OfficeCategories.Add(new OfficeCategory { Id = 2, Name = "HQ Plans" });
        db.DocumentCategories.Add(new DocumentCategory { Id = 2, Name = "Policy" });
        Dated(SeedDocument(db, "Letter 2026"), 2026, office: 1, type: 1);
        Dated(SeedDocument(db, "Letter 2025"), 2025, office: 1, type: 1);
        Dated(SeedDocument(db, "Policy 2026"), 2026, office: 2, type: 2);
        await db.SaveChangesAsync();

        var page = CreateIndex(db, Reader);
        page.DocumentCategoryId = 1;
        await page.OnGetAsync();

        Assert.Equal(2, page.TotalCount);
        // Office counts respect the type filter: only letters are counted.
        Assert.Equal(2, page.OfficeFacets.Single(f => f.Label == "CPDS North").Count);
        Assert.Equal(0, page.OfficeFacets.Single(f => f.Label == "HQ Plans").Count);
        // The type facet ignores its own filter, so every type shows what choosing it would give.
        Assert.Equal(1, page.TypeFacets.Single(f => f.Label == "Policy").Count);
        Assert.Equal(new[] { "2026", "2025" }, page.YearFacets.Select(f => f.Value));
        Assert.Equal("Letter", page.TypeName);
    }

    [Fact]
    public async Task Library_SortsAndLoadsTagsForThePage()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var older = Dated(SeedDocument(db, "Beta"), 2024, 1, 1);
        var newer = Dated(SeedDocument(db, "Alpha"), 2026, 1, 1);
        newer.DocumentTags.Add(new DocumentTag { DocumentId = newer.Id, Tag = new Tag { Name = "tec", NormalizedName = "tec" } });
        await db.SaveChangesAsync();

        var byDate = CreateIndex(db, Reader);
        await byDate.OnGetAsync();
        var byTitle = CreateIndex(db, Reader);
        byTitle.Sort = "title";
        await byTitle.OnGetAsync();

        Assert.Equal(new[] { newer.Id, older.Id }, byDate.Items.Select(i => i.Id));
        Assert.Equal(new[] { "tec" }, byDate.Items[0].Tags);
        Assert.Equal(new[] { "Alpha", "Beta" }, byTitle.Items.Select(i => i.Subject));
    }

    [Theory]
    [InlineData(null, false, DocRepoSort.Newest)]
    [InlineData(null, true, DocRepoSort.Relevance)]
    [InlineData("relevance", false, DocRepoSort.Newest)]
    [InlineData("TITLE", false, DocRepoSort.Title)]
    [InlineData("nonsense", true, DocRepoSort.Relevance)]
    public void SortParser_RelevanceOnlyAppliesToASearch(string? value, bool hasSearch, DocRepoSort expected)
        => Assert.Equal(expected, DocRepoSortParser.Parse(value, hasSearch));

    [Fact]
    public async Task UploadCheckHash_ReportsDuplicatesAndTrashedCopies()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var activeBytes = PdfBytes("active");
        var externalBytes = PdfBytes("external");
        var trashedBytes = PdfBytes("trashed");
        SeedDocument(db, "Visible circular", sha256: Sha256Hex(activeBytes));
        SeedDocument(db, "FFC sheet 42", isExternal: true, sha256: Sha256Hex(externalBytes));
        SeedDocument(db, "Old letter", isDeleted: true, sha256: Sha256Hex(trashedBytes));
        await db.SaveChangesAsync();

        string Json(IActionResult result) => System.Text.Json.JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value);
        var page = new UploadModel(db, new RecordingDocStorage(), new NoopFileScanner(), new DocRepoAuditService(db), Authorization(),
            Microsoft.Extensions.Options.Options.Create(new ProjectManagement.Configuration.ProjectDocumentOptions()),
            Microsoft.Extensions.Options.Options.Create(new ProjectManagement.Infrastructure.DocumentUploadRateLimitOptions()), Access());
        ConfigurePage(page, User("clerk", RoleNames.MainOfficeClerk));

        Assert.Contains("\"subject\":\"Visible circular\"", Json(await page.OnGetCheckHashAsync(Sha256Hex(activeBytes).ToLowerInvariant(), CancellationToken.None)));
        // A copy from another module is a duplicate, but its title is not shown (it is not in the library list).
        var external = Json(await page.OnGetCheckHashAsync(Sha256Hex(externalBytes), CancellationToken.None));
        Assert.Contains("\"external\":true", external);
        Assert.Contains("\"subject\":null", external);
        Assert.Contains("\"restoresTrashed\":true", Json(await page.OnGetCheckHashAsync(Sha256Hex(trashedBytes), CancellationToken.None)));
        Assert.Contains("\"duplicate\":false", Json(await page.OnGetCheckHashAsync(Sha256Hex(PdfBytes("new")), CancellationToken.None)));
        Assert.IsType<BadRequestResult>(await page.OnGetCheckHashAsync("not-a-hash", CancellationToken.None));
    }

    [Fact]
    public async Task RequestDelete_NeedsAReason()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Duplicate upload");
        await db.SaveChangesAsync();
        var page = new ManageModel(db, new DocRepoAuditService(db)) { Reason = "   " };
        ConfigurePage(page, Manager);

        var result = await page.OnPostRequestDeleteAsync(document.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Single(page.ModelState[nameof(ManageModel.Reason)]!.Errors);
        Assert.Empty(await db.DocumentDeleteRequests.ToListAsync());

        var withReason = new ManageModel(db, new DocRepoAuditService(db)) { Reason = "Uploaded twice", ReturnUrl = "/DocumentRepository/Documents?officeCategoryId=1" };
        ConfigurePage(withReason, Manager);
        var redirect = Assert.IsType<LocalRedirectResult>(await withReason.OnPostRequestDeleteAsync(document.Id, CancellationToken.None));
        Assert.Equal("/DocumentRepository/Documents?officeCategoryId=1", redirect.Url);
        Assert.Equal("Uploaded twice", (await db.DocumentDeleteRequests.SingleAsync()).Reason);
    }

    [Fact]
    public async Task MissingFiles_ChecksEveryDocumentNotJustOnePage()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var storage = new RecordingDocStorage();
        for (var i = 0; i < 30; i++)
        {
            var document = SeedDocument(db, $"Document {i}");
            if (i % 10 != 0) storage.Put(document.StoragePath, PdfBytes());
        }

        await db.SaveChangesAsync();
        var page = new MissingFilesModel(db, storage);
        ConfigurePage(page, Manager);

        await page.OnGetAsync(page: 1, pageSize: 25);

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(3, page.MissingCount);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task AotsViews_ListsWhoHasNotReadIt()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Training calendar");
        document.IsAots = true;
        db.Users.AddRange(
            new ApplicationUser { Id = "read", UserName = "read", FullName = "Has Read" },
            new ApplicationUser { Id = "unread", UserName = "unread", FullName = "Not Yet" },
            new ApplicationUser { Id = "off", UserName = "off", FullName = "Disabled", IsDisabled = true });
        db.DocRepoAotsViews.Add(new DocRepoAotsView { DocumentId = document.Id, UserId = "read", FirstViewedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var page = new AotsViewsModel(db);
        ConfigurePage(page, Manager);

        Assert.IsType<PageResult>(await page.OnGetAsync(document.Id, CancellationToken.None));

        Assert.Equal(new[] { "Has Read" }, page.Viewers.Select(v => v.FullName));
        Assert.Equal(new[] { "Not Yet" }, page.NotYetRead.Select(v => v.FullName));
        Assert.Equal(50, page.ReadPercent);
    }

    [Fact]
    public async Task Preview_OfAnAotsDocument_MarksItRead()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Circular for all officers");
        document.IsAots = true;
        await db.SaveChangesAsync();
        var page = CreateIndex(db, Reader);

        Assert.IsType<PartialViewResult>(await page.OnGetPreviewAsync(document.Id));

        var view = Assert.Single(await db.DocRepoAotsViews.AsNoTracking().ToListAsync());
        Assert.Equal((document.Id, ReaderId), (view.DocumentId, view.UserId));
    }

    [Theory]
    [InlineData("ocrmypdf exited with code 6: PriorOcrFoundError", "already has a text layer")]
    [InlineData("EncryptedPdfError: input file is encrypted", "password-protected")]
    [InlineData("something unexpected", "could not read this PDF")]
    public void OcrFailures_AreExplainedInPlainWords(string reason, string expected)
        => Assert.Contains(expected, DocRepoFormat.ExplainOcrFailure(reason));

    [Fact]
    public void Formatting_UsesShortMonthsAndReadableSizes()
    {
        Assert.Equal("14 Sep 2026", DocRepoFormat.Date(new DateOnly(2026, 9, 14)));
        Assert.Equal("No date", DocRepoFormat.Date((DateOnly?)null));
        Assert.Equal("18 KB", DocRepoFormat.FileSize(18_000));
        Assert.Equal("2.5 MB", DocRepoFormat.FileSize(2_621_440));
        Assert.Equal("1 document", DocRepoFormat.Count(1, "document"));
        Assert.InRange(DocRepoFormat.Hue(7), 1, 6);
        Assert.Equal(DocRepoFormat.Hue(1), DocRepoFormat.Hue(7));
    }

    private static Document Dated(Document document, int year, int office, int type)
    {
        document.DocumentDate = new DateOnly(year, 6, 1);
        document.OfficeCategoryId = office;
        document.DocumentCategoryId = type;
        return document;
    }

    private static IndexModel CreateIndex(ApplicationDbContext db, System.Security.Claims.ClaimsPrincipal user)
    {
        var page = new IndexModel(db, new DocumentSearchService(), Access(), Authorization(), new AotsUnreadService(db));
        ConfigurePage(page, user);
        return page;
    }
}
