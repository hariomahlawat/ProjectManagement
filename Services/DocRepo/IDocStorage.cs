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

    // Moves a stored file to another path in the same store, creating folders as needed. Throws
    // FileNotFoundException or DirectoryNotFoundException when the source is missing, and
    // IOException when the destination already exists. The trash purge uses it to set a file
    // aside until the database row is gone, and to put it back if the row stays.
    Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct)
        => throw new NotSupportedException("This document store cannot move files.");

    // Full path on this server's disk when the file is stored locally, so tools such as pdftoppm
    // can read it in place; null when the store has no local path.
    string? TryGetLocalPath(string storagePath) => null;
}
