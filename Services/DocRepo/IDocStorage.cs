using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.DocRepo;

public interface IDocStorage
{
    Task<string> SaveAsync(Stream source, string safeFileName, DateTime utcNow, CancellationToken ct);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
    Task<bool> ExistsAsync(string storagePath, CancellationToken ct);

    // Full path on this server's disk when the file is stored locally, so tools such as pdftoppm
    // can read it in place; null when the store has no local path.
    string? TryGetLocalPath(string storagePath) => null;
}
