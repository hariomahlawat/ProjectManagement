using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Application.Ipr;

public sealed class IprAttachmentStorage
{
    private readonly string _storageFolderName;
    private readonly string _storageRoot;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IUploadPathResolver _pathResolver;
    private readonly ILogger<IprAttachmentStorage>? _logger;

    public IprAttachmentStorage(IUploadRootProvider uploadRootProvider,
                                IUploadPathResolver pathResolver,
                                IOptions<IprAttachmentOptions> options,
                                ILogger<IprAttachmentStorage>? logger = null)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var value = options.Value ?? new IprAttachmentOptions();
        var sanitizedFolder = string.IsNullOrWhiteSpace(value.StorageFolderName)
            ? "ipr-attachments"
            : value.StorageFolderName.Trim().Trim('/', '\\');

        if (string.IsNullOrWhiteSpace(value.StorageRoot))
        {
            _storageFolderName = sanitizedFolder;
            _storageRoot = Path.GetFullPath(Path.Combine(uploadRootProvider.RootPath, sanitizedFolder));
        }
        else
        {
            _storageFolderName = string.Empty;
            _storageRoot = ResolveStorageRoot(value.StorageRoot);
        }
        _logger = logger;
    }

    public async Task<IprAttachmentStorageResult> SaveAsync(
        int iprId,
        Stream content,
        string originalFileName,
        long maxFileSizeBytes,
        CancellationToken cancellationToken)
    {
        if (iprId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iprId));
        }

        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var sanitizedName = SanitizeFileName(originalFileName);
        var storageKey = BuildStorageKey(iprId, sanitizedName);
        var absolutePath = ResolveAbsolutePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        long totalBytes = 0;
        try
        {
            await using var destination = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                totalBytes += read;
                if (maxFileSizeBytes > 0 && totalBytes > maxFileSizeBytes)
                {
                    throw new InvalidOperationException($"Attachment exceeds maximum allowed size of {FormatSize(maxFileSizeBytes)}.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
        }
        catch
        {
            SafeDelete(absolutePath);
            throw;
        }

        var persistedKey = _pathResolver.ToRelative(absolutePath);
        return new IprAttachmentStorageResult(persistedKey, sanitizedName, totalBytes);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var absolutePath = ResolveAbsolutePath(storageKey);
        Stream stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public void Delete(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }

        var absolutePath = ResolveAbsolutePath(storageKey);
        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete IPR attachment at '{Path}'.", absolutePath);
        }
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        if (Path.IsPathRooted(storageKey))
        {
            return Path.GetFullPath(storageKey);
        }

        if (!string.IsNullOrEmpty(_storageFolderName))
        {
            try
            {
                return _pathResolver.ToAbsolute(storageKey);
            }
            catch
            {
                // Fall back below.
            }
        }

        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_storageRoot, relative));
    }

    private static string SanitizeFileName(string original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return "attachment";
        }

        var safe = Path.GetFileName(original.Trim());
        return string.IsNullOrWhiteSpace(safe) ? "attachment" : safe;
    }

    // SECTION: Storage helpers
    private string BuildStorageKey(int iprId, string fileName)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var uniqueName = new StringBuilder(timestamp.Length + 1 + 32 + 1 + fileName.Length);
        uniqueName.Append(timestamp);
        uniqueName.Append('-');
        uniqueName.Append(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        uniqueName.Append('-');
        uniqueName.Append(fileName);

        var folder = string.IsNullOrWhiteSpace(_storageFolderName)
            ? string.Empty
            : _storageFolderName;

        return Path.Combine(folder, iprId.ToString(CultureInfo.InvariantCulture), uniqueName.ToString())
            .Replace('\\', '/');
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024 * 1024)} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024} KB";
        }

        return $"{bytes} bytes";
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignored on purpose to avoid masking original exception
        }
    }

    private static string ResolveStorageRoot(string configuredRoot)
    {
        return Path.GetFullPath(configuredRoot);
    }
}
