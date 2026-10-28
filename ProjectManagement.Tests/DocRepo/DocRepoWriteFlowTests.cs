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
using ProjectManagement.Areas.DocumentRepository.Pages.Documents;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.Fakes;
using Xunit;
using static ProjectManagement.Tests.DocRepo.DocRepoTestSupport;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>Upload, metadata edit, ingestion and OCR re-queue: what is stored and what is audited.</summary>
public sealed class DocRepoWriteFlowTests
{
    [Fact]
    public async Task Upload_OfADuplicate_IsRejectedBeforeAnythingIsStored()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var bytes = PdfBytes("minutes");
        SeedDocument(db, "Minutes of TEC", sha256: Sha256Hex(bytes));
        await db.SaveChangesAsync();
        var storage = new RecordingDocStorage();
        var page = CreateUpload(db, storage, bytes);

        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Input.File", UploadModel.FileKey);
        Assert.Contains("Duplicate document detected", page.ModelState[UploadModel.FileKey]!.Errors.Single().ErrorMessage);
        Assert.Empty(storage.Saved);
        Assert.Equal(1, await db.Documents.CountAsync());
    }

    [Fact]
    public async Task Upload_OfATrashedDocument_RestoresTheRowAndDeletesItsOldFile()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var bytes = PdfBytes("statement of work");
        var trashed = SeedDocument(db, "Old SOW", isDeleted: true, sha256: Sha256Hex(bytes));
        var oldPath = trashed.StoragePath;
        await db.SaveChangesAsync();
        var storage = new RecordingDocStorage();
        storage.Put(oldPath, bytes);
        var page = CreateUpload(db, storage, bytes, subject: "Statement of work", tags: "sow, ird");

        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var restored = await db.Documents.Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag).SingleAsync();
        Assert.Equal(trashed.Id, restored.Id);
        Assert.False(restored.IsDeleted);
        Assert.Null(restored.DeletedAtUtc);
        Assert.Equal("Statement of work", restored.Subject);
        Assert.Equal(DocOcrStatus.Pending, restored.OcrStatus);
        Assert.Equal(new[] { "ird", "sow" }, restored.DocumentTags.Select(dt => dt.Tag.Name).OrderBy(n => n));
        Assert.Equal(storage.Saved.Single(), restored.StoragePath);
        Assert.Equal(new[] { oldPath }, storage.Deleted);
        Assert.Equal(new[] { "OcrQueued", "Reuploaded" }, (await db.DocRepoAudits.Select(a => a.EventType).ToListAsync()).OrderBy(e => e));
    }

    [Fact]
    public async Task Upload_OfANewDocument_SavesItsTagsAndAuditsTogether()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        db.Tags.Add(new Tag { Name = "tec", NormalizedName = "tec" });
        await db.SaveChangesAsync();
        var storage = new RecordingDocStorage();
        var page = CreateUpload(db, storage, PdfBytes("new"), subject: "TEC minutes", tags: "TEC, radar");

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(CancellationToken.None));

        var created = await db.Documents.Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag).SingleAsync();
        Assert.Equal(new[] { "radar", "tec" }, created.DocumentTags.Select(dt => dt.Tag.Name).OrderBy(n => n));
        Assert.Equal(2, await db.Tags.CountAsync());
        Assert.Single(storage.Saved);
        Assert.Equal(new[] { "OcrQueued", "Uploaded" }, (await db.DocRepoAudits.Select(a => a.EventType).ToListAsync()).OrderBy(e => e));
    }

    [Fact]
    public async Task Edit_RecordsWhatChanged()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Draft title");
        await db.SaveChangesAsync();

        var page = CreateEdit(db);
        await page.OnGetAsync(document.Id, CancellationToken.None);
        page.Input.Subject = "Final title";
        page.Input.Tags = "policy";

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(CancellationToken.None));

        var saved = await db.Documents.AsNoTracking().SingleAsync();
        Assert.Equal("Final title", saved.Subject);
        var audit = Assert.Single(await db.DocRepoAudits.AsNoTracking().ToListAsync());
        Assert.Equal(DocRepoAuditEventTypes.MetadataEdited, audit.EventType);
        using var details = JsonDocument.Parse(audit.DetailsJson);
        var changes = details.RootElement.GetProperty("changes");
        Assert.Equal("Draft title", changes.GetProperty("Subject")[0].GetString());
        Assert.Equal("Final title", changes.GetProperty("Subject")[1].GetString());
        Assert.Equal("policy", changes.GetProperty("Tags")[1].GetString());
    }

    [Fact]
    public async Task Edit_KeepsAnUnchangedTagWhileAddingAnother()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Tagged");
        var tec = new Tag { Name = "tec", NormalizedName = "tec" };
        document.DocumentTags.Add(new DocumentTag { DocumentId = document.Id, Tag = tec });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = CreateEdit(db);
        await page.OnGetAsync(document.Id, CancellationToken.None);
        page.Input.Tags = "tec, radar";

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(CancellationToken.None));

        var names = await db.DocumentTags.Where(dt => dt.DocumentId == document.Id).Select(dt => dt.Tag.Name).ToListAsync();
        Assert.Equal(new[] { "radar", "tec" }, names.OrderBy(n => n));
        Assert.Equal(2, await db.Tags.CountAsync());
    }

    [Fact]
    public async Task Edit_ShowsTagErrorsOnTheTagsField()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Tagged");
        await db.SaveChangesAsync();
        var page = CreateEdit(db);
        await page.OnGetAsync(document.Id, CancellationToken.None);
        page.Input.Tags = "a, b, c, d, e, f";

        Assert.IsType<PageResult>(await page.OnPostAsync(CancellationToken.None));

        Assert.Equal("Input.Tags", EditModel.TagsKey);
        Assert.Single(page.ModelState[EditModel.TagsKey]!.Errors);
        Assert.Empty(await db.DocRepoAudits.ToListAsync());
    }

    [Fact]
    public async Task Edit_DoesNotOverwriteSomeoneElsesSave()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var document = SeedDocument(db, "Original");
        await db.SaveChangesAsync();

        var page = CreateEdit(db);
        await page.OnGetAsync(document.Id, CancellationToken.None);

        // Someone else saves while this form is open.
        var tracked = await db.Documents.SingleAsync();
        tracked.Subject = "Their change";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        page.Input.Subject = "My change";
        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Contains("Someone else changed this document", page.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
        Assert.Equal("Their change", page.Input.Subject);
        Assert.Equal("Their change", (await db.Documents.AsNoTracking().SingleAsync()).Subject);
        Assert.Empty(await db.DocRepoAudits.ToListAsync());
    }

    [Fact]
    public async Task Ingestion_OfAPdfThatIsInTheTrash_LinksItInsteadOfFailing()
    {
        await using var db = CreateDb();
        SeedLookups(db);
        var bytes = PdfBytes("ffc attachment");
        var trashed = SeedDocument(db, "FFC sheet", isDeleted: true, isExternal: true, sha256: Sha256Hex(bytes));
        await db.SaveChangesAsync();
        var storage = new RecordingDocStorage();
        var service = new DocRepoIngestionService(
            db,
            storage,
            Options.Create(new DocRepoOptions { IngestionOfficeCategoryId = 1, IngestionDocumentCategoryId = 1 }),
            FakeClock.AtUtc(DateTimeOffset.UtcNow));

        var id = await service.IngestExternalPdfAsync(new MemoryStream(bytes), "sheet.pdf", "FFC", "42");

        Assert.Equal(trashed.Id, id);
        Assert.Empty(storage.Saved);
        Assert.Equal(1, await db.Documents.CountAsync());
        Assert.True((await db.Documents.AsNoTracking().SingleAsync()).IsDeleted);
        var link = Assert.Single(await db.DocRepoExternalLinks.AsNoTracking().ToListAsync());
        Assert.Equal(("FFC", "42"), (link.SourceModule, link.SourceItemId));
    }

    [Fact]
    public async Task RequeueAllFailed_OnlyQueuesTheDocuments()
    {
        await using var db = CreateDb();
        var failed = SeedDocument(db, "Scan");
        failed.OcrStatus = DocOcrStatus.Failed;
        failed.OcrFailureReason = "PriorOcrFoundError";
        failed.OcrLastTriedUtc = DateTimeOffset.UtcNow.AddHours(-1);
        var done = SeedDocument(db, "Done");
        done.OcrStatus = DocOcrStatus.Succeeded;
        await db.SaveChangesAsync();
        var service = new DocumentOcrService(db, new ThrowingOcrRunner(), new DocRepoAuditService(db));

        var queued = await service.RequeueAllFailedAsync(ManagerId);

        Assert.Equal(1, queued);
        var reloaded = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == failed.Id);
        Assert.Equal(DocOcrStatus.Pending, reloaded.OcrStatus);
        Assert.Null(reloaded.OcrFailureReason);
        Assert.Null(reloaded.OcrLastTriedUtc);
        Assert.Equal(DocOcrStatus.Succeeded, (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == done.Id)).OcrStatus);
        var audit = Assert.Single(await db.DocRepoAudits.AsNoTracking().ToListAsync());
        Assert.Equal(DocRepoAuditEventTypes.OcrRequeued, audit.EventType);
    }

    private static UploadModel CreateUpload(ApplicationDbContext db, RecordingDocStorage storage, byte[] bytes, string subject = "Upload", string? tags = null)
    {
        var page = new UploadModel(
            db,
            storage,
            new NoopFileScanner(),
            new DocRepoAuditService(db),
            Authorization(),
            Options.Create(new ProjectDocumentOptions { MaxSizeMb = 100 }),
            Options.Create(new DocumentUploadRateLimitOptions()),
            Access());
        ConfigurePage(page, Manager);
        page.Input = new UploadModel.InputModel
        {
            Subject = subject,
            OfficeCategoryId = 1,
            DocumentCategoryId = 1,
            Tags = tags,
            File = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Input.File", "document.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            }
        };
        return page;
    }

    private static EditModel CreateEdit(ApplicationDbContext db)
    {
        var page = new EditModel(db, Authorization(), new DocRepoAuditService(db));
        ConfigurePage(page, Manager);
        return page;
    }

    private sealed class ThrowingOcrRunner : IDocumentOcrRunner
    {
        public Task<OcrRunResult> RunAsync(Document document, CancellationToken ct = default)
            => throw new InvalidOperationException("Re-queue must not run OCR in the request.");
    }
}
