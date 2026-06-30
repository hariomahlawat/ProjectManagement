using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Application.Security;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Services.Activities;

public sealed class FileSystemActivityAttachmentStorage : IActivityAttachmentStorage
{
    private const string RootFolder = "activities";

    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IFileSecurityValidator _validator;
    private readonly ILogger<FileSystemActivityAttachmentStorage>? _logger;

    public FileSystemActivityAttachmentStorage(IUploadRootProvider uploadRootProvider,
                                               IFileSecurityValidator validator,
                                               ILogger<FileSystemActivityAttachmentStorage>? logger = null)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
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
        var tempFile = Path.GetTempFileName();

        if (upload.Content.CanSeek)
        {
            upload.Content.Seek(0, SeekOrigin.Begin);
        }

        long totalBytes = 0;
        try
        {
            await using (var tempStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await upload.Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }

                    totalBytes += read;
                    await tempStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await tempStream.FlushAsync(cancellationToken);
            }

            await _validator.IsSafeAsync(tempFile, upload.ContentType, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.Move(tempFile, absolutePath, overwrite: true);
            tempFile = string.Empty;
        }
        catch
        {
            SafeDelete(tempFile);
            SafeDelete(absolutePath);
            throw;
        }

        return new ActivityAttachmentStorageResult(storageKey, sanitizedName, totalBytes);
    }


    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.FromResult<Stream?>(null);
        }

        var absolutePath = ResolveAbsolutePath(storageKey);
        if (!File.Exists(absolutePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
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
        var normalizedKey = storageKey.Replace('\\', '/').TrimStart('/');
        if (!normalizedKey.StartsWith(RootFolder + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Activity attachment storage key is outside the activities root.");
        }

        var root = Path.GetFullPath(_uploadRootProvider.RootPath);
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Activity attachment path escapes the configured upload root.");
        }

        return candidate;
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
