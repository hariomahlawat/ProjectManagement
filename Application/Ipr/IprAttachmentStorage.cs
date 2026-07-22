using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Security;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Storage;
using UglyToad.PdfPig;

namespace ProjectManagement.Application.Ipr;

public sealed class IprAttachmentStorage
{
    private const string PdfContentType = "application/pdf";
    private const int MaxDisplayFileNameLength = 240;

    private readonly string _storageFolderName;
    private readonly string _storageRoot;
    private readonly string _protectedStorageRoot;
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IFileSecurityValidator? _fileSecurityValidator;
    private readonly IprAttachmentOptions _options;
    private readonly ILogger<IprAttachmentStorage>? _logger;

    public IprAttachmentStorage(
        IUploadRootProvider uploadRootProvider,
        IUploadPathResolver pathResolver,
        IOptions<IprAttachmentOptions> options,
        IFileSecurityValidator? fileSecurityValidator = null,
        ILogger<IprAttachmentStorage>? logger = null)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _ = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _fileSecurityValidator = fileSecurityValidator;
        _logger = logger;

        var sanitizedFolder = string.IsNullOrWhiteSpace(_options.StorageFolderName)
            ? "ipr-attachments"
            : _options.StorageFolderName.Trim().Trim('/', '\\');

        if (string.IsNullOrWhiteSpace(_options.StorageRoot))
        {
            _storageFolderName = sanitizedFolder;
            _storageRoot = Path.GetFullPath(Path.Combine(_uploadRootProvider.RootPath, sanitizedFolder));
        }
        else
        {
            _storageFolderName = string.Empty;
            _storageRoot = ResolveStorageRoot(_options.StorageRoot, _uploadRootProvider.RootPath);
        }

