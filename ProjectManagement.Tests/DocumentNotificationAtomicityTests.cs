using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Project document notifications are written in the same transaction as the document change.
/// These run on SQLite, which has real transactions and savepoints (the in-memory provider has
/// neither): a notification commits with its change, a failing notification never undoes the
/// change or the approval that triggered it, and an interrupted change leaves neither.
/// </summary>
public sealed class DocumentNotificationAtomicityTests : IAsyncLifetime
{
    private const int ProjectId = 1;
    private const int StageId = 10;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prism-doc-outbox-{Guid.NewGuid():N}");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        // SQLite enforces the user foreign keys the in-memory provider ignores.
        foreach (var userId in new[] { "creator", "requestor", "approver", "po", "hod" })
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, NormalizedUserName = userId.ToUpperInvariant() });
        }

        db.Projects.Add(new Project
        {
            Id = ProjectId,
            Name = "Project 1",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "creator",
            LeadPoUserId = "po",
            HodUserId = "hod",
            RowVersion = new byte[] { 1 },
        });
        db.ProjectStages.Add(new ProjectStage { Id = StageId, ProjectId = ProjectId, StageCode = "ST10", SortOrder = 1 });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedUpload_CommitsWithItsNotification_AndSurvivesANotificationFailure(bool failNotification)
    {
        var requestId = await SubmitUploadRequestAsync();
        await using var db = new ApplicationDbContext(_options);
        var notifier = new QueueingNotifier(db, failNotification ? NotifierMode.Throw : NotifierMode.Queue);

        await CreateDecisionService(db, notifier).ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var document = await verify.ProjectDocuments.SingleAsync();
        Assert.Equal(ProjectDocumentStatus.Published, document.Status);
        Assert.True(File.Exists(StoredPath(document.StorageKey)));
        Assert.Equal(ProjectDocumentRequestStatus.Approved, (await verify.ProjectDocumentRequests.SingleAsync()).Status);
        Assert.Equal(failNotification ? 0 : 1, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task ApprovedUpload_WithANotificationRowThatCannotBeSaved_StillRecordsTheApproval()
    {
        // The notifier adds a row the database rejects. Publishing used to leave that row tracked
        // in the shared context after a failure, so the approval's own save tried to insert it
        // again and failed: the document was published but its request stayed "Submitted". The
        // queue now drops the rejected row, so the approval is recorded.
        var requestId = await SubmitUploadRequestAsync();
        await using var db = new ApplicationDbContext(_options);

        await CreateDecisionService(db, new QueueingNotifier(db, NotifierMode.QueueInvalidRow))
            .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(ProjectDocumentStatus.Published, (await verify.ProjectDocuments.SingleAsync()).Status);
        Assert.Equal(ProjectDocumentRequestStatus.Approved, (await verify.ProjectDocumentRequests.SingleAsync()).Status);
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task Archive_InterruptedWhileNotifying_LeavesTheDocumentPublishedAndUnnotified()
    {
        var documentId = await PublishDocumentAsync();
        await using var db = new ApplicationDbContext(_options);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateDocumentService(db, new CancellingNotifier(cancellation)).SoftDeleteAsync(documentId, "approver", cancellation.Token));

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(ProjectDocumentStatus.Published, (await verify.ProjectDocuments.SingleAsync()).Status);
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archive_CommitsWithItsNotification_AndSurvivesANotificationFailure(bool failNotification)
    {
        var documentId = await PublishDocumentAsync();
        await using var db = new ApplicationDbContext(_options);
        var notifier = new QueueingNotifier(db, failNotification ? NotifierMode.Throw : NotifierMode.Queue);

        await CreateDocumentService(db, notifier).SoftDeleteAsync(documentId, "approver", CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(ProjectDocumentStatus.SoftDeleted, (await verify.ProjectDocuments.SingleAsync()).Status);
        Assert.Equal(failNotification ? 0 : 1, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task HardDelete_InterruptedWhileNotifying_KeepsTheDocumentAndItsFile()
    {
        // The deletion and its notification now commit together. A cancellation while notifying
        // rolls the deletion back and restores the quarantined file, instead of leaving a
        // committed deletion that nobody was told about.
        var documentId = await PublishDocumentAsync();
        await MoveToRecycleBinAsync(documentId);
        string storageKey;
        await using (var read = new ApplicationDbContext(_options))
        {
            storageKey = (await read.ProjectDocuments.SingleAsync()).StorageKey;
        }

        await using var db = new ApplicationDbContext(_options);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateDocumentService(db, new CancellingNotifier(cancellation)).HardDeleteAsync(documentId, "approver", cancellation.Token));

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(1, await verify.ProjectDocuments.CountAsync());
        Assert.True(File.Exists(StoredPath(storageKey)));
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task HardDelete_CommitsWithItsNotification()
    {
        var documentId = await PublishDocumentAsync();
        await MoveToRecycleBinAsync(documentId);
        await using var db = new ApplicationDbContext(_options);

        await CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.Queue)).HardDeleteAsync(documentId, "approver", CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(0, await verify.ProjectDocuments.CountAsync());
        Assert.Equal(NotificationKind.DocumentDeleted, (await verify.NotificationDispatches.SingleAsync()).Kind);
    }

    [Fact]
    public async Task HardDelete_RefusesADocumentThatIsNotInTheRecycleBin()
    {
        // The admin recycle bin used to permanently delete every submitted id, including a
        // document someone restored after the page was opened.
        var documentId = await PublishDocumentAsync();
        var storageKey = await StorageKeyAsync();
        await using var db = new ApplicationDbContext(_options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.Queue)).HardDeleteAsync(documentId, "approver", CancellationToken.None));

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(ProjectDocumentStatus.Published, (await verify.ProjectDocuments.SingleAsync()).Status);
        Assert.True(File.Exists(StoredPath(storageKey)));
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task Replace_InterruptedWhileNotifying_KeepsTheOriginalFileAndRecord()
    {
        // The new file used to overwrite the original before the commit. A cancellation while
        // notifying then rolled the record back to the old file stamp and size with the old
        // bytes already gone. The original is now moved aside and put back.
        var documentId = await PublishDocumentAsync();
        var storageKey = await StorageKeyAsync();
        var originalBytes = await File.ReadAllBytesAsync(StoredPath(storageKey));
        await using var db = new ApplicationDbContext(_options);
        var documentService = CreateDocumentService(db, new CancellingNotifier(new CancellationTokenSource()));
        var replacement = await SaveTempAsync(documentService, "%PDF-1.4\nReplacement content");
        using var cancellation = new CancellationTokenSource();
        documentService = CreateDocumentService(db, new CancellingNotifier(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => documentService.OverwriteAsync(
            documentId, replacement.StorageKey, "spec-v2.pdf", replacement.Length, "application/pdf", "requestor", "approver", cancellation.Token));

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(1, (await verify.ProjectDocuments.SingleAsync()).FileStamp);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(StoredPath(storageKey)));
        Assert.True(File.Exists(StoredPath(replacement.StorageKey)), "The replacement should be back in staging for a retry.");
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task Replace_CommitsTheNewFileWithItsNotification_AndRemovesTheOldOne()
    {
        var documentId = await PublishDocumentAsync();
        var storageKey = await StorageKeyAsync();
        await using var db = new ApplicationDbContext(_options);
        var documentService = CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.Queue));
        var replacement = await SaveTempAsync(documentService, "%PDF-1.4\nReplacement content");

        await documentService.OverwriteAsync(
            documentId, replacement.StorageKey, "spec-v2.pdf", replacement.Length, "application/pdf", "requestor", "approver", CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(2, (await verify.ProjectDocuments.SingleAsync()).FileStamp);
        Assert.Equal("%PDF-1.4\nReplacement content", await File.ReadAllTextAsync(StoredPath(storageKey)));
        Assert.Equal(NotificationKind.DocumentReplaced, (await verify.NotificationDispatches.SingleAsync()).Kind);
        var quarantine = Path.Combine(_root, ".purge-quarantine");
        Assert.True(!Directory.Exists(quarantine) || !Directory.EnumerateFiles(quarantine, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task ApprovedUpload_InterruptedWhileNotifying_CanBeApprovedAgain()
    {
        // Nothing commits, and the uploaded file goes back to staging instead of being left
        // at a path no record points to, so a second approval succeeds.
        var requestId = await SubmitUploadRequestAsync();
        await using (var db = new ApplicationDbContext(_options))
        {
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateDecisionService(db, new CancellingNotifier(cancellation))
                .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, cancellation.Token));
        }

        await using (var verify = new ApplicationDbContext(_options))
        {
            Assert.Equal(0, await verify.ProjectDocuments.CountAsync());
            Assert.Equal(ProjectDocumentRequestStatus.Submitted, (await verify.ProjectDocumentRequests.SingleAsync()).Status);
        }

        await using (var retry = new ApplicationDbContext(_options))
        {
            await CreateDecisionService(retry, new QueueingNotifier(retry, NotifierMode.Queue))
                .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None);
        }

        await using var final = new ApplicationDbContext(_options);
        var document = await final.ProjectDocuments.SingleAsync();
        Assert.True(File.Exists(StoredPath(document.StorageKey)));
        Assert.Equal(ProjectDocumentRequestStatus.Approved, (await final.ProjectDocumentRequests.SingleAsync()).Status);
        Assert.Equal(1, await final.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task ApprovedUpload_WhenTheDecisionCannotBeSaved_PublishesNothingAndCanBeApprovedAgain()
    {
        // The document used to commit on its own before the request's decision was saved. If
        // that save failed (another reviewer decided first), the document stayed published while
        // its request still read "Submitted", and approving again published a duplicate.
        var requestId = await SubmitUploadRequestAsync();
        await using (var db = new ApplicationDbContext(FailingDecisionOptions(ProjectDocumentRequestStatus.Approved)))
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CreateDecisionService(db, new QueueingNotifier(db, NotifierMode.Queue))
                .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None));
        }

        await using (var verify = new ApplicationDbContext(_options))
        {
            Assert.Equal(0, await verify.ProjectDocuments.CountAsync());
            Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
            var request = await verify.ProjectDocumentRequests.SingleAsync();
            Assert.Equal(ProjectDocumentRequestStatus.Submitted, request.Status);
            Assert.True(File.Exists(StoredPath(request.TempStorageKey!)), "The upload should be back in staging.");
        }

        await using (var retry = new ApplicationDbContext(_options))
        {
            await CreateDecisionService(retry, new QueueingNotifier(retry, NotifierMode.Queue))
                .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None);
        }

        await using var final = new ApplicationDbContext(_options);
        var document = await final.ProjectDocuments.SingleAsync();
        Assert.True(File.Exists(StoredPath(document.StorageKey)));
        Assert.Equal(ProjectDocumentRequestStatus.Approved, (await final.ProjectDocumentRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApprovedReplacement_WhenTheDecisionCannotBeSaved_KeepsTheOriginalFileAndRecord()
    {
        var documentId = await PublishDocumentAsync();
        var storageKey = await StorageKeyAsync();
        var originalBytes = await File.ReadAllBytesAsync(StoredPath(storageKey));
        var requestId = await SubmitReplaceRequestAsync(documentId, "%PDF-1.4\nReplacement content");

        await using (var db = new ApplicationDbContext(FailingDecisionOptions(ProjectDocumentRequestStatus.Approved)))
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CreateDecisionService(db, new QueueingNotifier(db, NotifierMode.Queue))
                .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None));
        }

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(1, (await verify.ProjectDocuments.SingleAsync()).FileStamp);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(StoredPath(storageKey)));
        var request = await verify.ProjectDocumentRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(ProjectDocumentRequestStatus.Submitted, request.Status);
        Assert.True(File.Exists(StoredPath(request.TempStorageKey!)), "The replacement should be back in staging.");
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task ApprovedReplacement_CommitsTheNewFile_AndRemovesTheOldOneOnlyAfterTheDecisionCommits()
    {
        var documentId = await PublishDocumentAsync();
        var storageKey = await StorageKeyAsync();
        var requestId = await SubmitReplaceRequestAsync(documentId, "%PDF-1.4\nReplacement content");

        await using (var db = new ApplicationDbContext(_options))
        {
            await CreateDecisionService(db, new QueueingNotifier(db, NotifierMode.Queue))
                .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None);
        }

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(2, (await verify.ProjectDocuments.SingleAsync()).FileStamp);
        Assert.Equal("%PDF-1.4\nReplacement content", await File.ReadAllTextAsync(StoredPath(storageKey)));
        Assert.Equal(ProjectDocumentRequestStatus.Approved, (await verify.ProjectDocumentRequests.SingleAsync(r => r.Id == requestId)).Status);
        var quarantine = Path.Combine(_root, ".purge-quarantine");
        Assert.True(!Directory.Exists(quarantine) || !Directory.EnumerateFiles(quarantine, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Rejection_WhenTheDecisionCannotBeSaved_KeepsTheStagedFile()
    {
        // The staged file used to be deleted before the rejection was saved, so a failed save
        // left a submitted request whose file was gone.
        var requestId = await SubmitUploadRequestAsync();
        await using (var db = new ApplicationDbContext(FailingDecisionOptions(ProjectDocumentRequestStatus.Rejected)))
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CreateDecisionService(db, new QueueingNotifier(db, NotifierMode.None))
                .RejectAsync(requestId, "approver", isAdmin: true, isHoD: false, note: "Wrong file", CancellationToken.None));
        }

        await using var verify = new ApplicationDbContext(_options);
        var request = await verify.ProjectDocumentRequests.SingleAsync();
        Assert.Equal(ProjectDocumentRequestStatus.Submitted, request.Status);
        Assert.True(File.Exists(StoredPath(request.TempStorageKey!)));
    }

    // Restores are audited with the restore (docs/ux/admin-review-4-fixes.md, PR 2). The audit
    // entry used to be written after the commit, so a failed audit write left a restored
    // document with no record, and Restore deleted items reported it as failed although it was
    // back; a retry then skipped it because it was no longer in the recycle bin.
    [Fact]
    public async Task Restore_CommitsWithItsNotificationAndItsAuditEntry()
    {
        var documentId = await PublishDocumentAsync();
        await MoveToRecycleBinAsync(documentId);
        await using var db = new ApplicationDbContext(_options);

        await CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.Queue), DatabaseAudit(db))
            .RestoreAsync(documentId, "approver", CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(ProjectDocumentStatus.Published, (await verify.ProjectDocuments.SingleAsync()).Status);
        Assert.Equal(NotificationKind.DocumentRestored, (await verify.NotificationDispatches.SingleAsync()).Kind);
        var entry = await verify.AuditLogs.SingleAsync(log => log.Action == "Project.DocumentRestored");
        Assert.Equal("approver", entry.UserId);
    }

    [Fact]
    public async Task Restore_WhenTheAuditCannotBeWritten_LeavesTheDocumentInTheRecycleBin()
    {
        var documentId = await PublishDocumentAsync();
        await MoveToRecycleBinAsync(documentId);
        await using var db = new ApplicationDbContext(_options);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.Queue), new FailingAudit())
                .RestoreAsync(documentId, "approver", CancellationToken.None));

        await using var verify = new ApplicationDbContext(_options);
        var document = await verify.ProjectDocuments.SingleAsync();
        Assert.Equal(ProjectDocumentStatus.SoftDeleted, document.Status);
        Assert.True(document.IsArchived);
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
        Assert.Equal(0, await verify.AuditLogs.CountAsync(log => log.Action == "Project.DocumentRestored"));
    }

    [Fact]
    public async Task RecycleBinRestore_WhenTheAuditFails_ReportsTheDocumentAsFailed_AndARetryRestoresIt()
    {
        var documentId = await PublishDocumentAsync();
        await MoveToRecycleBinAsync(documentId);

        await using (var db = new ApplicationDbContext(_options))
        {
            var failed = await CreateRecycleService(db, CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.Queue), new FailingAudit()))
                .ExecuteAsync(DocumentRecycleAction.Restore, new[] { documentId }, "approver", CancellationToken.None);

            Assert.Equal(0, failed.Succeeded);
            Assert.Equal(new[] { documentId }, failed.FailedIds);
        }

        await using (var verify = new ApplicationDbContext(_options))
        {
            Assert.Equal(ProjectDocumentStatus.SoftDeleted, (await verify.ProjectDocuments.SingleAsync()).Status);
        }

        // The document is still in the recycle bin, so the retry finds it instead of skipping it.
        await using (var retry = new ApplicationDbContext(_options))
        {
            var result = await CreateRecycleService(retry, CreateDocumentService(retry, new QueueingNotifier(retry, NotifierMode.Queue), DatabaseAudit(retry)))
                .ExecuteAsync(DocumentRecycleAction.Restore, new[] { documentId }, "approver", CancellationToken.None);

            Assert.Equal(1, result.Succeeded);
            Assert.Equal(0, result.Skipped);
            Assert.Empty(result.FailedIds);
        }

        await using var final = new ApplicationDbContext(_options);
        Assert.Equal(ProjectDocumentStatus.Published, (await final.ProjectDocuments.SingleAsync()).Status);
        Assert.Equal(1, await final.AuditLogs.CountAsync(log => log.Action == "Project.DocumentRestored"));
        Assert.Equal(1, await final.NotificationDispatches.CountAsync());
    }

    private static DocumentRecycleService CreateRecycleService(ApplicationDbContext db, IDocumentService documents)
    {
        var options = Options.Create(new AdminRecoveryOptions());
        return new DocumentRecycleService(
            new DocumentRecoveryQueryService(db, new AdminTimeService(Clock()), options),
            documents,
            options,
            NullLogger<DocumentRecycleService>.Instance);
    }

    /// <summary>The real audit service, writing into the caller's context and transaction.</summary>
    private static AuditService DatabaseAudit(ApplicationDbContext db) => new(db, new HttpContextAccessor(), Clock());

    /// <summary>Fails the audit write the way the real audit service does when its insert fails.</summary>
    private sealed class FailingAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => throw new DbUpdateException("Audit store unavailable.");
    }

    private async Task<int> SubmitReplaceRequestAsync(int documentId, string content)
    {
        await using var db = new ApplicationDbContext(_options);
        var documentService = CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.None));
        var temp = await SaveTempAsync(documentService, content);
        var request = await new DocumentRequestService(db, Clock(), new RecordingAudit())
            .CreateReplaceRequestAsync(documentId, null, temp, "requestor", CancellationToken.None);
        return request.Id;
    }

    private DbContextOptions<ApplicationDbContext> FailingDecisionOptions(ProjectDocumentRequestStatus status)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new FailDecisionInterceptor(status))
            .Options;

    /// <summary>Fails the save that records a request's decision, as a concurrent decision would.</summary>
    private sealed class FailDecisionInterceptor(ProjectDocumentRequestStatus status) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ProjectDocumentRequest>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.Status == status))
            {
                throw new DbUpdateConcurrencyException("Another reviewer decided this request first.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static async Task<DocumentFileDescriptor> SaveTempAsync(DocumentService documentService, string content)
    {
        await using var pdf = new MemoryStream(Encoding.ASCII.GetBytes(content));
        return await documentService.SaveTempAsync(ProjectId, pdf, "spec-v2.pdf", "application/pdf", CancellationToken.None);
    }

    private async Task<string> StorageKeyAsync()
    {
        await using var read = new ApplicationDbContext(_options);
        return (await read.ProjectDocuments.SingleAsync()).StorageKey;
    }

    private async Task<int> SubmitUploadRequestAsync()
    {
        await using var db = new ApplicationDbContext(_options);
        var documentService = CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.None));
        await using var pdf = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4\nSample content"));
        var temp = await documentService.SaveTempAsync(ProjectId, pdf, "spec.pdf", "application/pdf", CancellationToken.None);
        var request = await new DocumentRequestService(db, Clock(), new RecordingAudit())
            .CreateUploadRequestAsync(ProjectId, StageId, "Spec", null, temp, "requestor", CancellationToken.None);
        return request.Id;
    }

    private async Task MoveToRecycleBinAsync(int documentId)
    {
        // Set the state directly so the soft delete adds no notification of its own.
        await using var db = new ApplicationDbContext(_options);
        var document = await db.ProjectDocuments.SingleAsync(item => item.Id == documentId);
        document.Status = ProjectDocumentStatus.SoftDeleted;
        document.IsArchived = true;
        await db.SaveChangesAsync();
    }

    private async Task<int> PublishDocumentAsync()
    {
        var requestId = await SubmitUploadRequestAsync();
        await using var db = new ApplicationDbContext(_options);
        await CreateDecisionService(db, new QueueingNotifier(db, NotifierMode.None))
            .ApproveAsync(requestId, "approver", isAdmin: true, isHoD: false, note: null, CancellationToken.None);
        return (await db.ProjectDocuments.SingleAsync()).Id;
    }

    private DocumentDecisionService CreateDecisionService(ApplicationDbContext db, IDocumentNotificationService notifier)
        => new(db, CreateDocumentService(db, notifier), Clock(), new RecordingAudit());

    private DocumentService CreateDocumentService(ApplicationDbContext db, IDocumentNotificationService notifier, IAuditService? audit = null)
    {
        var uploadRoot = new FixedUploadRoot(_root);
        return new DocumentService(
            db,
            Options.Create(new ProjectDocumentOptions { ProjectsSubpath = "projects", StorageSubPath = "docs", TempSubPath = "temp" }),
            uploadRoot,
            Clock(),
            audit ?? new RecordingAudit(),
            notifier,
            new ProjectDocumentStorageResolver(uploadRoot),
            null,
            NullLogger<DocumentService>.Instance);
    }

    private string StoredPath(string storageKey)
        => new ProjectDocumentStorageResolver(new FixedUploadRoot(_root)).ResolveAbsolutePath(storageKey);

    private static IClock Clock() => FakeClock.AtUtc(new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero));

    private enum NotifierMode
    {
        None,
        Queue,
        Throw,
        QueueInvalidRow,
    }

    /// <summary>Queues an outbox row into the caller's context like the real producer, or fails.</summary>
    private sealed class QueueingNotifier(ApplicationDbContext db, NotifierMode mode) : NotifierBase
    {
        protected override Task NotifyAsync(NotificationKind kind, CancellationToken cancellationToken)
        {
            switch (mode)
            {
                case NotifierMode.Throw:
                    throw new InvalidOperationException("Injected notification failure.");
                case NotifierMode.Queue:
                    db.NotificationDispatches.Add(Dispatch(kind, "po"));
                    break;
                case NotifierMode.QueueInvalidRow:
                    // RecipientUserId is NOT NULL, so saving this row fails.
                    db.NotificationDispatches.Add(Dispatch(kind, null!));
                    break;
            }

            return Task.CompletedTask;
        }

        private static NotificationDispatch Dispatch(NotificationKind kind, string recipient) => new()
        {
            RecipientUserId = recipient,
            Kind = kind,
            PayloadJson = "{}",
            CreatedUtc = DateTime.UtcNow,
        };
    }

    /// <summary>Cancels the request mid-notification, as an aborted HTTP request would.</summary>
    private sealed class CancellingNotifier(CancellationTokenSource cancellation) : NotifierBase
    {
        protected override Task NotifyAsync(NotificationKind kind, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private abstract class NotifierBase : IDocumentNotificationService
    {
        protected abstract Task NotifyAsync(NotificationKind kind, CancellationToken cancellationToken);

        public Task NotifyDocumentPublishedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.DocumentPublished, cancellationToken);

        public Task NotifyDocumentReplacedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.DocumentReplaced, cancellationToken);

        public Task NotifyDocumentArchivedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.DocumentArchived, cancellationToken);

        public Task NotifyDocumentRestoredAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.DocumentRestored, cancellationToken);

        public Task NotifyDocumentDeletedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.DocumentDeleted, cancellationToken);
    }

    /// <summary>
    /// A fixed upload root. The real provider reads PM_UPLOAD_ROOT, which other test classes
    /// change while tests run in parallel.
    /// </summary>
    private sealed class FixedUploadRoot(string root) : IUploadRootProvider
    {
        public string RootPath => root;
        public string GetProjectRoot(int projectId) => Ensure(Path.Combine(root, "projects", projectId.ToString()));
        public string GetProjectPhotosRoot(int projectId) => Ensure(Path.Combine(GetProjectRoot(projectId), "photos"));
        public string GetProjectDocumentsRoot(int projectId) => Ensure(Path.Combine(GetProjectRoot(projectId), "docs"));
        public string GetProjectCommentsRoot(int projectId) => Ensure(Path.Combine(GetProjectRoot(projectId), "comments"));
        public string GetProjectVideosRoot(int projectId) => Ensure(Path.Combine(GetProjectRoot(projectId), "videos"));
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => Ensure(Path.Combine(root, "social", eventId.ToString("D")));

        private static string Ensure(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
