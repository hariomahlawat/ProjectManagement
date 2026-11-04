using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Security;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.IndustryPartners;
using ProjectManagement.Tests.Fakes;
using Xunit;
using static ProjectManagement.Tests.IndustryPartners.IndustryPartnerTestDb;

namespace ProjectManagement.Tests.IndustryPartners;

// SECTION: Upload content checks, file-name limits and file/record consistency
public sealed class IndustryPartnerAttachmentSafetyTests
{
    private const string PdfType = "application/pdf";
    private const string PngType = "image/png";
    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%test\n");
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };

    [Theory]
    [InlineData("brochure.pdf", PdfType)]
    [InlineData("logo.png", PngType)]
    [InlineData("profile.docx", DocxType)]
    public void ValidateContent_AcceptsGenuineFiles(string fileName, string contentType)
    {
        var validator = CreateValidator();
        using var content = new MemoryStream(BytesFor(fileName));

        validator.ValidateContent(contentType, content);

        Assert.Equal(0, content.Position);
    }

    [Theory]
    [InlineData(PdfType)]
    [InlineData(PngType)]
    [InlineData(DocxType)]
    [InlineData("application/msword")]
    public void ValidateContent_RejectsContentThatDoesNotMatchTheDeclaredType(string contentType)
    {
        var validator = CreateValidator();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));

        Assert.Throws<IndustryPartnerValidationException>(() => validator.ValidateContent(contentType, content));
    }

    [Fact]
    public void ValidateContent_RejectsAZipThatIsNotAWordDocument()
    {
        var validator = CreateValidator();
        using var content = new MemoryStream(BuildZip(("readme.txt", "hello")));

        Assert.Throws<IndustryPartnerValidationException>(() => validator.ValidateContent(DocxType, content));
    }

    [Fact]
    public void Validate_RejectsFileNamesLongerThanTheStoredColumn()
    {
        var validator = CreateValidator();
        var longName = new string('a', 300) + ".pdf";

        var exception = Assert.Throws<IndustryPartnerValidationException>(() =>
            validator.Validate(longName, PdfType, 100));

        Assert.Contains(exception.Errors["File"], message => message.Contains("260"));
    }

    [Fact]
    public async Task Upload_WithMislabelledContent_IsRejectedAndNothingIsStored()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Org");
        var context = db.NewContext();
        var storage = new RecordingStorage();
        var manager = CreateManager(context, storage);

        await Assert.ThrowsAsync<IndustryPartnerValidationException>(() =>
            manager.UploadAsync(partnerId, FormFile("fake.pdf", PdfType, Encoding.UTF8.GetBytes("not a pdf")), User("u1")));

        Assert.Empty(storage.Saved);
        Assert.False(await context.IndustryPartnerAttachments.AnyAsync());
    }

    [Fact]
    public async Task Upload_StoresOnlyTheFileNameAndTheFile()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Org");
        var context = db.NewContext();
        var storage = new RecordingStorage();
        var manager = CreateManager(context, storage);

        await manager.UploadAsync(partnerId, FormFile(@"C:\Users\someone\brochure.pdf", PdfType, PdfBytes), User("u1"));

        var attachment = await context.IndustryPartnerAttachments.SingleAsync();
        Assert.Equal("brochure.pdf", attachment.OriginalFileName);
        Assert.Equal(PdfBytes.Length, attachment.SizeBytes);
        Assert.Single(storage.Saved);
    }

    [Fact]
    public async Task Upload_WhenTheRecordCannotBeSaved_RemovesTheStoredFile()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Org");
        var context = db.NewContext(new FailingSaveInterceptor());
        var storage = new RecordingStorage();
        var manager = CreateManager(context, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            manager.UploadAsync(partnerId, FormFile("brochure.pdf", PdfType, PdfBytes), User("u1")));

        var savedKey = Assert.Single(storage.Saved);
        Assert.Equal(new[] { savedKey }, storage.Deleted);
    }

    [Fact]
    public async Task Delete_WhenTheFileCannotBeRemoved_StillCompletes()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Org");
        var context = db.NewContext();
        var storage = new RecordingStorage();
        var manager = CreateManager(context, storage);
        var attachmentId = await manager.UploadAsync(partnerId, FormFile("brochure.pdf", PdfType, PdfBytes), User("u1"));
        storage.FailDeletes = true;

        await manager.DeleteAsync(partnerId, attachmentId, User("u1"));

        Assert.False(await context.IndustryPartnerAttachments.AnyAsync());
    }

    [Fact]
    public async Task FileSystemStorage_WhenTheSecurityCheckFails_LeavesNoFileBehind()
    {
        var root = Path.Combine(Path.GetTempPath(), "ip-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileSystemIndustryPartnerAttachmentStorage(
                new TestUploadRootProvider(root),
                new RejectingFileSecurityValidator());

            await Assert.ThrowsAsync<IndustryPartnerValidationException>(() =>
                storage.SaveAsync(7, "brochure.pdf", new MemoryStream(PdfBytes)));

            var partnerFolder = Path.Combine(root, "industry-partners", "7");
            Assert.True(!Directory.Exists(partnerFolder) || !Directory.EnumerateFiles(partnerFolder).Any());
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
    public async Task DownloadHandler_WhenTheAttachmentIsMissing_ReturnsNotFound()
    {
        await using var db = await IndustryPartnerTestDb.CreateAsync();
        var partnerId = await db.AddPartnerAsync("Org");
        var context = db.NewContext();
        var manager = CreateManager(context, new RecordingStorage());
        var page = IndustryPartnerJdpAuthorizationTests.CreatePage(
            new IndustryPartnerService(context), User("u1"), manager);

        var result = await page.OnGetDownloadAttachmentAsync(partnerId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // SECTION: Helpers
    private static IndustryPartnerAttachmentValidator CreateValidator() =>
        new(Options.Create(new ProjectDocumentOptions { MaxSizeMb = 10 }));

    private static IndustryPartnerAttachmentManager CreateManager(
        ApplicationDbContext context,
        IIndustryPartnerAttachmentStorage storage) =>
        new(context, storage, CreateValidator(), new PassScanner());

    private static byte[] BytesFor(string fileName) => Path.GetExtension(fileName) switch
    {
        ".pdf" => PdfBytes,
        ".png" => PngBytes,
        ".docx" => BuildZip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>")),
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

    private static IFormFile FormFile(string fileName, string contentType, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private sealed class RecordingStorage : IIndustryPartnerAttachmentStorage
    {
        public List<string> Saved { get; } = new();
        public List<string> Deleted { get; } = new();
        public bool FailDeletes { get; set; }

        public Task<string> SaveAsync(int partnerId, string originalFileName, Stream content, CancellationToken cancellationToken = default)
        {
            var key = $"industry-partners/{partnerId}/{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}";
            Saved.Add(key);
            return Task.FromResult(key);
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            if (FailDeletes)
            {
                throw new IOException("Simulated locked file.");
            }

            Deleted.Add(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class PassScanner : IFileScanner
    {
        public Task ScanOrThrowAsync(Stream fileStream, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RejectingFileSecurityValidator : IFileSecurityValidator
    {
        public void ValidateRelativePath(string relativePath)
        {
        }

        public Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new DbUpdateException("Simulated database failure.");
    }
}
