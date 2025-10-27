using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Activities;

public sealed class FileSystemActivityAttachmentStorage : IActivityAttachmentStorage
{
    private const string RootFolder = "activities";

    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly ILogger<FileSystemActivityAttachmentStorage>? _logger;

    public FileSystemActivityAttachmentStorage(IUploadRootProvider uploadRootProvider,
                                               ILogger<FileSystemActivityAttachmentStorage>? logger = null)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _logger = logger;
    }

    public async Task<ActivityAttachmentStorageResult> SaveAsync(
        int activityId,
        ActivityAttachmentUpload upload,
        CancellationToken cancellationToken = default)
    {
        if (activityId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activityId));
        }

        ArgumentNullException.ThrowIfNull(upload);

        var sanitizedName = ActivityAttachmentValidator.SanitizeFileName(upload.FileName);
        var storageKey = BuildStorageKey(activityId, sanitizedName);
        var absolutePath = ResolveAbsolutePath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        if (upload.Content.CanSeek)
        {
            upload.Content.Seek(0, SeekOrigin.Begin);
        }

        long totalBytes = 0;
        try
        {
            await using var destination = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            while (true)
            {
                var read = await upload.Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                totalBytes += read;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
        }
        catch
        {
            SafeDelete(absolutePath);
            throw;
        }

        return new ActivityAttachmentStorageResult(storageKey, sanitizedName, totalBytes);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.CompletedTask;
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
            _logger?.LogWarning(ex, "Failed to delete activity attachment at '{Path}'.", absolutePath);
        }

        return Task.CompletedTask;
    }

    public string GetDownloadUrl(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return string.Empty;
        }

        var normalized = storageKey.Replace('\\', '/');
        return $"/files/{normalized}";
    }

    private static string BuildStorageKey(int activityId, string fileName)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var builder = new StringBuilder(timestamp.Length + 1 + 32 + 1 + fileName.Length);
        builder.Append(timestamp);
        builder.Append('-');
        builder.Append(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        builder.Append('-');
        builder.Append(fileName);

        return Path.Combine(RootFolder, activityId.ToString(CultureInfo.InvariantCulture), builder.ToString())
            .Replace('\\', '/');
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(_uploadRootProvider.RootPath, relative);
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
            // ignored intentionally
        }
    }
}
