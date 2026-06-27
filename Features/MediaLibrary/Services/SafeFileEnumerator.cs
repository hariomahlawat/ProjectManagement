namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Enumerates one directory at a time and fails the scan on an inaccessible subtree.
/// Failing the scan is deliberate: the caller must not reconcile previously indexed
/// assets as missing when the NAS was only partially readable.
/// </summary>
public sealed class SafeFileEnumerator
{
    private readonly ILogger<SafeFileEnumerator> _logger;

    public SafeFileEnumerator(ILogger<SafeFileEnumerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IEnumerable<string> EnumerateFiles(
        string rootPath,
        bool includeSubfolders,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                _logger.LogError(ex, "Unable to enumerate media files in {Directory}", directory);
                throw;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            if (!includeSubfolders)
            {
                continue;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                _logger.LogError(ex, "Unable to enumerate media directories in {Directory}", directory);
                throw;
            }

            foreach (var child in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
                {
                    _logger.LogError(ex, "Unable to inspect media directory {Directory}", child);
                    throw;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    _logger.LogDebug("Skipping media reparse point {Directory}", child);
                    continue;
                }

                pending.Push(child);
            }
        }
    }
}
