using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Areas.DocumentRepository.Pages.Documents;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.DocRepo;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>Search shortcuts, covers, the upload queue's handler and the shelves.</summary>
public sealed class DocRepoLibraryFeatureTests
{
    // SECTION: Search shortcuts
    [Fact]
    public void Shortcuts_AreTakenOutOfTheSearchWords()
    {
        var parsed = DocRepoQueryTokens.Parse("radar type:minutes office:\"Test Range\" tag:sow year:2025 from:DRDO trials");

        Assert.Equal("radar trials", parsed.Text);
        Assert.Equal("minutes", parsed.Type);
        Assert.Equal("Test Range", parsed.Office);
        Assert.Equal("sow", parsed.Tag);
        Assert.Equal(2025, parsed.Year);
        Assert.Equal("DRDO", parsed.From);
        Assert.True(parsed.HasShortcuts);
    }

    [Theory]
    [InlineData("radar trials")]
    [InlineData("year:soon")] // not a year: left as words
    [InlineData("http://type:x")] // only at the start of a word
    [InlineData("")]
    public void TextWithoutShortcuts_IsLeftAlone(string query)
    {
        var parsed = DocRepoQueryTokens.Parse(query);
        Assert.False(parsed.HasShortcuts);
    }

    [Fact]
    public void TypedNames_MatchOneCategoryOnly()
    {
        var options = new[] { (1L, "Letter"), (2L, "Letter of intent"), (3L, "Minutes"), (4L, "Test Range"), (5L, "Range office") };

        Assert.Equal(1, IndexModel.Resolve(options, "letter").Id); // exact beats "starts with"
        Assert.Equal(3, IndexModel.Resolve(options, "min").Id);
        Assert.Equal(4, IndexModel.Resolve(options, "test").Id);
        Assert.Equal(5, IndexModel.Resolve(options, "range").Id); // "starts with" beats "contains"
        Assert.Null(IndexModel.Resolve(options, "let").Id); // two types start with it: ambiguous
        Assert.Null(IndexModel.Resolve(options, "policy").Id);
    }

    [Fact]
    public async Task Shortcuts_RedirectToTheOrdinaryFilters()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        db.DocumentCategories.Add(new DocumentCategory { Id = 2, Name = "Minutes" });
        await db.SaveChangesAsync();
        var page = CreateIndex(db, Manager);
        page.Q = "radar type:min office:nowhere year:2025";

        var redirect = Assert.IsType<LocalRedirectResult>(await page.OnGetAsync());

