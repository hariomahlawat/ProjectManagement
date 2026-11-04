using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
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
        await using var db = new ApplicationDbContext(_options);

        await CreateDocumentService(db, new QueueingNotifier(db, NotifierMode.Queue)).HardDeleteAsync(documentId, "approver", CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(0, await verify.ProjectDocuments.CountAsync());
        Assert.Equal(NotificationKind.DocumentDeleted, (await verify.NotificationDispatches.SingleAsync()).Kind);
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

    private DocumentService CreateDocumentService(ApplicationDbContext db, IDocumentNotificationService notifier)
    {
        var uploadRoot = new FixedUploadRoot(_root);
        return new DocumentService(
            db,
            Options.Create(new ProjectDocumentOptions { ProjectsSubpath = "projects", StorageSubPath = "docs", TempSubPath = "temp" }),
            uploadRoot,
            Clock(),
            new RecordingAudit(),
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
