namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Performs a deliberately bounded connection/read probe. It never turns a health check
/// into a full archive enumeration.
/// </summary>
public sealed class FileSystemSourceHealthService : IFileSystemSourceHealthService
{
    private const int MaximumDirectoriesToInspect = 32;
    private const int MaximumFilesToInspect = 500;
    private const int MaximumMediaSamples = 25;

    private readonly IFileSystemPathResolver _pathResolver;
    private readonly ILogger<FileSystemSourceHealthService> _logger;

    public FileSystemSourceHealthService(
        IFileSystemPathResolver pathResolver,
        ILogger<FileSystemSourceHealthService> logger)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<FileSystemSourceHealth> TestAsync(
        string rootPath,
        bool includeSubfolders,
        IReadOnlyCollection<string> allowedExtensions,
        CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            var root = _pathResolver.ResolveRoot(rootPath);
            if (!Directory.Exists(root))
            {
                return Task.FromResult(new FileSystemSourceHealth(
                    false,
                    _pathResolver.DescribePathKind(root),
                    0,
                    "The folder is not reachable by the PRISM worker account.",
                    checkedAt));
            }

            var normalized = allowedExtensions
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(NormalizeExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (normalized.Count == 0)
            {
                return Task.FromResult(new FileSystemSourceHealth(
                    false,
                    _pathResolver.DescribePathKind(root),
                    0,
                    "No supported media extensions are configured.",
                    checkedAt));
            }

            var sampleCount = CountBoundedMediaSample(
                root,
                includeSubfolders,
                normalized,
                cancellationToken);

            return Task.FromResult(new FileSystemSourceHealth(
                true,
                _pathResolver.DescribePathKind(root),
                sampleCount,
                sampleCount == 0
                    ? "The folder is reachable. No supported media was found in the bounded sample."
                    : $"The folder is reachable. {sampleCount} supported media file(s) were found in the bounded sample.",
                checkedAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or DirectoryNotFoundException
                                   or InvalidOperationException
                                   or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "External media source health test failed for {RootPath}", rootPath);
            return Task.FromResult(new FileSystemSourceHealth(
                false,
                SafeDescribePathKind(rootPath),
                0,
                ex.GetBaseException().Message,
                checkedAt));
        }
    }

    private static int CountBoundedMediaSample(
        string root,
        bool includeSubfolders,
        IReadOnlySet<string> allowedExtensions,
        CancellationToken cancellationToken)
    {
        var directories = new Queue<string>();
        directories.Enqueue(root);
        var inspectedDirectories = 0;
        var inspectedFiles = 0;
        var mediaCount = 0;

        while (directories.Count > 0
               && inspectedDirectories < MaximumDirectoriesToInspect
               && inspectedFiles < MaximumFilesToInspect
               && mediaCount < MaximumMediaSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Dequeue();
            inspectedDirectories++;

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inspectedFiles++;
                if (allowedExtensions.Contains(Path.GetExtension(file)))
                {
                    mediaCount++;
                    if (mediaCount >= MaximumMediaSamples)
                    {
                        break;
                    }
                }

                if (inspectedFiles >= MaximumFilesToInspect)
                {
                    break;
                }
            }

            if (!includeSubfolders || inspectedDirectories >= MaximumDirectoriesToInspect)
            {
                continue;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    directories.Enqueue(child);
                }

                if (directories.Count + inspectedDirectories >= MaximumDirectoriesToInspect)
                {
                    break;
                }
            }
        }

        return mediaCount;
    }

    private string SafeDescribePathKind(string path)
    {
        try
        {
            return _pathResolver.DescribePathKind(path);
        }
        catch
        {
            return "File-system folder";
        }
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim().ToLowerInvariant();
        return value.StartsWith('.') ? value : $".{value}";
    }
}
