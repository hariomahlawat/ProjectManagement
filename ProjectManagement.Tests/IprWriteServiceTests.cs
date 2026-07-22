using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests;

public sealed class IprWriteServiceTests
{
    [Fact]
    public async Task CreateAsync_RequiresTitle()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = ValidRecord(clock, "IPR-000");
            record.Title = "  ";

            var ex = await Assert.ThrowsAsync<IprValidationException>(() => service.CreateAsync(record));
            Assert.Equal(IprValidationCode.TitleRequired, ex.Code);
            Assert.Equal("Title is required.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_RequiresFiledDate()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = ValidRecord(clock, "IPR-001");
            record.FiledAtUtc = null;

            var ex = await Assert.ThrowsAsync<IprValidationException>(() => service.CreateAsync(record));
            Assert.Equal(IprValidationCode.FiledDateRequired, ex.Code);
            Assert.Equal("Filed date is required.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsFutureFiledDateByIstCalendarDate()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 20, 0, 0, TimeSpan.Zero)); // 02 Jan 2024, 01:30 IST
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = ValidRecord(clock, "IPR-002");
            record.FiledAtUtc = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero);

            var ex = await Assert.ThrowsAsync<IprValidationException>(() => service.CreateAsync(record));
            Assert.Equal(IprValidationCode.FiledDateInFuture, ex.Code);
            Assert.Equal("Filed date cannot be in the future.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_AcceptsCurrentIstDateBefore0530Ist()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 20, 0, 0, TimeSpan.Zero)); // 02 Jan 2024, 01:30 IST
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = ValidRecord(clock, "IPR-003");
            record.FiledAtUtc = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);

            var created = await service.CreateAsync(record);

            Assert.Equal(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero), created.FiledAtUtc);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateFilingNumbersCaseInsensitivelyWithinType()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            db.IprRecords.Add(ValidRecord(clock, "ipr-100", IprType.Copyright));
            await db.SaveChangesAsync();

            var duplicate = ValidRecord(clock, " IPR-100 ", IprType.Copyright);

            var ex = await Assert.ThrowsAsync<IprValidationException>(() => service.CreateAsync(duplicate));
            Assert.Equal(IprValidationCode.DuplicateFilingNumber, ex.Code);
            Assert.Equal("An IPR record with the same filing number and type already exists.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenUniqueConstraintThrownDuringSave_RaisesTypedValidationError()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new UniqueConstraintViolationDbContext(options);
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var ex = await Assert.ThrowsAsync<IprValidationException>(() =>
                service.CreateAsync(ValidRecord(clock, "IPR-UNIQUE")));

            Assert.Equal(IprValidationCode.DuplicateFilingNumber, ex.Code);
            Assert.Equal("An IPR record with the same filing number and type already exists.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_NormalizesFilingNumberAndStoresNullProjectWhenProjectIdIsZero()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = ValidRecord(clock, "  ipr   200  ");
            record.ProjectId = 0;

            var created = await service.CreateAsync(record);

            Assert.Equal("IPR 200", created.IprFilingNumber);
            Assert.Null(created.ProjectId);
            var stored = await db.IprRecords.AsNoTracking().SingleAsync(r => r.Id == created.Id);
            Assert.Null(stored.ProjectId);
        }
        finally
        {
            CleanupRoot(root);
        }
    }


