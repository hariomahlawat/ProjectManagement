using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services;
using ProjectManagement.Services.Activities;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public class ActivityAttachmentManagerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IActivityRepository _repository;
    private readonly ActivityAttachmentManager _manager;
    private readonly FakeStorage _storage = new();
    private readonly ActivityAttachmentValidator _validator = new();
    private readonly FakeClock _clock = new();
    private readonly StubDocRepoIngestionService _ingestion = new();
    private readonly TestUploadRootProvider _uploadRootProvider;
    private readonly string _rootPath;
    private readonly FakeUrlBuilder _urlBuilder = new();

    public ActivityAttachmentManagerTests()
    {
        _context = ActivityTestHelpers.CreateContext();
        _repository = new ActivityRepository(_context);
        _rootPath = Path.Combine(Path.GetTempPath(), "activity-attachments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
        _uploadRootProvider = new TestUploadRootProvider(_rootPath);
        _storage.RootPath = _rootPath;
        _manager = new ActivityAttachmentManager(
            _repository,
            _storage,
            _validator,
            _clock,
            _ingestion,
            _uploadRootProvider,
            _urlBuilder,
            NullLogger<ActivityAttachmentManager>.Instance);
    }

    [Fact]
    public async Task AddAsync_PersistsAttachmentAndUsesStorage()
    {
        var activity = await CreateActivityAsync();
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var upload = new ActivityAttachmentUpload(stream, "plan.pdf", "application/pdf", stream.Length);

        var attachment = await _manager.AddAsync(activity, upload, "user-1", CancellationToken.None);

        Assert.True(_storage.SaveCalled);
        Assert.Equal(FakeStorage.StorageKey, attachment.StorageKey);
        Assert.Equal(FakeStorage.FileName, attachment.OriginalFileName);
        Assert.Equal(_clock.UtcNow, attachment.UploadedAtUtc);

        var stored = await _repository.GetAttachmentByIdAsync(attachment.Id, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.True(_ingestion.Called);
    }

    [Fact]
    public async Task RemoveAsync_DeletesFromStorage()
    {
        var activity = await CreateActivityAsync();
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var upload = new ActivityAttachmentUpload(stream, "spec.pdf", "application/pdf", stream.Length);
        var attachment = await _manager.AddAsync(activity, upload, "user-1", CancellationToken.None);

        await _manager.RemoveAsync(attachment, CancellationToken.None);

        Assert.True(_storage.DeleteCalled);
        Assert.Equal(attachment.StorageKey, _storage.DeletedKey);
        var stored = await _repository.GetAttachmentByIdAsync(attachment.Id, CancellationToken.None);
        Assert.Null(stored);
    }

    [Fact]
    public async Task CreateMetadata_ProvidesDownloadUrl()
    {
        var activity = await CreateActivityAsync();
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var upload = new ActivityAttachmentUpload(stream, " diagram.pdf ", "application/pdf", stream.Length);
        await _manager.AddAsync(activity, upload, "user-1", CancellationToken.None);

        var metadata = _manager.CreateMetadata(activity);
        var item = Assert.Single(metadata);
        Assert.Equal($"/files/signed?t={FakeStorage.StorageKey}", item.DownloadUrl);
        Assert.Equal(ActivityAttachmentValidator.SanitizeFileName(upload.FileName), item.FileName);
    }

    private async Task<Activity> CreateActivityAsync()
    {
        var type = new ActivityType
        {
            Name = "Operations",
            CreatedByUserId = "seed"
        };

        _context.ActivityTypes.Add(type);
        await _context.SaveChangesAsync();

        var activity = new Activity
        {
            Title = "Kickoff",
            ActivityTypeId = type.Id,
            CreatedByUserId = "user-1"
        };

        await _repository.AddAsync(activity, CancellationToken.None);
        return activity;
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed class FakeStorage : IActivityAttachmentStorage
    {
        public const string StorageKey = "activities/1/fake-key";
        public const string FileName = "fake.pdf";

        public bool SaveCalled { get; private set; }
        public bool DeleteCalled { get; private set; }
        public string? DeletedKey { get; private set; }

        public string RootPath { get; set; } = Path.GetTempPath();

        public Task<ActivityAttachmentStorageResult> SaveAsync(int activityId,
                                                               ActivityAttachmentUpload upload,
                                                               CancellationToken cancellationToken = default)
        {
            SaveCalled = true;
            var absolutePath = Path.Combine(RootPath, StorageKey.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            if (upload.Content.CanSeek)
            {
                upload.Content.Seek(0, SeekOrigin.Begin);
            }

            using (var fileStream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                upload.Content.CopyTo(fileStream);
            }

            return Task.FromResult(new ActivityAttachmentStorageResult(StorageKey, FileName, upload.Length));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeleteCalled = true;
            DeletedKey = storageKey;
            var absolutePath = Path.Combine(RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            return Task.CompletedTask;
        }

    }

    private sealed class FakeUrlBuilder : IProtectedFileUrlBuilder
    {
        public string CreateDownloadUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null)
        {
            return string.IsNullOrWhiteSpace(storageKey) ? string.Empty : $"/files/signed?t={storageKey}";
        }

        public string CreateInlineUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null)
        {
            return string.IsNullOrWhiteSpace(storageKey) ? string.Empty : $"/files/signed?t={storageKey}&mode=inline";
        }
    }

    private sealed class StubDocRepoIngestionService : IDocRepoIngestionService
    {
        public bool Called { get; private set; }

        public Task<Guid> IngestExternalPdfAsync(Stream pdfStream, string originalFileName, string sourceModule, string sourceItemId, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class TestUploadRootProvider : IUploadRootProvider
    {
        public TestUploadRootProvider(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        // For tests we can just return subfolders under RootPath.
        public string GetProjectRoot(int projectId)
            => Path.Combine(RootPath, "projects", projectId.ToString());

        public string GetProjectDocumentsRoot(int projectId)
            => Path.Combine(GetProjectRoot(projectId), "documents");

        public string GetProjectPhotosRoot(int projectId)
            => Path.Combine(GetProjectRoot(projectId), "photos");

        public string GetProjectVideosRoot(int projectId)
            => Path.Combine(GetProjectRoot(projectId), "videos");

        public string GetProjectCommentsRoot(int projectId)
            => Path.Combine(GetProjectRoot(projectId), "comments");

        public string GetSocialMediaRoot(string category, Guid id)
            => Path.Combine(RootPath, "social", category, id.ToString("N"));
    }
}
