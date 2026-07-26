namespace ProjectManagement.Services.Arpp;

public interface IArppAttachmentStorage
{
    Task<ArppStoredAttachment> SaveAsync(
        long issueId,
        string originalFileName,
        string contentType,
        long declaredLength,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}
