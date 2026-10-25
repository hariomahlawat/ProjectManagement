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
    public async Task CreateAsync_RejectsRepeatBuildProject()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var project = new Project
            {
                Name = "Repeat Build project",
                CreatedByUserId = "test-user",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                IsBuild = true
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            var record = ValidRecord(clock, "IPR-REPEAT-BUILD-CREATE");
            record.ProjectId = project.Id;

            var exception = await Assert.ThrowsAsync<IprValidationException>(() => service.CreateAsync(record));

            Assert.Equal(IprValidationCode.ProjectNotAvailable, exception.Code);
            Assert.Contains("Repeat Build", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await db.IprRecords.CountAsync());
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
    public async Task UpdateAsync_RejectsRepeatBuildProject()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var project = new Project
            {
                Name = "Repeat Build project",
                CreatedByUserId = "test-user",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                IsBuild = true
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            var created = await service.CreateAsync(ValidRecord(clock, "IPR-REPEAT-BUILD-UPDATE"));
            var update = ValidRecord(clock, created.IprFilingNumber);
            update.Id = created.Id;
            update.RowVersion = created.RowVersion;
            update.ProjectId = project.Id;

            var exception = await Assert.ThrowsAsync<IprValidationException>(() => service.UpdateAsync(update));

            Assert.Equal(IprValidationCode.ProjectNotAvailable, exception.Code);
            Assert.Contains("Repeat Build", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null((await db.IprRecords.AsNoTracking().SingleAsync()).ProjectId);
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
    public async Task DeleteAttachmentAsync_TrashesTheRepositoryCopyWhenNothingElseLinksIt()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 2, 9, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var (record, attachment) = await SeedAttachmentAsync(db, clock, "DOC-001");
            var onlyIpr = SeedRepositoryDocument(db, isExternal: true, ("IPR", attachment.Id.ToString()));
            var shared = SeedRepositoryDocument(db, isExternal: true, ("IPR", attachment.Id.ToString()), ("FFC", "77"));
            var uploaded = SeedRepositoryDocument(db, isExternal: false, ("IPR", attachment.Id.ToString()));
            var otherAttachment = SeedRepositoryDocument(db, isExternal: true, ("IPR", "999"));
            await db.SaveChangesAsync();

            var deleted = await service.DeleteAttachmentAsync(attachment.Id, attachment.RowVersion, " editor-1 ");

            Assert.True(deleted);
            var trashed = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == onlyIpr);
            Assert.True(trashed.IsDeleted);
            Assert.False(trashed.IsActive);
            Assert.Equal("editor-1", trashed.DeletedByUserId);
            Assert.Equal(clock.UtcNow.UtcDateTime, trashed.DeletedAtUtc);
            Assert.Contains("IPR attachment", trashed.DeleteReason);

            // A copy another module still uses, a directly uploaded document and another
            // attachment's copy stay in the repository.
            Assert.False((await db.Documents.AsNoTracking().SingleAsync(d => d.Id == shared)).IsDeleted);
            Assert.False((await db.Documents.AsNoTracking().SingleAsync(d => d.Id == uploaded)).IsDeleted);
            Assert.False((await db.Documents.AsNoTracking().SingleAsync(d => d.Id == otherAttachment)).IsDeleted);

            var remainingLinks = await db.DocRepoExternalLinks.AsNoTracking().ToListAsync();
            Assert.DoesNotContain(remainingLinks, l => l.SourceModule == "IPR" && l.SourceItemId == attachment.Id.ToString());
            Assert.Contains(remainingLinks, l => l.DocumentId == shared && l.SourceModule == "FFC");
            Assert.Contains(remainingLinks, l => l.DocumentId == otherAttachment);

            var audit = await db.DocRepoAudits.AsNoTracking().SingleAsync();
            Assert.Equal(onlyIpr, audit.DocumentId);
            Assert.Equal(DocRepoSourceLinks.SourceRemovedEvent, audit.EventType);
            Assert.Equal("editor-1", audit.ActorUserId);
            Assert.Contains("\"sourceModule\":\"IPR\"", audit.DetailsJson);
            Assert.Empty(await db.IprAttachments.AsNoTracking().ToListAsync());
            Assert.Single(await db.IprRecords.AsNoTracking().Where(r => r.Id == record.Id).ToListAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task DeleteAsync_TrashesTheRepositoryCopiesOfEveryAttachment()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 3, 9, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var (record, first) = await SeedAttachmentAsync(db, clock, "DOC-002");
            var second = new IprAttachment
            {
                IprRecordId = record.Id,
                StorageKey = "missing/second.pdf",
                OriginalFileName = "second.pdf",
                ContentType = "application/pdf",
                FileSize = 10,
                UploadedByUserId = "user-1",
                UploadedAtUtc = clock.UtcNow,
                RowVersion = new byte[] { 2 }
            };
            db.IprAttachments.Add(second);
            await db.SaveChangesAsync();
            var firstCopy = SeedRepositoryDocument(db, isExternal: true, ("IPR", first.Id.ToString()));
            var secondCopy = SeedRepositoryDocument(db, isExternal: true, ("IPR", second.Id.ToString()));
            await db.SaveChangesAsync();

            var deleted = await service.DeleteAsync(record.Id, record.RowVersion);

            Assert.True(deleted);
            var documents = await db.Documents.AsNoTracking().Where(d => d.Id == firstCopy || d.Id == secondCopy).ToListAsync();
            Assert.Equal(2, documents.Count);
            Assert.All(documents, d =>
            {
                Assert.True(d.IsDeleted);
                Assert.Equal("system", d.DeletedByUserId);
                Assert.Contains("DOC-002", d.DeleteReason);
            });
            Assert.Empty(await db.DocRepoExternalLinks.AsNoTracking().ToListAsync());
            Assert.Equal(2, await db.DocRepoAudits.AsNoTracking().CountAsync());
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    private static async Task<(IprRecord Record, IprAttachment Attachment)> SeedAttachmentAsync(
        ApplicationDbContext db,
        FakeClock clock,
        string filingNumber)
    {
        var record = ValidRecord(clock, filingNumber);
        record.RowVersion = new byte[] { 1 };
        db.IprRecords.Add(record);
        await db.SaveChangesAsync();

        var attachment = new IprAttachment
        {
            IprRecordId = record.Id,
            StorageKey = "missing/evidence.pdf",
            OriginalFileName = "evidence.pdf",
            ContentType = "application/pdf",
            FileSize = 10,
            UploadedByUserId = "user-1",
            UploadedAtUtc = clock.UtcNow,
            RowVersion = new byte[] { 1 }
        };
        db.IprAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return (record, attachment);
    }

    private static Guid SeedRepositoryDocument(ApplicationDbContext db, bool isExternal, params (string Module, string ItemId)[] links)
    {
        var id = Guid.NewGuid();
        db.Documents.Add(new ProjectManagement.Data.DocRepo.Document
        {
            Id = id,
            Subject = "Evidence",
            OfficeCategoryId = 1,
            DocumentCategoryId = 1,
            OriginalFileName = "evidence.pdf",
            FileSizeBytes = 10,
            Sha256 = id.ToString("N"),
            StoragePath = $"docs/{id:N}.pdf",
            IsExternal = isExternal,
            CreatedByUserId = "system",
            CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        foreach (var (module, itemId) in links)
        {
            db.DocRepoExternalLinks.Add(new ProjectManagement.Data.DocRepo.DocRepoExternalLink
            {
                Id = Guid.NewGuid(),
                DocumentId = id,
                SourceModule = module,
                SourceItemId = itemId,
                CreatedAtUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
        }

        return id;
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

    [Theory]
    [InlineData(IprStatus.Rejected)]
    [InlineData(IprStatus.Withdrawn)]
    public async Task UpdateAsync_KeepsClosedStatusAndClearsGrantDate(IprStatus closedStatus)
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var granted = ValidRecord(clock, "IPR-900");
            granted.Status = IprStatus.Granted;
            granted.GrantedAtUtc = granted.FiledAtUtc;
            var created = await service.CreateAsync(granted);

            var update = ValidRecord(clock, "IPR-900");
            update.Id = created.Id;
            update.Status = closedStatus;
            update.GrantedAtUtc = granted.GrantedAtUtc;
            update.RowVersion = created.RowVersion;

            var updated = await service.UpdateAsync(update);

            Assert.NotNull(updated);
            Assert.Equal(closedStatus, updated!.Status);
            Assert.Null(updated.GrantedAtUtc);
        }
        finally
        {
            CleanupRoot(root);
        }
    }

    [Fact]
    public async Task CreateAsync_DuplicateOfClosedRecord_NamesItsStatus()
    {
        await using var db = CreateDbContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (service, root) = CreateService(db, clock);

        try
        {
            var rejected = ValidRecord(clock, "IPR-901");
            rejected.Status = IprStatus.Rejected;
            db.IprRecords.Add(rejected);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<IprValidationException>(
                () => service.CreateAsync(ValidRecord(clock, "IPR-901")));

            Assert.Equal(IprValidationCode.DuplicateFilingNumber, ex.Code);
            Assert.Contains("rejected IPR record", ex.Message);
            Assert.Contains("Filter the register by Rejected", ex.Message);
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

    internal static MemoryStream CreateValidPdfStream()
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
