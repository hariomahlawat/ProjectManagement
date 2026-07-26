using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Security;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Storage;
using ProjectManagement.Helpers;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Arpp;

public sealed class FileSystemArppAttachmentStorage : IArppAttachmentStorage
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IFileSecurityValidator _fileSecurityValidator;
    private readonly ArppAttachmentOptions _options;
    private readonly ILogger<FileSystemArppAttachmentStorage> _logger;
    private readonly string _rootFolder;

    public FileSystemArppAttachmentStorage(
        IUploadRootProvider uploadRootProvider,
        IFileSecurityValidator fileSecurityValidator,
        IOptions<ArppAttachmentOptions> options,
        ILogger<FileSystemArppAttachmentStorage> logger)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _fileSecurityValidator = fileSecurityValidator ?? throw new ArgumentNullException(nameof(fileSecurityValidator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootFolder = NormalizeRootFolder(_options.StorageFolderName);
    }

    public async Task<ArppStoredAttachment> SaveAsync(
        long issueId,
        string originalFileName,
        string contentType,
        long declaredLength,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (issueId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(issueId));
        }

        ArgumentNullException.ThrowIfNull(content);

        var normalizedContentType = contentType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!string.Equals(normalizedContentType, "application/pdf", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Only PDF files are supported for the issued HQ document.");
        }

        var sanitizedOriginalName = SanitizePdfFileName(originalFileName);
        var maximumBytes = _options.MaxFileSizeBytes > 0
            ? _options.MaxFileSizeBytes
            : 100L * 1024L * 1024L;

        if (declaredLength <= 0)
        {
            throw new InvalidDataException("The selected PDF is empty.");
        }

        if (declaredLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"The PDF exceeds the maximum permitted size of {FileSizeFormatter.FormatFileSize(maximumBytes)}.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"arpp-{Guid.NewGuid():N}.upload");
        string? finalPath = null;

        try
        {
            var actualLength = await CopyWithLimitAsync(
                content,
                tempPath,
                maximumBytes,
                cancellationToken);

            await ValidatePdfSignatureAsync(tempPath, cancellationToken);
            if (!await _fileSecurityValidator.IsSafeAsync(tempPath, normalizedContentType, cancellationToken))
            {
                throw new InvalidDataException("The selected PDF failed the configured security checks.");
            }

            var checksum = await ComputeSha256Async(tempPath, cancellationToken);
            var storageKey = BuildStorageKey(issueId);
            finalPath = ResolveAbsolutePath(storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(tempPath, finalPath, overwrite: false);
            tempPath = string.Empty;

            return new ArppStoredAttachment(
                storageKey,
                sanitizedOriginalName,
                normalizedContentType,
                actualLength,
                checksum);
        }
        catch
        {
            SafeDelete(tempPath);
            SafeDelete(finalPath);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not remove ARPP attachment file '{StorageKey}'. The database record has already been updated.",
                storageKey);
        }

        return Task.CompletedTask;
    }

    private string BuildStorageKey(long issueId)
    {
        var key = $"{_rootFolder}/{issueId}/{Guid.NewGuid():N}.pdf";
        _fileSecurityValidator.ValidateRelativePath(key);
        return key;
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        _fileSecurityValidator.ValidateRelativePath(storageKey);

        var normalizedKey = storageKey.Replace('\\', '/').TrimStart('/');
        if (!normalizedKey.StartsWith(_rootFolder + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The ARPP attachment storage key is outside the configured ARPP root.");
        }

        var uploadRoot = Path.GetFullPath(_uploadRootProvider.RootPath);
        var arppRoot = Path.GetFullPath(Path.Combine(
            uploadRoot,
            _rootFolder.Replace('/', Path.DirectorySeparatorChar)));
        var candidate = Path.GetFullPath(Path.Combine(
            uploadRoot,
            normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = arppRoot.EndsWith(Path.DirectorySeparatorChar)
            ? arppRoot
            : arppRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException("The ARPP attachment path escapes the configured storage root.");
        }

        return candidate;
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream source,
        string destinationPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            source.Seek(0, SeekOrigin.Begin);
        }

        long totalBytes = 0;
        var buffer = new byte[128 * 1024];
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The PDF exceeds the maximum permitted size of {FileSizeFormatter.FormatFileSize(maximumBytes)}.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);

        if (totalBytes == 0)
        {
            throw new InvalidDataException("The selected PDF is empty.");
        }

        return totalBytes;
    }

    private static async Task ValidatePdfSignatureAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var header = new byte[PdfSignature.Length];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);

        if (read != PdfSignature.Length || !header.AsSpan().SequenceEqual(PdfSignature))
        {
            throw new InvalidDataException("The selected file does not contain a valid PDF signature.");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SanitizePdfFileName(string? originalFileName)
    {
        var fileName = Path.GetFileName(originalFileName ?? string.Empty);
        fileName = FileNameSanitizer.Sanitize(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "ARPP-issued-document";
        }

        var maximumStemLength = 250;
        if (stem.Length > maximumStemLength)
        {
            stem = stem[..maximumStemLength];
        }

        return stem + ".pdf";
    }

    private static string NormalizeRootFolder(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? "arpp" : configured.Trim();
        value = value.Replace('\\', '/').Trim('/');
        if (value.Length == 0 || value.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("ArppAttachments:StorageFolderName must be a safe relative folder.");
        }

        return value;
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
            // Best-effort compensation; the primary exception remains authoritative.
        }
    }
}
