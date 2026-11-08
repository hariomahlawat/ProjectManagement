using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Application.Security;
using ProjectManagement.Models;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Services.Storage;
using Xunit;

namespace ProjectManagement.Tests.ItProjects;

// SECTION: Upload safety (content checks, name limits, file/record consistency), listing,
// download scoping and soft delete. Mirrors the Industry partners attachment safety tests.
public class ItProjectAttachmentServiceTests
{
    public const string PdfType = "application/pdf";
    public const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PptxType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    public const string DocType = "application/msword";

    public static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%test\n");
    private static readonly byte[] OleBytes = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00 };

    // ---- validator -------------------------------------------------------------------------

    [Theory]
    [InlineData("ppp.pdf", PdfType)]
    [InlineData("aon.docx", DocxType)]
    [InlineData("costing.xlsx", XlsxType)]
    [InlineData("tec.pptx", PptxType)]
    [InlineData("old.doc", DocType)]
    public void ContentMatches_AcceptsGenuineFiles(string fileName, string contentType)
    {
        using var content = new MemoryStream(BytesFor(fileName));

        Assert.True(ItProjectTestDb.AttachmentValidator().ContentMatches(contentType, content));
        Assert.Equal(0, content.Position);
    }

    [Theory]
    [InlineData(PdfType)]
    [InlineData(DocxType)]
    [InlineData(XlsxType)]
    [InlineData(DocType)]
    public void ContentMatches_RejectsBytesThatAreNotTheDeclaredType(string contentType)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));

        Assert.False(ItProjectTestDb.AttachmentValidator().ContentMatches(contentType, content));
    }

    [Fact]
    public void ContentMatches_RejectsAZipThatIsNotAWordDocument()
    {
        using var content = new MemoryStream(BuildZip(("readme.txt", "hello")));

        Assert.False(ItProjectTestDb.AttachmentValidator().ContentMatches(DocxType, content));
    }

    [Fact]
    public void ContentMatches_RejectsASpreadsheetDeclaredAsAWordDocument()
    {
        using var content = new MemoryStream(BytesFor("costing.xlsx"));

        Assert.False(ItProjectTestDb.AttachmentValidator().ContentMatches(DocxType, content));
    }

    [Theory]
    [InlineData("photo.png", "image/png", 100, "Only PDF, Word, Excel and PowerPoint")]
    [InlineData("ppp.exe", PdfType, 100, "extension does not match")]
    [InlineData("ppp.pdf", PdfType, 0, "empty")]
    [InlineData("ppp.pdf", PdfType, 11L * 1024 * 1024, "10 MB")]
    [InlineData("", PdfType, 100, "Choose a file")]
    public void Validate_RejectsWhatTheModuleDoesNotAccept(string fileName, string contentType, long length, string expectedFragment)
    {
        var errors = ItProjectTestDb.AttachmentValidator().Validate(fileName, contentType, length);

        Assert.Contains(errors, error => error.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsFileNamesLongerThanTheStoredColumn()
    {
        var errors = ItProjectTestDb.AttachmentValidator().Validate(new string('a', 300) + ".pdf", PdfType, 100);

        Assert.Contains(errors, error => error.Contains("260", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsAGenuineDeclaration()
        => Assert.Empty(ItProjectTestDb.AttachmentValidator().Validate("SO 14-2026.pdf", PdfType, 2048));

    [Fact]
    public void AcceptList_NamesTheAllowedExtensionsOnly()
        => Assert.Equal(".doc,.docx,.pdf,.ppt,.pptx,.xls,.xlsx", ItProjectAttachmentValidator.AcceptList);

    // ---- upload ----------------------------------------------------------------------------

    [Fact]
    public async Task Upload_StoresTheFileWritesTheRowAndAudits()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        await AddUserAsync(db, "ito-1", "Maj Rathi");

        var (result, attachmentId) = await db.Attachments().UploadAsync(id, FormFile(@"C:\Users\someone\IT PPP 09-2025.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor("ito-1"));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var view = Assert.Single(await db.Attachments().ListAsync(id));
        Assert.Equal(attachmentId, view.Id);
        Assert.Equal("IT PPP 09-2025.pdf", view.FileName); // the client path is not kept
        Assert.Equal(PdfType, view.ContentType);
        Assert.Equal(PdfBytes.Length, view.SizeBytes);
        Assert.Equal("Maj Rathi", view.UploadedByName);
        Assert.Equal(ItProjectTestDb.Now.UtcDateTime, view.UploadedAtUtc);

        var key = Assert.Single(db.Storage.Saved);
        Assert.StartsWith($"it-projects/{id}/", key, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", key, StringComparison.Ordinal);
        Assert.Equal(PdfBytes, db.Storage.Bytes[key]);

        var audit = Assert.Single(db.Audit.Entries, entry => entry.Action == "ItProjects.AttachmentUploaded");
        Assert.Equal("ito-1", audit.UserId);
        Assert.Equal("IT PPP 09-2025.pdf", audit.Data["FileName"]);
        Assert.Equal(id.ToString(), audit.Data["ItProjectId"]);
    }

    [Fact]
    public async Task Upload_WithMislabelledContent_IsRejectedAndNothingIsStored()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var (result, _) = await db.Attachments().UploadAsync(id, FormFile("fake.pdf", PdfType, Encoding.UTF8.GetBytes("not a pdf")), ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
        Assert.Contains(ItProjectAttachmentValidator.ContentMismatchMessage, result.Errors);
        Assert.Empty(db.Storage.Saved);
        await using var context = db.NewContext();
        Assert.False(await context.ItProjectAttachments.AnyAsync());
        Assert.DoesNotContain(db.Audit.Entries, entry => entry.Action.StartsWith("ItProjects.Attachment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_OfADisallowedType_IsRejectedBeforeTheBytesAreRead()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var (result, _) = await db.Attachments().UploadAsync(id, FormFile("photo.png", "image/png", new byte[] { 0x89, 0x50 }), ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
        Assert.Contains(ItProjectAttachmentValidator.TypeNotAllowedMessage, result.Errors);
        Assert.Empty(db.Storage.Saved);
    }

    [Fact]
    public async Task Upload_WhenTheScannerRejectsTheFile_StoresNothing()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();

        var (result, _) = await db.Attachments(scanner: new ItProjectTestDb.FailingScanner()).UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
        Assert.Contains(ItProjectAttachmentService.ScanFailedMessage, result.Errors);
        Assert.Empty(db.Storage.Saved);
    }

    [Fact]
    public async Task Upload_WhenTheStorageRejectsTheFile_ReportsItAndWritesNoRow()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var service = new ItProjectAttachmentService(db.NewContext(), new RejectingStorage(), ItProjectTestDb.AttachmentValidator(), new ItProjectTestDb.PassScanner(), db.Audit, db.Clock);

        var (result, _) = await service.UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());

        Assert.False(result.Succeeded);
        Assert.Contains(FileSystemItProjectAttachmentStorage.RejectedMessage, result.Errors);
        await using var context = db.NewContext();
        Assert.False(await context.ItProjectAttachments.AnyAsync());
    }

    [Fact]
    public async Task Upload_WhenTheRecordCannotBeSaved_RemovesTheStoredFile()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var service = db.Attachments(db.NewContext(new FailingSaveInterceptor()));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor()));

        var savedKey = Assert.Single(db.Storage.Saved);
        Assert.Equal(new[] { savedKey }, db.Storage.Deleted);
        Assert.DoesNotContain(db.Audit.Entries, entry => entry.Action == "ItProjects.AttachmentUploaded");
    }

    [Fact]
    public async Task Upload_ToADeletedOrUnknownProject_IsNotFound()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        Assert.True((await db.Commands().DeleteAsync(id, rowVersion, ItProjectTestDb.Actor())).Succeeded);

        var (deleted, _) = await db.Attachments().UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());
        var (unknown, _) = await db.Attachments().UploadAsync(9999, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());

        Assert.Contains(ItProjectCommandService.NotFoundMessage, deleted.Errors);
        Assert.Contains(ItProjectCommandService.NotFoundMessage, unknown.Errors);
        Assert.Empty(db.Storage.Saved);
    }

    // ---- list, open, delete ------------------------------------------------------------------

    [Fact]
    public async Task List_ShowsLiveFilesNewestFirstAndNotTheDeletedOnes()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (_, first) = await db.Attachments().UploadAsync(id, FormFile("a-first.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());
        db.Clock.Set(ItProjectTestDb.Now.AddMinutes(5));
        var (_, second) = await db.Attachments().UploadAsync(id, FormFile("b-second.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());
        db.Clock.Set(ItProjectTestDb.Now.AddMinutes(10));
        var (_, third) = await db.Attachments().UploadAsync(id, FormFile("c-third.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());
        Assert.True((await db.Attachments().DeleteAsync(id, second, ItProjectTestDb.Actor())).Succeeded);

        var list = await db.Attachments().ListAsync(id);

        Assert.Equal(new[] { third, first }, list.Select(view => view.Id));
    }

    [Fact]
    public async Task Open_ReturnsTheFileOfThisProjectOnly()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (otherId, _) = await db.SeedAsync("Other", "IT PPP 10/2025");
        var (_, attachmentId) = await db.Attachments().UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());

        var own = await db.Attachments().OpenAsync(id, attachmentId);
        var crossProject = await db.Attachments().OpenAsync(otherId, attachmentId);
        var unknown = await db.Attachments().OpenAsync(id, Guid.NewGuid());

        Assert.NotNull(own);
        Assert.Equal("ppp.pdf", own!.FileName);
        Assert.Equal(PdfType, own.ContentType);
        using var buffer = new MemoryStream();
        await own.Content.CopyToAsync(buffer);
        Assert.Equal(PdfBytes, buffer.ToArray());
        Assert.Null(crossProject);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Open_OfADeletedAttachmentOrADeletedProject_IsNull()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, rowVersion) = await db.SeedAsync();
        var (_, kept) = await db.Attachments().UploadAsync(id, FormFile("kept.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());
        var (_, removed) = await db.Attachments().UploadAsync(id, FormFile("removed.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());
        Assert.True((await db.Attachments().DeleteAsync(id, removed, ItProjectTestDb.Actor())).Succeeded);

        Assert.Null(await db.Attachments().OpenAsync(id, removed));
        Assert.NotNull(await db.Attachments().OpenAsync(id, kept));

        Assert.True((await db.Commands().DeleteAsync(id, rowVersion, ItProjectTestDb.Actor())).Succeeded);
        Assert.Null(await db.Attachments().OpenAsync(id, kept));
    }

    [Fact]
    public async Task Open_WhenTheFileIsMissingOnDisk_IsNull()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (_, attachmentId) = await db.Attachments().UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());
        db.Storage.Bytes.Clear();

        Assert.Null(await db.Attachments().OpenAsync(id, attachmentId));
    }

    [Fact]
    public async Task Delete_SoftDeletesKeepsTheFileAndAudits()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (_, attachmentId) = await db.Attachments().UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor("ito-1"));
        db.Clock.Set(ItProjectTestDb.Now.AddHours(1));

        var result = await db.Attachments().DeleteAsync(id, attachmentId, ItProjectTestDb.Actor("hod-1"));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        await using var context = db.NewContext();
        var row = await context.ItProjectAttachments.SingleAsync(attachment => attachment.Id == attachmentId);
        Assert.True(row.IsDeleted);
        Assert.Equal("hod-1", row.DeletedByUserId);
        Assert.Equal(ItProjectTestDb.Now.UtcDateTime.AddHours(1), row.DeletedAtUtc);
        Assert.Empty(db.Storage.Deleted); // the bytes stay for a later restore
        Assert.Single(db.Audit.Entries, entry => entry.Action == "ItProjects.AttachmentDeleted");

        var again = await db.Attachments().DeleteAsync(id, attachmentId, ItProjectTestDb.Actor("hod-1"));
        Assert.Contains(ItProjectAttachmentService.NotFoundMessage, again.Errors);
    }

    [Fact]
    public async Task Delete_IsBoundToTheRouteProject()
    {
        await using var db = await ItProjectTestDb.CreateAsync();
        var (id, _) = await db.SeedAsync();
        var (otherId, _) = await db.SeedAsync("Other", "IT PPP 10/2025");
        var (_, attachmentId) = await db.Attachments().UploadAsync(id, FormFile("ppp.pdf", PdfType, PdfBytes), ItProjectTestDb.Actor());

        var result = await db.Attachments().DeleteAsync(otherId, attachmentId, ItProjectTestDb.Actor());

        Assert.Contains(ItProjectAttachmentService.NotFoundMessage, result.Errors);
        Assert.Single(await db.Attachments().ListAsync(id));
    }

    // ---- file system storage -----------------------------------------------------------------

    [Fact]
    public async Task FileSystemStorage_RoundTripsUnderTheProjectFolderAndDeletes()
    {
        var root = Path.Combine(Path.GetTempPath(), "itp-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileSystemItProjectAttachmentStorage(new TestUploadRootProvider(root), new PassFileSecurityValidator());

            var key = await storage.SaveAsync(7, "IT PPP 09-2025.PDF", new MemoryStream(PdfBytes));

            Assert.StartsWith("it-projects/7/", key, StringComparison.Ordinal);
            Assert.EndsWith(".pdf", key, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, "it-projects", "7", Path.GetFileName(key))));
            await using (var stream = await storage.OpenReadAsync(key))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                Assert.Equal(PdfBytes, buffer.ToArray());
            }

            await storage.DeleteAsync(key);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "it-projects", "7")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileSystemStorage_WhenTheSecurityCheckFails_LeavesNoFileBehind()
    {
        var root = Path.Combine(Path.GetTempPath(), "itp-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileSystemItProjectAttachmentStorage(new TestUploadRootProvider(root), new RejectingFileSecurityValidator());

            await Assert.ThrowsAsync<ItProjectAttachmentRejectedException>(() => storage.SaveAsync(7, "ppp.pdf", new MemoryStream(PdfBytes)));

            var folder = Path.Combine(root, "it-projects", "7");
            Assert.True(!Directory.Exists(folder) || !Directory.EnumerateFiles(folder).Any());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileSystemStorage_RefusesAKeyOutsideTheRoot()
    {
        var storage = new FileSystemItProjectAttachmentStorage(new TestUploadRootProvider(Path.GetTempPath()), new StrictPathValidator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("../../secrets.txt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync("it-projects/../../x.pdf"));
    }

    // ---- helpers -----------------------------------------------------------------------------

    public static IFormFile FormFile(string fileName, string contentType, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "AttachmentFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    public static byte[] BytesFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => PdfBytes,
        ".doc" or ".xls" or ".ppt" => OleBytes,
        ".docx" => BuildZip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>")),
        ".xlsx" => BuildZip(("[Content_Types].xml", "<Types/>"), ("xl/workbook.xml", "<workbook/>")),
        ".pptx" => BuildZip(("[Content_Types].xml", "<Types/>"), ("ppt/presentation.xml", "<p:presentation/>")),
        _ => throw new ArgumentOutOfRangeException(nameof(fileName))
    };

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static async Task AddUserAsync(ItProjectTestDb db, string id, string fullName)
    {
        await using var context = db.NewContext();
        context.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), FullName = fullName });
        await context.SaveChangesAsync();
    }

    private sealed class RejectingStorage : IItProjectAttachmentStorage
    {
        public Task<string> SaveAsync(int itProjectId, string originalFileName, Stream content, CancellationToken cancellationToken = default)
            => throw new ItProjectAttachmentRejectedException(FileSystemItProjectAttachmentStorage.RejectedMessage);

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new DbUpdateException("Simulated database failure.");
    }

    private sealed class TestUploadRootProvider(string root) : IUploadRootProvider
    {
        public string RootPath => root;
        public string GetProjectRoot(int projectId) => root;
        public string GetProjectPhotosRoot(int projectId) => root;
        public string GetProjectDocumentsRoot(int projectId) => root;
        public string GetProjectCommentsRoot(int projectId) => root;
        public string GetProjectVideosRoot(int projectId) => root;
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => root;
    }

    private sealed class PassFileSecurityValidator : IFileSecurityValidator
    {
        public void ValidateRelativePath(string relativePath)
        {
        }

        public Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RejectingFileSecurityValidator : IFileSecurityValidator
    {
        public void ValidateRelativePath(string relativePath)
        {
        }

        public Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class StrictPathValidator : IFileSecurityValidator
    {
        public void ValidateRelativePath(string relativePath)
        {
            if (relativePath.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Path traversal.");
            }
        }

        public Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
