using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Production-safe bulk exporter for the unified Photos library.
///
/// The ZIP is deliberately built into a private temporary file and fully finalised before
/// ASP.NET Core receives a stream to send. ZipArchive.Dispose performs synchronous central-
/// directory writes; doing that against Response.Body is invalid when Kestrel synchronous
/// I/O is disabled (the normal ASP.NET Core configuration).
/// </summary>
public sealed class MediaBulkDownloadService : IMediaBulkDownloadService
{
    private const int CopyBufferSize = 128 * 1024;

    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IMediaContentProviderResolver _contentResolver;
    private readonly IMediaCachePathResolver _cachePaths;
    private readonly MediaBulkDownloadOptions _options;
    private readonly ILogger<MediaBulkDownloadService> _logger;

    public MediaBulkDownloadService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IMediaContentProviderResolver contentResolver,
        IMediaCachePathResolver cachePaths,
        IOptions<MediaLibraryOptions> options,
        ILogger<MediaBulkDownloadService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
        _cachePaths = cachePaths ?? throw new ArgumentNullException(nameof(cachePaths));
        _options = options?.Value.BulkDownload ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaBulkDownloadResult> CreateAsync(
        IReadOnlyCollection<long> assetIds,
        string requestedByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        requestedByUserId = string.IsNullOrWhiteSpace(requestedByUserId)
            ? "unknown"
            : requestedByUserId.Trim();

        var stopwatch = Stopwatch.StartNew();
        var maximumItems = Math.Clamp(_options.MaxItems, 1, 500);
        var maximumSourceBytes = Math.Max(1L, _options.MaxSourceBytes);
        var requestedIds = assetIds
            .Where(id => id > 0)
            .Distinct()
            .Take(maximumItems + 1)
            .ToArray();

        if (requestedIds.Length == 0)
        {
            return Failure(
                MediaBulkDownloadFailureReason.EmptySelection,
                "Select at least one catalogue-backed media item.",
                requestedByUserId,
                stopwatch,
                requestedCount: 0);
        }

        if (requestedIds.Length > maximumItems)
        {
            return Failure(
                MediaBulkDownloadFailureReason.TooManyItems,
                $"A maximum of {maximumItems} media items can be downloaded at once.",
                requestedByUserId,
                stopwatch,
                requestedCount: requestedIds.Length);
        }

        var assets = await _visibility
            .Apply(_db.Assets.AsNoTracking().Include(asset => asset.Source))
            .Where(asset => requestedIds.Contains(asset.Id))
            .OrderBy(asset => asset.MediaDateUtc)
            .ThenBy(asset => asset.Id)
            .ToListAsync(cancellationToken);

        if (assets.Count == 0)
        {
            return Failure(
                MediaBulkDownloadFailureReason.NoEligibleAssets,
                "The selected media is no longer available.",
                requestedByUserId,
                stopwatch,
                requestedIds.Length);
        }

        var resolved = new List<ResolvedDownloadItem>(assets.Count);
        long knownSourceBytes = 0;
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var content = await _contentResolver.ResolveAsync(asset, cancellationToken);
                if (content is null)
                {
                    continue;
                }

                if (content.Length is > 0)
                {
                    if (knownSourceBytes > maximumSourceBytes - content.Length.Value)
                    {
                        return Failure(
                            MediaBulkDownloadFailureReason.SourceBytesExceeded,
                            BuildSizeLimitMessage(maximumSourceBytes),
                            requestedByUserId,
                            stopwatch,
                            requestedIds.Length,
                            assets.Count,
                            sourceBytes: knownSourceBytes);
                    }

                    knownSourceBytes += content.Length.Value;
                }

                resolved.Add(new ResolvedDownloadItem(asset.Id, content));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Resolution happens before any ZIP entry is created, so a stale physical
                // source can be skipped without ever producing a partial archive entry.
                _logger.LogWarning(
                    exception,
                    "Unable to resolve media asset {AssetId} for Photos bulk download requested by {UserId}.",
                    asset.Id,
                    requestedByUserId);
            }
        }

        if (resolved.Count == 0)
        {
            return Failure(
                MediaBulkDownloadFailureReason.NoReadableAssets,
                "None of the selected media files could be opened.",
                requestedByUserId,
                stopwatch,
                requestedIds.Length,
                assets.Count,
                skippedCount: assets.Count);
        }

        var temporaryDirectory = Path.Combine(_cachePaths.CacheRoot, "bulk-downloads");
        Directory.CreateDirectory(temporaryDirectory);
        CleanupAbandonedArchives(temporaryDirectory);
        var temporaryPath = Path.Combine(
            temporaryDirectory,
            $"bulk-{Guid.NewGuid():N}.zip.partial");

        var includedCount = 0;
        var skippedCount = assets.Count - resolved.Count;
        long actualSourceBytes = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using (var archiveStream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                // ZipArchive has synchronous finalisation on Dispose. That is safe here
                // because the target is a private FileStream, never the HTTP response body.
                using (var archive = new ZipArchive(
                           archiveStream,
                           ZipArchiveMode.Create,
                           leaveOpen: true,
                           Encoding.UTF8))
                {
                    foreach (var item in resolved)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        Stream? source = null;
                        try
                        {
                            // Open before creating the entry. If the source disappeared
                            // after descriptor resolution, skip it cleanly with no ZIP entry.
                            source = await item.Content.OpenReadAsync(cancellationToken);
                            if (source is null || !source.CanRead)
                            {
                                if (source is not null)
                                {
                                    await source.DisposeAsync();
                                }

                                source = null;
                                skippedCount++;
                                continue;
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            if (source is not null)
                            {
                                await source.DisposeAsync();
                            }

                            skippedCount++;
                            _logger.LogWarning(
                                exception,
                                "Media asset {AssetId} became unavailable before its ZIP entry was created for user {UserId}.",
                                item.AssetId,
                                requestedByUserId);
                            continue;
                        }

                        await using var ownedSource = source!;
                        var entryName = MakeUniqueArchiveName(
                            item.Content.FileName,
                            item.AssetId,
                            usedNames);
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

                        try
                        {
                            await using var target = entry.Open();
                            actualSourceBytes = await CopyWithLimitAsync(
                                ownedSource,
                                target,
                                actualSourceBytes,
                                maximumSourceBytes,
                                cancellationToken);
                            includedCount++;
                        }
                        catch (MediaBulkDownloadSizeLimitException)
                        {
                            throw;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            // Once an entry exists, a failed mid-copy can leave a partial
                            // member. Abort the whole archive rather than return a ZIP that
                            // knowingly contains truncated media.
                            throw new MediaBulkDownloadBuildException(
                                item.AssetId,
                                "A selected media file became unavailable while the archive was being created.",
                                exception);
                        }
                    }
                }

                await archiveStream.FlushAsync(cancellationToken);
            }

            if (includedCount == 0)
            {
                TryDelete(temporaryPath);
                return Failure(
                    MediaBulkDownloadFailureReason.NoReadableAssets,
                    "None of the selected media files could be opened.",
                    requestedByUserId,
                    stopwatch,
                    requestedIds.Length,
                    assets.Count,
                    includedCount,
                    skippedCount,
                    actualSourceBytes);
            }

            var archiveLength = new FileInfo(temporaryPath).Length;
            var reader = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                CopyBufferSize,
                FileOptions.Asynchronous
                | FileOptions.SequentialScan
                | FileOptions.DeleteOnClose);

            var archiveName = $"PRISM_Photos_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            var archiveResult = new MediaBulkDownloadArchive(
                reader,
                archiveName,
                archiveLength,
                requestedIds.Length,
                assets.Count,
                includedCount,
                skippedCount,
                actualSourceBytes);

            _logger.LogInformation(
                "Photos bulk download created. UserId={UserId} RequestedCount={RequestedCount} EligibleCount={EligibleCount} IncludedCount={IncludedCount} SkippedCount={SkippedCount} SourceBytes={SourceBytes} ArchiveBytes={ArchiveBytes} ElapsedMilliseconds={ElapsedMilliseconds}.",
                requestedByUserId,
                requestedIds.Length,
                assets.Count,
                includedCount,
                skippedCount,
                actualSourceBytes,
                archiveLength,
                stopwatch.ElapsedMilliseconds);

            return MediaBulkDownloadResult.Success(archiveResult);
        }
        catch (MediaBulkDownloadSizeLimitException)
        {
            TryDelete(temporaryPath);
            return Failure(
                MediaBulkDownloadFailureReason.SourceBytesExceeded,
                BuildSizeLimitMessage(maximumSourceBytes),
                requestedByUserId,
                stopwatch,
                requestedIds.Length,
                assets.Count,
                includedCount,
                skippedCount,
                actualSourceBytes);
        }
        catch (MediaBulkDownloadBuildException exception)
        {
            TryDelete(temporaryPath);
            _logger.LogError(
                exception,
                "Photos bulk download aborted because asset {AssetId} failed during archive creation. UserId={UserId} RequestedCount={RequestedCount} EligibleCount={EligibleCount} IncludedCount={IncludedCount} SourceBytes={SourceBytes} ElapsedMilliseconds={ElapsedMilliseconds}.",
                exception.AssetId,
                requestedByUserId,
                requestedIds.Length,
                assets.Count,
                includedCount,
                actualSourceBytes,
                stopwatch.ElapsedMilliseconds);
            return MediaBulkDownloadResult.Failure(
                MediaBulkDownloadFailureReason.SourceReadFailed,
                "One selected media file changed or became unavailable while the ZIP was being created. Refresh Photos and try again.",
                requestedIds.Length,
                assets.Count,
                includedCount,
                skippedCount,
                actualSourceBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(temporaryPath);
            _logger.LogInformation(
                "Photos bulk download cancelled. UserId={UserId} RequestedCount={RequestedCount} EligibleCount={EligibleCount} IncludedCount={IncludedCount} SourceBytes={SourceBytes} ElapsedMilliseconds={ElapsedMilliseconds}.",
                requestedByUserId,
                requestedIds.Length,
                assets.Count,
                includedCount,
                actualSourceBytes,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private MediaBulkDownloadResult Failure(
        MediaBulkDownloadFailureReason reason,
        string message,
        string requestedByUserId,
        Stopwatch stopwatch,
        int requestedCount,
        int eligibleCount = 0,
        int includedCount = 0,
        int skippedCount = 0,
        long sourceBytes = 0)
    {
        _logger.LogWarning(
            "Photos bulk download not created. UserId={UserId} FailureReason={FailureReason} RequestedCount={RequestedCount} EligibleCount={EligibleCount} IncludedCount={IncludedCount} SkippedCount={SkippedCount} SourceBytes={SourceBytes} ElapsedMilliseconds={ElapsedMilliseconds}.",
            requestedByUserId,
            reason,
            requestedCount,
            eligibleCount,
            includedCount,
            skippedCount,
            sourceBytes,
            stopwatch.ElapsedMilliseconds);
        return MediaBulkDownloadResult.Failure(
            reason,
            message,
            requestedCount,
            eligibleCount,
            includedCount,
            skippedCount,
            sourceBytes);
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long alreadyCopied,
        long maximumSourceBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            var total = alreadyCopied;
            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, CopyBufferSize),
                    cancellationToken);
                if (read == 0)
                {
                    return total;
                }

                if (total > maximumSourceBytes - read)
                {
                    throw new MediaBulkDownloadSizeLimitException();
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string BuildSizeLimitMessage(long maximumSourceBytes)
        => $"The selected media exceeds the bulk-download limit of {FormatBytes(maximumSourceBytes)}. Select fewer items and try again.";

    private static string FormatBytes(long bytes)
    {
        const double unit = 1024d;
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (unit * unit * unit):0.#} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (unit * unit):0.#} MB";
        }

        return $"{Math.Max(1, bytes / unit):0} KB";
    }

    private static string MakeUniqueArchiveName(
        string? preferredName,
        long assetId,
        ISet<string> usedNames)
    {
        var candidate = SanitizeArchiveFileName(preferredName, assetId);
        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(candidate);
        var stem = Path.GetFileNameWithoutExtension(candidate);
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var unique = LimitArchiveFileName($"{stem} ({suffix}){extension}");
            if (usedNames.Add(unique))
            {
                return unique;
            }
        }

        var fallback = LimitArchiveFileName($"{stem}-{assetId}{extension}");
        usedNames.Add(fallback);
        return fallback;
    }

    private static string SanitizeArchiveFileName(string? preferredName, long assetId)
    {
        var raw = string.IsNullOrWhiteSpace(preferredName)
            ? $"media-{assetId}"
            : preferredName.Trim();

        // ZIP entry paths use '/'. Normalising both separator styles before taking the
        // basename prevents path traversal when an archive is later extracted on Windows
        // or Linux, irrespective of the operating system that generated the ZIP.
        raw = raw.Replace('\\', '/');
        var separator = raw.LastIndexOf('/');
        var candidate = separator >= 0 ? raw[(separator + 1)..] : raw;

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        };
        var builder = new StringBuilder(candidate.Length);
        foreach (var character in candidate)
        {
            builder.Append(character < 32 || invalid.Contains(character) ? '_' : character);
        }

        candidate = builder.ToString().Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(candidate) || candidate is "." or "..")
        {
            candidate = $"media-{assetId}";
        }

        return LimitArchiveFileName(candidate);
    }

    private static string LimitArchiveFileName(string candidate)
    {
        const int maximumLength = 180;
        if (candidate.Length <= maximumLength)
        {
            return candidate;
        }

        var extension = Path.GetExtension(candidate);
        if (extension.Length > 20)
        {
            extension = extension[..20];
        }

        var stem = Path.GetFileNameWithoutExtension(candidate);
        var stemLength = Math.Max(1, maximumLength - extension.Length);
        return stem[..Math.Min(stem.Length, stemLength)] + extension;
    }

    private void CleanupAbandonedArchives(string temporaryDirectory)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (var path in Directory.EnumerateFiles(temporaryDirectory, "*.zip.partial")
                         .Take(100))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff)
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    // Another request/process may still own the file. Leave it alone.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort maintenance; download creation must not fail because an
                    // old cache file cannot currently be removed.
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Unable to complete best-effort cleanup of abandoned Photos bulk-download archives in {Directory}.",
                temporaryDirectory);
        }
    }

    private static void TryDelete(string path)
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
            // Best-effort cleanup only. The cache directory remains private and a future
            // maintenance sweep can remove abandoned *.partial files if the OS holds a lock.
        }
    }

    private sealed record ResolvedDownloadItem(long AssetId, MediaContentDescriptor Content);

    private sealed class MediaBulkDownloadSizeLimitException : Exception
    {
    }

    private sealed class MediaBulkDownloadBuildException : Exception
    {
        public MediaBulkDownloadBuildException(long assetId, string message, Exception innerException)
            : base(message, innerException)
            => AssetId = assetId;

        public long AssetId { get; }
    }
}
