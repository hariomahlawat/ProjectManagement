using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.Projects;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DocumentServicesTests
{
    [Fact]
    public async Task SaveTempAsync_RejectsNonPdf()
    {
        await using var db = CreateContext();
        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, _, _, _) = CreateServices(db, options);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a pdf"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                documentService.SaveTempAsync(1, stream, "bad.pdf", "application/pdf", CancellationToken.None));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SaveTempAsync_RejectsWhenTooLarge()
    {
        await using var db = CreateContext();
        var options = CreateDocumentOptions();
        options.MaxSizeMb = 1;
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, _, _, _) = CreateServices(db, options);
            using var stream = CreatePdfStream((options.MaxSizeMb * 1024 * 1024) + 128);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                documentService.SaveTempAsync(1, stream, "large.pdf", "application/pdf", CancellationToken.None));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ApproveUpload_PublishesDocument()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 1, 10);
        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, decisionService, audit) = CreateServices(db, options);
            using var stream = CreatePdfStream(2048);

            var temp = await documentService.SaveTempAsync(1, stream, "spec.pdf", "application/pdf", CancellationToken.None);
            var request = await requestService.CreateUploadRequestAsync(1, 10, "Spec", null, temp, "requestor", CancellationToken.None);

            await decisionService.ApproveAsync(request.Id, "approver", "Looks good", CancellationToken.None);

            var document = await db.ProjectDocuments.SingleAsync();
            Assert.Equal(ProjectDocumentStatus.Published, document.Status);
            Assert.Equal(1, document.FileStamp);
            Assert.Equal("requestor", document.UploadedByUserId);
            Assert.Equal("Spec", document.Title);
            Assert.Equal(ProjectDocumentOcrStatus.Pending, document.OcrStatus);
            Assert.Null(document.OcrFailureReason);

            var path = ResolvePath(root, document.StorageKey);
            Assert.True(File.Exists(path));
            var fileBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(temp.Length, fileBytes.LongLength);

            Assert.Contains(audit.Entries, e => e.Action == "Project.DocumentPublished");
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ApproveUpload_WithTot_AssignsTot()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 6, 60, ProjectTotStatus.InProgress);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 6);

        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, decisionService, _) = CreateServices(db, options);
            using var stream = CreatePdfStream(2048);

            var temp = await documentService.SaveTempAsync(1, stream, "tot.pdf", "application/pdf", CancellationToken.None);
            var request = await requestService.CreateUploadRequestAsync(6, 60, "ToT Plan", tot.Id, temp, "requestor", CancellationToken.None);

            await decisionService.ApproveAsync(request.Id, "approver", null, CancellationToken.None);

            var document = await db.ProjectDocuments.SingleAsync();
            Assert.Equal(tot.Id, document.TotId);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task CreateUploadRequestAsync_ThrowsWhenTotNotAllowed()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 7, 70, ProjectTotStatus.NotRequired);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 7);

        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, _, _) = CreateServices(db, options);
            using var stream = CreatePdfStream(2048);

            var temp = await documentService.SaveTempAsync(1, stream, "invalid.pdf", "application/pdf", CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                requestService.CreateUploadRequestAsync(7, 70, "Invalid", tot.Id, temp, "requestor", CancellationToken.None));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ApproveReplace_UpdatesFileStamp()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 2, 20);
        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, decisionService, _) = CreateServices(db, options);
            using var initial = CreatePdfStream(1024);
            var temp = await documentService.SaveTempAsync(1, initial, "init.pdf", "application/pdf", CancellationToken.None);
            var uploadRequest = await requestService.CreateUploadRequestAsync(2, 20, "Manual", null, temp, "requestor", CancellationToken.None);
            await decisionService.ApproveAsync(uploadRequest.Id, "approver", null, CancellationToken.None);

            var document = await db.ProjectDocuments.SingleAsync();
            using var replacement = CreatePdfStream(1536, payload: "New content");
            var tempReplace = await documentService.SaveTempAsync(2, replacement, "manual.pdf", "application/pdf", CancellationToken.None);
            await requestService.CreateReplaceRequestAsync(document.Id, null, tempReplace, "requestor", CancellationToken.None);

            var replaceRequest = await db.ProjectDocumentRequests
                .OrderByDescending(r => r.Id)
                .FirstAsync();

            await decisionService.ApproveAsync(replaceRequest.Id, "approver", "update", CancellationToken.None);

            await db.Entry(document).ReloadAsync();
            Assert.Equal(2, document.FileStamp);
            Assert.Equal("manual.pdf", document.OriginalFileName);

            var path = ResolvePath(root, document.StorageKey);
            var content = await File.ReadAllBytesAsync(path);
            Assert.Equal(tempReplace.Length, content.LongLength);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task SoftDeleteRestore_TogglesStatus()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 3, 30);
        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, decisionService, _) = CreateServices(db, options);
            using var stream = CreatePdfStream(1024);
            var temp = await documentService.SaveTempAsync(1, stream, "soft.pdf", "application/pdf", CancellationToken.None);
            var request = await requestService.CreateUploadRequestAsync(3, 30, "Soft", null, temp, "requestor", CancellationToken.None);
            await decisionService.ApproveAsync(request.Id, "approver", null, CancellationToken.None);

            var document = await db.ProjectDocuments.SingleAsync();
            await documentService.SoftDeleteAsync(document.Id, "approver", CancellationToken.None);
            Assert.Equal(ProjectDocumentStatus.SoftDeleted, document.Status);
            Assert.True(document.IsArchived);

            await documentService.RestoreAsync(document.Id, "approver", CancellationToken.None);
            Assert.Equal(ProjectDocumentStatus.Published, document.Status);
            Assert.False(document.IsArchived);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task RetryOcrAsync_ResetsFailureState()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 5, 50);
        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, decisionService, audit) = CreateServices(db, options);
            using var stream = CreatePdfStream(1024);
            var temp = await documentService.SaveTempAsync(1, stream, "ocr.pdf", "application/pdf", CancellationToken.None);
            var request = await requestService.CreateUploadRequestAsync(5, 50, "OCR Doc", null, temp, "requestor", CancellationToken.None);
            await decisionService.ApproveAsync(request.Id, "approver", null, CancellationToken.None);

            var document = await db.ProjectDocuments.SingleAsync();
            document.OcrStatus = ProjectDocumentOcrStatus.Failed;
            document.OcrFailureReason = "ocrmypdf exited with code 1";
            document.OcrLastTriedUtc = new DateTimeOffset(2024, 10, 7, 10, 0, 0, TimeSpan.Zero);

            var staleText = new ProjectDocumentText
            {
                ProjectDocumentId = document.Id,
                OcrText = "stale",
                UpdatedAtUtc = document.OcrLastTriedUtc.Value
            };
            document.DocumentText = staleText;
            db.ProjectDocumentTexts.Add(staleText);
            await db.SaveChangesAsync();

            await documentService.RetryOcrAsync(document.Id, "admin", CancellationToken.None);

            var reloaded = await db.ProjectDocuments
                .Include(d => d.DocumentText)
                .SingleAsync();

            Assert.Equal(ProjectDocumentOcrStatus.Pending, reloaded.OcrStatus);
            Assert.Null(reloaded.OcrFailureReason);
            Assert.Null(reloaded.OcrLastTriedUtc);
            Assert.NotNull(reloaded.DocumentText);
            Assert.Null(reloaded.DocumentText!.OcrText);
            var expectedTimestamp = new DateTimeOffset(2024, 10, 8, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal(expectedTimestamp, reloaded.DocumentText!.UpdatedAtUtc);
            Assert.Contains(
                audit.Entries,
                entry => entry.Action == "Project.DocumentOcrRequeued" && entry.Data!["DocumentId"] == reloaded.Id.ToString());
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task HardDelete_RemovesDatabaseAndFile()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 4, 40);
        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, decisionService, _) = CreateServices(db, options);
            using var stream = CreatePdfStream(1024);
            var temp = await documentService.SaveTempAsync(1, stream, "hard.pdf", "application/pdf", CancellationToken.None);
            var request = await requestService.CreateUploadRequestAsync(4, 40, "Hard", null, temp, "requestor", CancellationToken.None);
            await decisionService.ApproveAsync(request.Id, "approver", null, CancellationToken.None);

            var document = await db.ProjectDocuments.SingleAsync();
            var path = ResolvePath(root, document.StorageKey);
            Assert.True(File.Exists(path));

            await documentService.HardDeleteAsync(document.Id, "approver", CancellationToken.None);

            Assert.Empty(db.ProjectDocuments);
            Assert.False(File.Exists(path));
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ReplaceRequest_EnforcesSinglePending()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, 5, 50);
        var options = CreateDocumentOptions();
        var root = CreateTempRoot();
        SetUploadRoot(root);
        try
        {
            var (documentService, requestService, decisionService, _) = CreateServices(db, options);
            using var stream = CreatePdfStream(1024);
            var temp = await documentService.SaveTempAsync(1, stream, "original.pdf", "application/pdf", CancellationToken.None);
            var uploadRequest = await requestService.CreateUploadRequestAsync(5, 50, "Original", null, temp, "requestor", CancellationToken.None);
            await decisionService.ApproveAsync(uploadRequest.Id, "approver", null, CancellationToken.None);
            var document = await db.ProjectDocuments.SingleAsync();

            using var replacement = CreatePdfStream(1024, payload: "replace");
            var tempReplace = await documentService.SaveTempAsync(2, replacement, "replace.pdf", "application/pdf", CancellationToken.None);
            await requestService.CreateReplaceRequestAsync(document.Id, null, tempReplace, "requestor", CancellationToken.None);

            using var another = CreatePdfStream(1024, payload: "again");
            var tempSecond = await documentService.SaveTempAsync(3, another, "replace.pdf", "application/pdf", CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                requestService.CreateReplaceRequestAsync(document.Id, null, tempSecond, "requestor", CancellationToken.None));

            await documentService.DeleteTempAsync(tempSecond.StorageKey, CancellationToken.None);
        }
        finally
        {
            ResetUploadRoot();
            CleanupTempRoot(root);
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, int projectId, int stageId, ProjectTotStatus? totStatus = null)
    {
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"Project {projectId}",
            CreatedByUserId = "creator",
            RowVersion = new byte[] { 1 }
        });

        db.ProjectStages.Add(new ProjectStage
        {
            Id = stageId,
            ProjectId = projectId,
            StageCode = $"ST{stageId}",
            SortOrder = 1
        });

        if (totStatus.HasValue)
        {
            db.ProjectTots.Add(new ProjectTot
            {
                ProjectId = projectId,
                Status = totStatus.Value
            });
        }

        await db.SaveChangesAsync();
    }

    private static ProjectDocumentOptions CreateDocumentOptions()
    {
        return new ProjectDocumentOptions
        {
            ProjectsSubpath = "projects",
            StorageSubPath = "docs",
            TempSubPath = "temp"
        };
    }

    private static (DocumentService documentService, DocumentRequestService requestService, DocumentDecisionService decisionService, RecordingAudit audit) CreateServices(ApplicationDbContext db, ProjectDocumentOptions options)
    {
        var audit = new RecordingAudit();
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var photoOptions = Options.Create(new ProjectPhotoOptions());
        var documentOptions = Options.Create(options);
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), "pm-doc-tests")
        };
        var uploadRoot = new UploadRootProvider(photoOptions, documentOptions, environment, NullLogger<UploadRootProvider>.Instance);
        var storageResolver = new ProjectDocumentStorageResolver(uploadRoot);
        var documentService = new DocumentService(db, documentOptions, uploadRoot, clock, audit, new NullDocumentNotificationService(), storageResolver, null, NullLogger<DocumentService>.Instance);
        var requestService = new DocumentRequestService(db, clock, audit);
        var decisionService = new DocumentDecisionService(db, documentService, clock, audit);
        return (documentService, requestService, decisionService, audit);
    }

    private sealed class NullDocumentNotificationService : IDocumentNotificationService
    {
        public Task NotifyDocumentArchivedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyDocumentDeletedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyDocumentPublishedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyDocumentReplacedAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyDocumentRestoredAsync(ProjectDocument document, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static MemoryStream CreatePdfStream(long length, string payload = "Sample")
    {
        if (length < 6)
        {
            length = 6;
        }

        var stream = new MemoryStream();
        var header = Encoding.ASCII.GetBytes("%PDF-1.4\n");
        stream.Write(header, 0, header.Length);
        var remaining = length - header.Length;
        var body = Encoding.UTF8.GetBytes(payload);
        while (remaining > 0)
        {
            var chunk = Math.Min(body.Length, (int)remaining);
            stream.Write(body, 0, chunk);
            remaining -= chunk;
        }

        stream.Position = 0;
        return stream;
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "pm-docs-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupTempRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    private static void SetUploadRoot(string path)
    {
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", path);
    }

    private static void ResetUploadRoot()
    {
        Environment.SetEnvironmentVariable("PM_UPLOAD_ROOT", null);
    }

    private static string ResolvePath(string root, string storageKey)
    {
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(root, relative);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
