using ProjectManagement.Features.MediaLibrary.Options;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaCacheHealthResult(
    bool IsHealthy,
    string CacheRoot,
    bool DirectoryExists,
    bool CanWrite,
    long? FreeSpaceBytes,
    string Message,
    DateTimeOffset CheckedAtUtc);

public interface IMediaCacheHealthService
{
    Task<MediaCacheHealthResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class MediaCacheHealthService : IMediaCacheHealthService
{
    private readonly IMediaCachePathResolver _paths;
    private readonly MediaLibraryOptions _options;

    public MediaCacheHealthService(
        IMediaCachePathResolver paths,
        IOptions<MediaLibraryOptions> options)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<MediaCacheHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        var root = _paths.CacheRoot;
        try
        {
            Directory.CreateDirectory(root);
            var probePath = Path.Combine(root, $".write-probe-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(probePath, "PRISM media cache write test", cancellationToken);
                await using var stream = new FileStream(
                    probePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1];
                _ = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            }
            finally
            {
                try { if (File.Exists(probePath)) File.Delete(probePath); }
                catch { /* Health result concerns write access; cleanup is best effort. */ }
            }

            long? freeSpace = null;
            try
            {
                var pathRoot = Path.GetPathRoot(Path.GetFullPath(root));
                if (!string.IsNullOrWhiteSpace(pathRoot))
                {
                    freeSpace = new DriveInfo(pathRoot).AvailableFreeSpace;
                }
            }
            catch
            {
                // UNC and virtual paths may not expose drive information.
            }

            return new MediaCacheHealthResult(
                true,
                root,
                Directory.Exists(root),
                true,
                freeSpace,
                "The media cache is writable.",
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or DirectoryNotFoundException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return new MediaCacheHealthResult(
                false,
                root,
                Directory.Exists(root),
                false,
                null,
                $"The media cache is not writable: {Trim(ex.GetBaseException().Message, 512)}",
                DateTimeOffset.UtcNow);
        }
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
