using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Tests;

public sealed class IprWriteServiceTests
{
    [Fact]
    public async Task CreateAsync_RequiresFiledDateWhenStatusNotFilingUnderProcess()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = new IprRecord
            {
                IprFilingNumber = "IPR-001",
                Status = IprStatus.Filed,
                Type = IprType.Patent
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(record));
            Assert.Equal("Filed date is required once the record is not under filing.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsFutureFiledDate()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = new IprRecord
            {
                IprFilingNumber = "IPR-002",
                Status = IprStatus.Filed,
                Type = IprType.Patent,
                FiledAtUtc = clock.UtcNow.AddDays(1)
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(record));
            Assert.Equal("Filed date cannot be in the future.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateFilingNumbersWithinType()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            db.IprRecords.Add(new IprRecord
            {
                IprFilingNumber = "IPR-100",
                Type = IprType.Copyright,
                Status = IprStatus.FilingUnderProcess
            });
            await db.SaveChangesAsync();

            var duplicate = new IprRecord
            {
                IprFilingNumber = " IPR-100 ",
                Type = IprType.Copyright,
                Status = IprStatus.FilingUnderProcess
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(duplicate));
            Assert.Equal("An IPR with the same filing number and type already exists.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenUniqueConstraintThrownDuringSave_RaisesInvalidOperation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new UniqueConstraintViolationDbContext(options);
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = new IprRecord
            {
                IprFilingNumber = "IPR-UNIQUE",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(record));
            Assert.Equal("An IPR with the same filing number and type already exists.", ex.Message);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_StoresNullProjectWhenProjectIdIsZero()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var record = new IprRecord
            {
                IprFilingNumber = "IPR-200",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess,
                ProjectId = 0
            };

            var created = await service.CreateAsync(record);
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
    public async Task AddAttachmentAsync_PersistsAttachmentWhenContentTypeAllowed()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero));
        var options = new IprAttachmentOptions
        {
            MaxFileSizeBytes = 1024,
            AllowedContentTypes = new List<string>
            {
                "application/pdf"
            }
        };

        var root = CreateTempRoot();
        try
        {
            var optionWrapper = Options.Create(options);
            var storage = CreateStorage(root, optionWrapper);
            var ingestion = new StubDocRepoIngestionService();
            var service = new IprWriteService(db, clock, storage, optionWrapper, ingestion, NullLogger<IprWriteService>.Instance);

            var record = new IprRecord
            {
                IprFilingNumber = "ATT-001",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess
            };
            db.IprRecords.Add(record);
            await db.SaveChangesAsync();

            await using var content = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            var attachment = await service.AddAttachmentAsync(record.Id, content, " specification.pdf ", "application/pdf", " user-1 ");

            Assert.Equal("user-1", attachment.UploadedByUserId);
            Assert.Equal(clock.UtcNow, attachment.UploadedAtUtc);
            Assert.Equal("application/pdf", attachment.ContentType);
            Assert.Equal("specification.pdf", attachment.OriginalFileName);
            Assert.True(attachment.FileSize > 0);

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
        var options = new IprAttachmentOptions
        {
            MaxFileSizeBytes = 1024,
            AllowedContentTypes = new List<string>
            {
                "application/pdf"
            }
        };

        var root = CreateTempRoot();
        try
        {
            var optionWrapper = Options.Create(options);
            var storage = CreateStorage(root, optionWrapper);
            var ingestion = new StubDocRepoIngestionService();
            var service = new IprWriteService(db, clock, storage, optionWrapper, ingestion, NullLogger<IprWriteService>.Instance);

            var record = new IprRecord
            {
                IprFilingNumber = "ATT-002",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess
            };
            db.IprRecords.Add(record);
            await db.SaveChangesAsync();

            await using var content = new MemoryStream(new byte[] { 1, 2, 3 });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAttachmentAsync(record.Id, content, "notes.txt", "text/plain", "user-2"));

            Assert.Equal("Attachments of type 'text/plain' are not allowed.", ex.Message);
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
        var options = new IprAttachmentOptions
        {
            MaxFileSizeBytes = 5,
            AllowedContentTypes = new List<string>
            {
                "application/pdf"
            }
        };

        var root = CreateTempRoot();
        try
        {
            var optionWrapper = Options.Create(options);
            var storage = CreateStorage(root, optionWrapper);
            var ingestion = new StubDocRepoIngestionService();
            var service = new IprWriteService(db, clock, storage, optionWrapper, ingestion, NullLogger<IprWriteService>.Instance);

            var record = new IprRecord
            {
                IprFilingNumber = "ATT-003",
                Type = IprType.Patent,
                Status = IprStatus.FilingUnderProcess
            };
            db.IprRecords.Add(record);
            await db.SaveChangesAsync();

            await using var content = new MemoryStream(Enumerable.Repeat((byte)1, 16).ToArray());
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAttachmentAsync(record.Id, content, "oversized.pdf", "application/pdf", "user-3"));

            Assert.Equal("Attachment exceeds maximum allowed size of 5 bytes.", ex.Message);
            Assert.Equal(0, await db.IprAttachments.CountAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
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
        var options = Options.Create(new IprAttachmentOptions());
        var root = CreateTempRoot();
        var storage = CreateStorage(root, options);
        var ingestion = new StubDocRepoIngestionService();
        var service = new IprWriteService(db, clock, storage, options, ingestion, NullLogger<IprWriteService>.Instance);
        return (service, root);
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

    private sealed class StubDocRepoIngestionService : IDocRepoIngestionService
    {
        public Task<Guid> IngestExternalPdfAsync(Stream pdfStream, string originalFileName, string sourceModule, string sourceItemId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }
}
