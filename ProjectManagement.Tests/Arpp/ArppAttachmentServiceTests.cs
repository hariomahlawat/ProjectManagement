using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.DocRepo;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppAttachmentServiceTests
{
    [Fact]
    public async Task UploadOrReplaceAsync_PersistsOneAttachment_WithoutChangingStructuredRows()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.Entries.Add(CreateEntry());
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var storage = new FakeStorage();
        var audit = new FakeAuditService();
        var service = CreateService(db, storage, audit);
        var file = CreatePdfFormFile("ARPP-2026-27.pdf");

        var result = await service.UploadOrReplaceAsync(
            issue.Id,
            file,
            "user-1",
            "User One");

        Assert.True(result.Success);
        var attachment = await db.ArppAttachments.SingleAsync();
        Assert.Equal(issue.Id, attachment.ArppIssueId);
        Assert.Equal("ARPP-2026-27.pdf", attachment.OriginalFileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(64, attachment.Sha256.Length);
        Assert.Single(await db.ArppEntries.ToListAsync());
        var updatedIssue = await db.ArppIssues.SingleAsync();
        Assert.Equal("user-1", updatedIssue.UpdatedByUserId);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero), updatedIssue.UpdatedAtUtc);
        Assert.Contains("Arpp.AttachmentUploaded", audit.Actions);
    }

    [Fact]
    public async Task UploadOrReplaceAsync_ReplacesMetadata_AndDeletesPreviousStoredFile()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/1/old.pdf",
            OriginalFileName = "old.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var storage = new FakeStorage { NextSha256 = new string('b', 64) };
        var service = CreateService(db, storage, new FakeAuditService());

        var result = await service.UploadOrReplaceAsync(
            issue.Id,
            CreatePdfFormFile("replacement.pdf"),
            "user-2",
            "User Two");

        Assert.True(result.Success);
        var attachment = await db.ArppAttachments.SingleAsync();
        Assert.Equal("replacement.pdf", attachment.OriginalFileName);
        Assert.Equal(new string('b', 64), attachment.Sha256);
        Assert.Contains("arpp/1/old.pdf", storage.DeletedKeys);
    }

    [Fact]
    public async Task UploadOrReplaceAsync_KeepsPreviousPdf_WhenPublishedSnapshotStillReferencesIt()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/1/published.pdf",
            OriginalFileName = "published.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        AddPublishedSnapshot(issue, "arpp/1/published.pdf");
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var storage = new FakeStorage { NextSha256 = new string('b', 64) };
        var service = CreateService(db, storage, new FakeAuditService());

        var result = await service.UploadOrReplaceAsync(
            issue.Id,
            CreatePdfFormFile("working-replacement.pdf"),
            "user-2",
            "User Two");

        Assert.True(result.Success);
        Assert.DoesNotContain("arpp/1/published.pdf", storage.DeletedKeys);
        Assert.Equal("arpp/1/published.pdf", (await db.ArppPublishedIssues.SingleAsync()).AttachmentStorageKey);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMetadata_AndStoredFile()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/1/document.pdf",
            OriginalFileName = "document.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();
        var attachmentId = issue.Attachment.Id;

        var storage = new FakeStorage();
        var service = CreateService(db, storage, new FakeAuditService());
        var result = await service.DeleteAsync(
            issue.Id,
            attachmentId,
            "user-1",
            "User One");

        Assert.True(result.Success);
        Assert.Empty(await db.ArppAttachments.ToListAsync());
        var updatedIssue = await db.ArppIssues.SingleAsync();
        Assert.Equal("user-1", updatedIssue.UpdatedByUserId);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero), updatedIssue.UpdatedAtUtc);
        Assert.Contains("arpp/1/document.pdf", storage.DeletedKeys);
    }


    [Fact]
    public async Task DeleteAsync_KeepsPdf_WhenPublishedSnapshotStillReferencesIt()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/1/published.pdf",
            OriginalFileName = "published.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        AddPublishedSnapshot(issue, "arpp/1/published.pdf");
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var storage = new FakeStorage();
        var service = CreateService(db, storage, new FakeAuditService());
        var result = await service.DeleteAsync(
            issue.Id,
            issue.Attachment.Id,
            "user-1",
            "User One");

        Assert.True(result.Success);
        Assert.Empty(await db.ArppAttachments.ToListAsync());
        Assert.DoesNotContain("arpp/1/published.pdf", storage.DeletedKeys);
        Assert.Equal("arpp/1/published.pdf", (await db.ArppPublishedIssues.SingleAsync()).AttachmentStorageKey);
    }


    [Fact]
    public async Task UploadAndDelete_AreBlockedWhenIssueIsVerified()
    {
        await using var db = CreateContext();
        var issue = CreateIssue();
        issue.IsVerified = true;
        issue.VerifiedAtUtc = DateTimeOffset.UtcNow;
        issue.VerifiedByUserId = "verifier";
        issue.Attachment = new ArppAttachment
        {
            StorageKey = "arpp/1/document.pdf",
            OriginalFileName = "document.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
            Sha256 = new string('a', 64),
            UploadedByUserId = "seed",
            UploadedAtUtc = DateTimeOffset.UtcNow
        };
        db.ArppIssues.Add(issue);
        await db.SaveChangesAsync();

        var storage = new FakeStorage();
        var service = CreateService(db, storage, new FakeAuditService());

        var upload = await service.UploadOrReplaceAsync(
            issue.Id,
            CreatePdfFormFile("replacement.pdf"),
            "user-1",
            "User One");
        var delete = await service.DeleteAsync(
            issue.Id,
            issue.Attachment.Id,
            "user-1",
            "User One");

        Assert.False(upload.Success);
        Assert.False(delete.Success);
        Assert.Contains("verified and locked", upload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified and locked", delete.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.DeletedKeys);
        Assert.Single(await db.ArppAttachments.ToListAsync());
    }

    private static ArppAttachmentService CreateService(
        ApplicationDbContext db,
        FakeStorage storage,
        FakeAuditService audit)
        => new(
            db,
            storage,
            new FakeDocRepoIngestionService(),
            audit,
            new FixedClock(new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero)),
            Options.Create(new ArppAttachmentOptions()),
            NullLogger<ArppAttachmentService>.Instance);

    private static FormFile CreatePdfFormFile(string fileName)
    {
        var bytes = "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF"u8.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "UploadFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static ArppIssue CreateIssue()
        => new()
        {
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "ARPP 2026-27",
            IssueDate = new DateOnly(2026, 4, 10),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

    private static void AddPublishedSnapshot(ArppIssue issue, string storageKey)
    {
        issue.PublishedSnapshot = new ArppPublishedIssue
        {
            RevisionNumber = 1,
            FinancialYearStart = issue.FinancialYearStart,
            Kind = issue.Kind,
            IssueSequence = issue.IssueSequence,
            Name = issue.Name,
            IssueDate = issue.IssueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = storageKey,
            AttachmentOriginalFileName = "published.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64)
        };
    }

    private static ArppEntry CreateEntry()
        => new()
        {
            SortOrder = 1,
            SerialNumber = null,
            PppNumber = null,
            ProjectReference = "Project",
            Category = ArppCategory.Delisted,
            IpaCost = 5_000_000m,
            Cfa = "Comdt SDD",
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeStorage : IArppAttachmentStorage
    {
        public string NextSha256 { get; set; } = new string('a', 64);
        public List<string> DeletedKeys { get; } = [];
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public async Task<ArppStoredAttachment> SaveAsync(
            long issueId,
            string originalFileName,
            string contentType,
            long declaredLength,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var key = $"arpp/{issueId}/{Guid.NewGuid():N}.pdf";
            _files[key] = buffer.ToArray();
            return new ArppStoredAttachment(
                key,
                Path.GetFileName(originalFileName),
                "application/pdf",
                buffer.Length,
                NextSha256);
        }

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(_files.TryGetValue(storageKey, out var bytes)
                ? new MemoryStream(bytes)
                : new MemoryStream("%PDF-test"u8.ToArray()));

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(storageKey);
            _files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDocRepoIngestionService : IDocRepoIngestionService
    {
        public Task<Guid> IngestExternalPdfAsync(
            Stream pdfStream,
            string originalFileName,
            string sourceModule,
            string sourceItemId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeAuditService : IAuditService
    {
        public List<string> Actions { get; } = [];

        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }
}