        Assert.Contains("documentCategoryId=2", redirect.Url);
        Assert.Contains("year=2025", redirect.Url);
        // An office nobody has stays a search word, so nothing silently disappears.
        Assert.Contains("q=radar%20nowhere", redirect.Url);
    }

    [Fact]
    public void PrefixQuery_KeepsOnlyWordsAndMakesTheLastAPrefix()
    {
        Assert.Equal("radar & tri:*", DocumentSearchService.PrefixQuery("radar  tri"));
        Assert.Equal("a & b:*", DocumentSearchService.PrefixQuery("a' | !b"));
        Assert.Null(DocumentSearchService.PrefixQuery(" &|! "));
        Assert.Equal("sonar", DocumentSearchService.FirstSearchWord("\"of\" -draft sonar trials"));
    }

    // SECTION: Covers
    [Fact]
    public async Task Cover_FollowsTheSameAccessRuleAsThePdf()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var hidden = SeedDocument(db, "Deactivated memo", isActive: false);
        var visible = SeedDocument(db, "Circular");
        await db.SaveChangesAsync();
        var covers = new FixedCovers(Path.GetTempFileName());

        var reader = new CoverModel(db, Access(), covers);
        ConfigurePage(reader, Reader);
        Assert.IsType<NotFoundResult>(await reader.OnGetAsync(hidden.Id, CancellationToken.None));
        Assert.Equal(0, covers.Calls);

        var file = Assert.IsType<PhysicalFileResult>(await reader.OnGetAsync(visible.Id, CancellationToken.None));
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("private, no-store", reader.Response.Headers.CacheControl.ToString());

        var manager = new CoverModel(db, Access(), covers);
        ConfigurePage(manager, Manager);
        Assert.IsType<PhysicalFileResult>(await manager.OnGetAsync(hidden.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cover_MissingImage_Is404SoThePageShowsTheColouredCover()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Circular");
        await db.SaveChangesAsync();

        var page = new CoverModel(db, Access(), new FixedCovers(null));
        ConfigurePage(page, Reader);

        Assert.IsType<NotFoundResult>(await page.OnGetAsync(document.Id, CancellationToken.None));
    }

    // SECTION: Upload queue (one PDF per request, answered in JSON)
    [Fact]
    public async Task UploadJson_AddsThePdf_AndAnswersWithItsReaderLink()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        await db.SaveChangesAsync();
        var page = CreateUpload(db, PdfBytes("queued"));

        var json = Serialize(await page.OnPostJsonAsync(CancellationToken.None));

        var saved = await db.Documents.SingleAsync();
        Assert.Contains("\"ok\":true", json);
        Assert.Contains($"\"id\":\"{saved.Id}\"", json);
        Assert.Contains("\"restored\":false", json);
        // The form post's toast is not set for queued uploads.
        Assert.False(page.TempData.ContainsKey("ToastMessage"));
    }

    [Fact]
    public async Task UploadJson_Duplicate_Is422WithTheMessageForTheFile()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var bytes = PdfBytes("again");
        SeedDocument(db, "Existing", sha256: Sha256Hex(bytes));
        await db.SaveChangesAsync();
        var page = CreateUpload(db, bytes);

        var result = Assert.IsType<JsonResult>(await page.OnPostJsonAsync(CancellationToken.None));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"File\":[", json);
        Assert.Contains("Existing", json);
        Assert.Equal(1, await db.Documents.CountAsync());
    }

    // SECTION: Shelves
    [Fact]
    public async Task Cards_KeepTheRememberedOrder_AndHideWhatTheUserCannotSee()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var first = SeedDocument(db, "First");
        var hidden = SeedDocument(db, "Hidden", isActive: false);
        var second = SeedDocument(db, "Second");
        await db.SaveChangesAsync();
        var page = CreateIndex(db, Reader);

        var partial = Assert.IsType<PartialViewResult>(await page.OnGetCardsAsync($"{second.Id},{hidden.Id},not-a-guid,{first.Id}"));

        // The partial gets the page as its model (ViewData does not reach the nested cover partial).
        var shelf = Assert.IsType<ShelfItemsVm>(partial.Model);
        Assert.Same(page, shelf.Page);
        Assert.Equal(new[] { "Second", "First" }, shelf.Items.Select(item => item.Subject));
    }

    [Fact]
    public async Task Preview_CarriesPermissionsAndTheSearchWordsInItsModel()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Circular");
        await db.SaveChangesAsync();
        var page = CreateIndex(db, Manager);
        page.Q = "radar";

        var partial = Assert.IsType<PartialViewResult>(await page.OnGetPreviewAsync(document.Id));

        var preview = Assert.IsType<DocumentPreviewVm>(partial.Model);
        Assert.True(preview.CanEdit);
        Assert.True(preview.CanManage);
        Assert.Equal("radar", preview.Query);
    }

    [Fact]
    public async Task Home_FillsShelves_WithUnreadAotsFirstForThisUser()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var aots = SeedDocument(db, "Circular for all");
        aots.IsAots = true;
        var read = SeedDocument(db, "Already read");
        read.IsAots = true;
        db.DocRepoAotsViews.Add(new DocRepoAotsView { DocumentId = read.Id, UserId = ReaderId, FirstViewedAtUtc = DateTime.UtcNow });
        SeedDocument(db, "Plain letter");
        await db.SaveChangesAsync();
        var page = CreateIndex(db, Reader);

        await page.OnGetAsync();

        Assert.True(page.IsHome);
        Assert.Equal(new[] { "Circular for all" }, page.NewForYou.Select(item => item.Subject));
        Assert.Equal(3, page.RecentlyAdded.Count);
        Assert.Equal("covers", page.ViewMode);
    }

    [Fact]
    public void Views_DefaultToCoversWhenBrowsing_AndOldCardLinksStillWork()
    {
        var page = new IndexModel(CreateDb(), new DocumentSearchService(), Access(), Authorization(), new AotsUnreadService(CreateDb()));
        Assert.Equal("covers", page.ViewMode);
        page.View = "cards";
        Assert.Equal("covers", page.ViewMode);
        page.View = "timeline";
        Assert.True(page.IsTimelineView);
        page.View = "nonsense";
        Assert.Null(page.ViewValue);
    }

    [Fact]
    public void Reader_FindsThePagesOfAScanFromItsStoredText()
    {
        const string text = "Cover page\fThe vendor asked for more time\fNothing here\fVENDOR reply and sanction";

        Assert.Equal(new[] { 2, 4 }, ReaderModel.PagesContaining(text, "vendor"));
        Assert.Equal(new[] { 2, 4 }, ReaderModel.PagesContaining(text, "\"vendor\" -draft type:letter"));
        Assert.Equal(new[] { 4 }, ReaderModel.PagesContaining(text, "sanction"));
        // No page breaks: the page is unknown rather than guessed.
        Assert.Empty(ReaderModel.PagesContaining("one page about the vendor", "vendor"));
        Assert.Empty(ReaderModel.PagesContaining(text, "of an"));
        Assert.Empty(ReaderModel.PagesContaining(null, "vendor"));
    }

    [Fact]
    public void UserKey_IsStableAndDoesNotShowTheId()
    {
        var key = DocRepoFormat.UserKey("1b2c3d4e-user");
        Assert.Equal(key, DocRepoFormat.UserKey("1b2c3d4e-user"));
        Assert.Equal(16, key.Length);
        Assert.DoesNotContain("user", key);
        Assert.Equal("anon", DocRepoFormat.UserKey(null));
    }

    // SECTION: Helpers
    private static string Serialize(IActionResult result) => JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value);

    private static IndexModel CreateIndex(ApplicationDbContext db, System.Security.Claims.ClaimsPrincipal user)
    {
        var page = new IndexModel(db, new DocumentSearchService(), Access(), Authorization(), new AotsUnreadService(db));
        ConfigurePage(page, user);
        return page;
    }

    private static UploadModel CreateUpload(ApplicationDbContext db, byte[] bytes)
    {
        var page = new UploadModel(db, new RecordingDocStorage(), new NoopFileScanner(), new DocRepoAuditService(db), Authorization(),
            Options.Create(new ProjectDocumentOptions { MaxSizeMb = 100 }),
            Options.Create(new DocumentUploadRateLimitOptions()), Access());
        ConfigurePage(page, Manager);
        page.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(page.HttpContext, new NullTempDataProvider());
        page.Input = new UploadModel.InputModel
        {
            Subject = "Queued PDF",
            OfficeCategoryId = 1,
            DocumentCategoryId = 1,
            File = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Input.File", "queued.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            }
        };
        return page;
    }

    private sealed class NullTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public System.Collections.Generic.IDictionary<string, object> LoadTempData(HttpContext context) => new System.Collections.Generic.Dictionary<string, object>();
        public void SaveTempData(HttpContext context, System.Collections.Generic.IDictionary<string, object> values) { }
    }

    private sealed class FixedCovers : IDocRepoCoverService
    {
        private readonly string? _path;
        public FixedCovers(string? path) => _path = path;
        public int Calls { get; private set; }

        public Task<string?> GetCoverAsync(Document document, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_path);
        }

        public void DeleteCover(Guid documentId) { }
    }
}
