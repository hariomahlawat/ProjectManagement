using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Services.Arpp;

public interface IArppAttachmentService
{
    Task<ArppAttachmentCommandResult> UploadOrReplaceAsync(
        long issueId,
        IFormFile? file,
        string userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<ArppAttachmentCommandResult> DeleteAsync(
        long issueId,
        long attachmentId,
        string userId,
        string? userName,
        CancellationToken cancellationToken = default);

    Task<ArppAttachmentDownload?> OpenDownloadAsync(
        long issueId,
        CancellationToken cancellationToken = default);
}
