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

    string GetDownloadUrl(string storageKey);
}

public sealed record ActivityAttachmentStorageResult(
    string StorageKey,
    string FileName,
    long FileSize);
