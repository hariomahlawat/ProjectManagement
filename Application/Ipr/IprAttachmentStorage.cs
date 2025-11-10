using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Application.Ipr;

public sealed class IprAttachmentStorage
{
    private const string RootFolder = "ipr-attachments";

    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly ILogger<IprAttachmentStorage>? _logger;

    public IprAttachmentStorage(IUploadRootProvider uploadRootProvider, ILogger<IprAttachmentStorage>? logger = null)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
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

        return new IprAttachmentStorageResult(storageKey, sanitizedName, totalBytes);
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
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_uploadRootProvider.RootPath, relative);
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

    private static string BuildStorageKey(int iprId, string fileName)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var uniqueName = new StringBuilder(timestamp.Length + 1 + 32 + 1 + fileName.Length);
        uniqueName.Append(timestamp);
        uniqueName.Append('-');
        uniqueName.Append(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        uniqueName.Append('-');
        uniqueName.Append(fileName);

        return Path.Combine(RootFolder, iprId.ToString(CultureInfo.InvariantCulture), uniqueName.ToString())
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
}
