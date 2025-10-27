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

    public ActivityAttachmentManagerTests()
    {
        _context = ActivityTestHelpers.CreateContext();
        _repository = new ActivityRepository(_context);
        _manager = new ActivityAttachmentManager(_repository, _storage, _validator, _clock);
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
        Assert.Equal(FakeStorage.DownloadRoot + "/" + FakeStorage.StorageKey, item.DownloadUrl);
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
    }

    private sealed class FakeStorage : IActivityAttachmentStorage
    {
        public const string StorageKey = "activities/1/fake-key";
        public const string FileName = "fake.pdf";
        public const string DownloadRoot = "/files";

        public bool SaveCalled { get; private set; }
        public bool DeleteCalled { get; private set; }
        public string? DeletedKey { get; private set; }

        public Task<ActivityAttachmentStorageResult> SaveAsync(int activityId,
                                                               ActivityAttachmentUpload upload,
                                                               CancellationToken cancellationToken = default)
        {
            SaveCalled = true;
            return Task.FromResult(new ActivityAttachmentStorageResult(StorageKey, FileName, upload.Length));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeleteCalled = true;
            DeletedKey = storageKey;
            return Task.CompletedTask;
        }

        public string GetDownloadUrl(string storageKey)
        {
            return DownloadRoot + "/" + storageKey;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