        _protectedStorageRoot = EnsureTrailingSeparator(_storageRoot);
    }

    public async Task<IprAttachmentStorageResult> SaveAsync(
        int iprId,
        Stream content,
        string originalFileName,
        string claimedContentType,
        long maxFileSizeBytes,
        CancellationToken cancellationToken)
    {
        if (iprId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iprId));
        }

        ArgumentNullException.ThrowIfNull(content);

        var sanitizedName = SanitizeFileName(originalFileName);
        EnsureExtensionAllowed(sanitizedName);

        Directory.CreateDirectory(_storageRoot);
        var stagingFolder = Path.Combine(_storageRoot, ".staging");
        Directory.CreateDirectory(stagingFolder);

        var tempPath = Path.Combine(stagingFolder, $"{Guid.NewGuid():N}.upload");
        string? finalPath = null;

        try
        {
            var totalBytes = await CopyToTemporaryFileAsync(
                content,
                tempPath,
                maxFileSizeBytes,
                cancellationToken);

            if (totalBytes == 0)
            {
                throw new InvalidOperationException("The selected attachment is empty.");
            }

            await ValidatePdfAsync(tempPath, claimedContentType, cancellationToken);

            var storageKey = BuildStorageKey(iprId);
            finalPath = ResolveAbsolutePath(storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(tempPath, finalPath, overwrite: false);
            tempPath = string.Empty;

            return new IprAttachmentStorageResult(
                storageKey,
                sanitizedName,
                totalBytes,
                PdfContentType);
        }
        catch
        {
            SafeDelete(tempPath);
            SafeDelete(finalPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var absolutePath = ResolveAbsolutePath(storageKey);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("The IPR attachment could not be found.", absolutePath);
        }

        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);

        return Task.FromResult(stream);
    }

    public void Delete(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }

        try
        {
            var absolutePath = ResolveAbsolutePath(storageKey);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete IPR attachment for storage key '{StorageKey}'.", storageKey);
        }
    }

    private async Task ValidatePdfAsync(
        string filePath,
        string claimedContentType,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        {
            var header = new byte[5];
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            if (bytesRead != header.Length ||
                header[0] != (byte)'%' ||
                header[1] != (byte)'P' ||
                header[2] != (byte)'D' ||
                header[3] != (byte)'F' ||
                header[4] != (byte)'-')
            {
                throw new InvalidOperationException("The selected file is not a valid PDF document.");
            }
        }

        try
        {
            using var document = PdfDocument.Open(filePath);
            if (document.NumberOfPages <= 0)
            {
                throw new InvalidOperationException("The selected PDF does not contain any pages.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Rejected malformed IPR PDF attachment '{FileName}'.", Path.GetFileName(filePath));
            throw new InvalidOperationException("The selected file is not a readable PDF document.", ex);
        }

        if (_fileSecurityValidator is not null &&
            !await _fileSecurityValidator.IsSafeAsync(filePath, PdfContentType, cancellationToken))
        {
            throw new InvalidOperationException("The selected file failed security validation.");
        }

        if (!string.IsNullOrWhiteSpace(claimedContentType) &&
            !string.Equals(claimedContentType, PdfContentType, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(claimedContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation(
                "IPR attachment content type '{ClaimedContentType}' was normalised to application/pdf after content validation.",
                claimedContentType);
        }
    }

    private static async Task<long> CopyToTemporaryFileAsync(
        Stream content,
        string tempPath,
        long maxFileSizeBytes,
        CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        long totalBytes = 0;
        await using var destination = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

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
                throw new InvalidOperationException(
                    $"Attachment exceeds maximum allowed size of {FormatSize(maxFileSizeBytes)}.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        return totalBytes;
    }

    private void EnsureExtensionAllowed(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var allowedExtensions = _options.AllowedExtensions is { Count: > 0 }
            ? _options.AllowedExtensions
            : new List<string> { ".pdf" };

        if (string.IsNullOrWhiteSpace(extension) ||
            !allowedExtensions.Any(item => string.Equals(NormalizeExtension(item), extension, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Only PDF attachments are allowed.");
        }
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        string fullPath;
        if (Path.IsPathRooted(storageKey))
        {
            // Backward compatibility for legacy custom-root rows. Rooted keys are
            // accepted only when they remain inside the configured IPR root.
            fullPath = Path.GetFullPath(storageKey);
        }
        else
        {
            var normalized = storageKey
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            if (ContainsTraversalSegment(normalized))
            {
                throw new InvalidOperationException("Invalid IPR attachment storage key.");
            }

            if (!string.IsNullOrEmpty(_storageFolderName) &&
                StartsWithFolder(normalized, _storageFolderName))
            {
                fullPath = Path.GetFullPath(Path.Combine(_uploadRootProvider.RootPath, normalized));
            }
            else
            {
                fullPath = Path.GetFullPath(Path.Combine(_storageRoot, normalized));
            }
        }

        if (!IsWithinStorageRoot(fullPath))
        {
            throw new InvalidOperationException("IPR attachment path resolved outside the configured storage root.");
        }

        return fullPath;
    }

    private string BuildStorageKey(int iprId)
    {
        var storedFileName = $"{Guid.NewGuid():N}.pdf";
        var relative = Path.Combine(
            iprId.ToString(CultureInfo.InvariantCulture),
            storedFileName);

        if (!string.IsNullOrEmpty(_storageFolderName))
        {
            relative = Path.Combine(_storageFolderName, relative);
        }

        return relative.Replace('\\', '/');
    }

    private bool IsWithinStorageRoot(string fullPath)
    {
        var canonical = Path.GetFullPath(fullPath);
        return string.Equals(canonical, _storageRoot, StringComparison.OrdinalIgnoreCase) ||
               canonical.StartsWith(_protectedStorageRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithFolder(string path, string folder)
    {
        var firstSeparator = path.IndexOf(Path.DirectorySeparatorChar);
        var firstSegment = firstSeparator >= 0 ? path[..firstSeparator] : path;
        return string.Equals(firstSegment, folder, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTraversalSegment(string path)
    {
        return path
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static string SanitizeFileName(string original)
    {
        var safe = string.IsNullOrWhiteSpace(original)
            ? "attachment.pdf"
            : Path.GetFileName(original.Trim());

        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "attachment.pdf";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        if (safe.Length <= MaxDisplayFileNameLength)
        {
            return safe;
        }

        var extension = Path.GetExtension(safe);
        var stemLength = Math.Max(1, MaxDisplayFileNameLength - extension.Length);
        return safe[..stemLength] + extension;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim();
        return trimmed.StartsWith(".", StringComparison.Ordinal) ? trimmed : $".{trimmed}";
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

    private static void SafeDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best effort and must not hide the original failure.
        }
    }

    private static string ResolveStorageRoot(string configuredRoot, string uploadRoot)
    {
        var candidate = configuredRoot.Trim();
        if (!Path.IsPathRooted(candidate))
        {
            candidate = Path.Combine(uploadRoot, candidate);
        }

        return Path.GetFullPath(candidate);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
