using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Activities;

public interface IActivityAttachmentStorage
{
    Task<ActivityAttachmentStorageResult> SaveAsync(
        int activityId,
        ActivityAttachmentUpload upload,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
        => Task.FromResult<Stream?>(null);
}

public sealed record ActivityAttachmentStorageResult(
    string StorageKey,
    string FileName,
    long FileSize);
