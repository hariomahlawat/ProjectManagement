namespace ProjectManagement.Services.DocRepo;

public interface IDocStorage
{
    Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
}