    [Fact]
    public async Task CreateAsync_AssignsExistingProject()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var project = new Project
            {
                Name = "AURA",
                CreatedByUserId = "test-user",
                LifecycleStatus = ProjectLifecycleStatus.Active
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            var record = ValidRecord(clock, "IPR-PROJECT-001");
            record.ProjectId = project.Id;

            var created = await service.CreateAsync(record);

            Assert.Equal(project.Id, created.ProjectId);
            var stored = await db.IprRecords.AsNoTracking().SingleAsync(item => item.Id == created.Id);
            Assert.Equal(project.Id, stored.ProjectId);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownProject()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = ValidRecord(clock, "IPR-PROJECT-UNKNOWN");
            record.ProjectId = 987654;

            var exception = await Assert.ThrowsAsync<IprValidationException>(() => service.CreateAsync(record));

            Assert.Equal(IprValidationCode.ProjectNotAvailable, exception.Code);
            Assert.Equal("The selected project is no longer available. Select another project.", exception.Message);
            Assert.Equal(0, await db.IprRecords.CountAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsSoftDeletedProject()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var project = new Project
            {
                Name = "Deleted project",
                CreatedByUserId = "test-user",
                IsDeleted = true
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            var record = ValidRecord(clock, "IPR-PROJECT-002");
            record.ProjectId = project.Id;

            var exception = await Assert.ThrowsAsync<IprValidationException>(() => service.CreateAsync(record));

            Assert.Equal(IprValidationCode.ProjectNotAvailable, exception.Code);
            Assert.Equal("The selected project is no longer available. Select another project.", exception.Message);
            Assert.Equal(0, await db.IprRecords.CountAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task UpdateAsync_AssignsReassignsAndRemovesProject()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var firstProject = new Project
            {
                Name = "First project",
                CreatedByUserId = "test-user",
                LifecycleStatus = ProjectLifecycleStatus.Active
            };
            var archivedProject = new Project
            {
                Name = "Archived project",
                CreatedByUserId = "test-user",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                IsArchived = true
            };
            db.Projects.AddRange(firstProject, archivedProject);
            await db.SaveChangesAsync();

            var created = await service.CreateAsync(ValidRecord(clock, "IPR-PROJECT-003"));

            var assign = ValidRecord(clock, created.IprFilingNumber);
            assign.Id = created.Id;
            assign.RowVersion = created.RowVersion;
            assign.ProjectId = firstProject.Id;
            var assigned = await service.UpdateAsync(assign);
            Assert.NotNull(assigned);
            Assert.Equal(firstProject.Id, assigned!.ProjectId);

            var reassign = ValidRecord(clock, assigned.IprFilingNumber);
            reassign.Id = assigned.Id;
            reassign.RowVersion = assigned.RowVersion;
            reassign.ProjectId = archivedProject.Id;
            var reassigned = await service.UpdateAsync(reassign);
            Assert.NotNull(reassigned);
            Assert.Equal(archivedProject.Id, reassigned!.ProjectId);

            var remove = ValidRecord(clock, reassigned.IprFilingNumber);
            remove.Id = reassigned.Id;
            remove.RowVersion = reassigned.RowVersion;
            remove.ProjectId = null;
            var unassigned = await service.UpdateAsync(remove);
            Assert.NotNull(unassigned);
            Assert.Null(unassigned!.ProjectId);

            var stored = await db.IprRecords.AsNoTracking().SingleAsync(item => item.Id == created.Id);
            Assert.Null(stored.ProjectId);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task AddAttachmentAsync_PersistsValidatedPdfUsingShortStorageKey()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero));
        var options = CreateAttachmentOptions(maxFileSizeBytes: 64 * 1024);
        var root = CreateTempRoot();

        try
        {
            var storage = CreateStorage(root, options);
            var service = new IprWriteService(
                db,
                clock,
                storage,
                options,
                new StubDocRepoIngestionService(),
                NullLogger<IprWriteService>.Instance);

            var record = ValidRecord(clock, "ATT-001");
            db.IprRecords.Add(record);
            await db.SaveChangesAsync();

            await using var content = CreateValidPdfStream();
            var attachment = await service.AddAttachmentAsync(
                record.Id,
                content,
                " specification.pdf ",
                "application/pdf",
                " user-1 ");

            Assert.Equal("user-1", attachment.UploadedByUserId);
            Assert.Equal(clock.UtcNow, attachment.UploadedAtUtc);
            Assert.Equal("application/pdf", attachment.ContentType);
            Assert.Equal("specification.pdf", attachment.OriginalFileName);
            Assert.EndsWith(".pdf", attachment.StorageKey, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("specification", attachment.StorageKey, StringComparison.OrdinalIgnoreCase);
            Assert.True(attachment.StorageKey.Length < 100);

            var saved = await db.IprAttachments.AsNoTracking().SingleAsync(a => a.Id == attachment.Id);
            Assert.Equal(record.Id, saved.IprRecordId);
            Assert.Equal("application/pdf", saved.ContentType);
            Assert.Equal("specification.pdf", saved.OriginalFileName);

            var path = ResolvePath(root, attachment.StorageKey);
            Assert.True(File.Exists(path));
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task AddAttachmentAsync_RejectsDisallowedContentType()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 4, 10, 8, 0, 0, TimeSpan.Zero));
        var options = CreateAttachmentOptions();
        var root = CreateTempRoot();

        try
        {
            var storage = CreateStorage(root, options);
            var service = new IprWriteService(
                db,
                clock,
                storage,
                options,
                new StubDocRepoIngestionService(),
                NullLogger<IprWriteService>.Instance);

            var record = ValidRecord(clock, "ATT-002");
            db.IprRecords.Add(record);
            await db.SaveChangesAsync();

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("not a PDF"));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAttachmentAsync(record.Id, content, "notes.txt", "text/plain", "user-2"));

            Assert.Equal("Only PDF attachments are allowed.", ex.Message);
            Assert.Equal(0, await db.IprAttachments.CountAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task AddAttachmentAsync_RejectsForgedPdfContent()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 4, 10, 8, 0, 0, TimeSpan.Zero));
        var options = CreateAttachmentOptions();
        var root = CreateTempRoot();

        try
        {
            var storage = CreateStorage(root, options);
            var service = new IprWriteService(
                db,
                clock,
                storage,
                options,
                new StubDocRepoIngestionService(),
                NullLogger<IprWriteService>.Instance);

            var record = ValidRecord(clock, "ATT-003");
            db.IprRecords.Add(record);
            await db.SaveChangesAsync();

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("plain text pretending to be a PDF"));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAttachmentAsync(record.Id, content, "forged.pdf", "application/pdf", "user-3"));

            Assert.Equal("The selected file is not a valid PDF document.", ex.Message);
            Assert.Equal(0, await db.IprAttachments.CountAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task AddAttachmentAsync_RejectsFilesExceedingSizeLimit()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 5, 12, 0, 0, TimeSpan.Zero));
        var options = CreateAttachmentOptions(maxFileSizeBytes: 5);
        var root = CreateTempRoot();

        try
        {
            var storage = CreateStorage(root, options);
            var service = new IprWriteService(
                db,
                clock,
                storage,
                options,
                new StubDocRepoIngestionService(),
                NullLogger<IprWriteService>.Instance);

            var record = ValidRecord(clock, "ATT-004");
            db.IprRecords.Add(record);
            await db.SaveChangesAsync();

            await using var content = new MemoryStream(Enumerable.Repeat((byte)1, 16).ToArray());
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAttachmentAsync(record.Id, content, "oversized.pdf", "application/pdf", "user-4"));

            Assert.Equal("Attachment exceeds maximum allowed size of 5 bytes.", ex.Message);
            Assert.Equal(0, await db.IprAttachments.CountAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    private static IprRecord ValidRecord(FakeClock clock, string filingNumber, IprType type = IprType.Patent)
    {
        return new IprRecord
        {
            IprFilingNumber = filingNumber,
            Title = "Test IPR record",
            Type = type,
            Status = IprStatus.Filed,
            FiledAtUtc = new DateTimeOffset(
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
        };
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class UniqueConstraintViolationDbContext : ApplicationDbContext
    {
        private const string UniqueConstraintName = "UX_IprRecords_FilingNumber_Type";

        public UniqueConstraintViolationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            => Task.FromException<int>(CreateException());

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
            => throw CreateException();

        private static DbUpdateException CreateException()
        {
            var inner = new FakeDbException($"duplicate key value violates unique constraint \"{UniqueConstraintName}\"");
            return new DbUpdateException("duplicate key value violates unique constraint", inner);
        }
    }

    private sealed class FakeDbException : DbException
    {
        public FakeDbException(string message) : base(message)
        {
        }
    }

    private static (IprWriteService Service, string Root) CreateService(ApplicationDbContext db, FakeClock clock)
    {
        var options = CreateAttachmentOptions();
        var root = CreateTempRoot();
        var storage = CreateStorage(root, options);
        var service = new IprWriteService(
            db,
            clock,
            storage,
            options,
            new StubDocRepoIngestionService(),
            NullLogger<IprWriteService>.Instance);
        return (service, root);
    }

    private static IOptions<IprAttachmentOptions> CreateAttachmentOptions(long maxFileSizeBytes = 1024 * 1024)
    {
        return Options.Create(new IprAttachmentOptions
        {
            MaxFileSizeBytes = maxFileSizeBytes,
            AllowedContentTypes = new List<string> { "application/pdf" },
            AllowedExtensions = new List<string> { ".pdf" }
        });
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "ipr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ResolvePath(string root, string storageKey)
    {
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(root, relative);
    }

    private static IprAttachmentStorage CreateStorage(string root, IOptions<IprAttachmentOptions> options)
    {
        var provider = new TestUploadRootProvider(root);
        var resolver = new UploadPathResolver(provider);
        return new IprAttachmentStorage(provider, resolver, options);
    }

    private static MemoryStream CreateValidPdfStream()
    {
        var stream = new MemoryStream();
        var offsets = new List<long>();

        static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);
        void Write(string value)
        {
            var bytes = Bytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream"
        };

        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(stream.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = stream.Position;
        Write($"xref\n0 {objects.Length + 1}\n");
        Write("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:0000000000} 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        stream.Position = 0;
        return stream;
    }

    private sealed class StubDocRepoIngestionService : IDocRepoIngestionService
    {
        public Task<Guid> IngestExternalPdfAsync(
            Stream pdfStream,
            string originalFileName,
            string sourceModule,
            string sourceItemId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }
}
