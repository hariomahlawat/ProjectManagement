using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Incrementally indexes a local or UNC folder. The historic filename is retained so the
/// upgrade can overwrite the prototype without requiring a manual file deletion.
/// </summary>
public sealed class FileSystemMediaSourceScanner : IExternalMediaSourceScanner
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IFileSystemPathResolver _pathResolver;
    private readonly SafeFileEnumerator _enumerator;
    private readonly IMediaMetadataReader _metadataReader;
    private readonly ILogger<FileSystemMediaSourceScanner> _logger;

    public FileSystemMediaSourceScanner(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IFileSystemPathResolver pathResolver,
        SafeFileEnumerator enumerator,
        IMediaMetadataReader metadataReader,
        ILogger<FileSystemMediaSourceScanner> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ScanAsync(Guid sourceId, string workerId, CancellationToken cancellationToken)
    {
        if (!_options.IsExternalSourceFeatureEnabled)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var leaseDuration = TimeSpan.FromMinutes(_options.ExternalSources.ScanLeaseMinutes);
        if (!await TryAcquireLeaseAsync(sourceId, workerId, leaseDuration, cancellationToken))
        {
            _logger.LogDebug("External media source {SourceId} is already being scanned", sourceId);
            return;
        }

        MediaLibrarySource? source = null;
        try
        {
            source = await _db.Sources.SingleOrDefaultAsync(item => item.Id == sourceId, cancellationToken);
            if (source is null
                || source.IsDeleted
                || !source.IsEnabled
                || source.SourceType != MediaLibrarySourceType.FileSystem)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(source.RootPath))
            {
                throw new InvalidOperationException("The external media source has no folder path.");
            }

            var scanId = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow;
            source.LastScanStartedAtUtc = started;
            source.ScanStatus = "Scanning";
            source.LastError = null;
            source.HealthStatus = "Checking";
            source.HealthMessage = null;
            await _db.SaveChangesAsync(cancellationToken);

            var root = _pathResolver.ResolveRoot(source.RootPath);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"The folder '{root}' is not reachable by the PRISM worker account.");
            }

            var allowed = ParseExtensions(source.AllowedExtensionsJson)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (allowed.Count == 0)
            {
                throw new InvalidOperationException("The source has no allowed media extensions.");
            }

            var batchSize = _options.ExternalSources.ScanBatchSize;
            var batch = new List<FileCandidate>(batchSize);
            var nextLeaseRenewalUtc = DateTimeOffset.UtcNow.AddTicks(leaseDuration.Ticks / 2);

            foreach (var fullPath in _enumerator.EnumerateFiles(root, source.IncludeSubfolders, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DateTimeOffset.UtcNow >= nextLeaseRenewalUtc)
                {
                    await RenewLeaseAsync(sourceId, workerId, leaseDuration, cancellationToken);
                    nextLeaseRenewalUtc = DateTimeOffset.UtcNow.AddTicks(leaseDuration.Ticks / 2);
                }

                var extension = Path.GetExtension(fullPath);
                if (!allowed.Contains(extension))
                {
                    continue;
                }

                FileInfo file;
                try
                {
                    file = new FileInfo(fullPath);
                    if (!file.Exists)
                    {
                        continue;
                    }

                    var attributes = file.Attributes;
                    if ((attributes & (FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    _logger.LogError(ex, "Unable to inspect external media file {Path}", fullPath);
                    throw;
                }

                var relative = _pathResolver.ToRelativePath(root, fullPath);
                if (relative.Length > 2048)
                {
                    _logger.LogWarning(
                        "Skipping external media path longer than the catalogue limit ({Length}): {Path}",
                        relative.Length,
                        fullPath);
                    continue;
                }

                var normalizedPath = OperatingSystem.IsWindows()
                    ? relative.ToUpperInvariant()
                    : relative;
                var sourceEntityId = normalizedPath.Length <= 1024
                    ? normalizedPath
                    : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
                var fingerprint = $"{file.Length:X}-{file.LastWriteTimeUtc.Ticks:X}";
                batch.Add(new FileCandidate(fullPath, relative, sourceEntityId, fingerprint));

                if (batch.Count >= batchSize)
                {
                    await RenewLeaseAsync(sourceId, workerId, leaseDuration, cancellationToken);
                    await ProcessBatchAsync(source, scanId, batch, cancellationToken);
                    batch.Clear();
                    await RenewLeaseAsync(sourceId, workerId, leaseDuration, cancellationToken);
                    nextLeaseRenewalUtc = DateTimeOffset.UtcNow.AddTicks(leaseDuration.Ticks / 2);
                }
            }

            if (batch.Count > 0)
            {
                await RenewLeaseAsync(sourceId, workerId, leaseDuration, cancellationToken);
                await ProcessBatchAsync(source, scanId, batch, cancellationToken);
                await RenewLeaseAsync(sourceId, workerId, leaseDuration, cancellationToken);
            }

            // Reconcile only after a complete, successful enumeration. A network outage or
            // inaccessible subtree must never make the catalogue mark the archive missing.
            await _db.Assets
                .Where(asset => asset.SourceId == source.Id
                                && asset.LastSeenScanId != scanId
                                && asset.IsAvailable)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(asset => asset.IsAvailable, false)
                    .SetProperty(asset => asset.LastSeenAtUtc, DateTimeOffset.UtcNow), cancellationToken);

            source.IndexedAssetCount = await _db.Assets.CountAsync(
                asset => asset.SourceId == source.Id && asset.IsAvailable && !asset.IsDeleted,
                cancellationToken);
            source.LastScanCompletedAtUtc = DateTimeOffset.UtcNow;
            source.LastSuccessfulScanAtUtc = source.LastScanCompletedAtUtc;
            source.LastHealthCheckedAtUtc = source.LastScanCompletedAtUtc;
            source.ScanStatus = "Healthy";
            source.HealthStatus = "Reachable";
            source.HealthMessage = $"{source.IndexedAssetCount:N0} media item(s) indexed.";
            source.LastError = null;
            source.ScanRequestedAtUtc = null;
            source.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = Trim(ex.GetBaseException().Message, 2048);
            if (source is not null)
            {
                source.LastScanCompletedAtUtc = DateTimeOffset.UtcNow;
                source.LastHealthCheckedAtUtc = source.LastScanCompletedAtUtc;
                source.ScanStatus = "Failed";
                source.HealthStatus = "Unavailable";
                source.HealthMessage = message;
                source.LastError = message;
                source.UpdatedAtUtc = DateTimeOffset.UtcNow;
                try
                {
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception saveException)
                {
                    _logger.LogError(saveException,
                        "Unable to persist failed scan state for external media source {SourceId}", sourceId);
                }
            }

            // A failed optional source is contained. Other sources and the core Photos page
            // continue normally.
            _logger.LogError(ex, "External media scan failed for source {SourceId}", sourceId);
        }
        finally
        {
            try
            {
                await ReleaseLeaseAsync(sourceId, workerId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The lease expires automatically. A release failure must not turn an
                // optional source outage into a worker or web-host failure.
                _logger.LogWarning(ex,
                    "Unable to release external media scan lease for source {SourceId}; it will expire automatically",
                    sourceId);
            }
        }
    }

    private async Task ProcessBatchAsync(
        MediaLibrarySource source,
        Guid scanId,
        IReadOnlyCollection<FileCandidate> batch,
        CancellationToken cancellationToken)
    {
        var keys = batch.Select(item => item.SourceEntityId).ToArray();
        var existing = await _db.Assets
            .Where(asset => asset.SourceId == source.Id && keys.Contains(asset.SourceEntityId))
            .ToDictionaryAsync(asset => asset.SourceEntityId, StringComparer.Ordinal, cancellationToken);

        var changedAssets = new List<MediaAsset>();
        var now = DateTimeOffset.UtcNow;

        foreach (var candidate in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (existing.TryGetValue(candidate.SourceEntityId, out var unchanged)
                && string.Equals(unchanged.QuickFingerprint, candidate.QuickFingerprint, StringComparison.Ordinal)
                && unchanged.IsAvailable)
            {
                unchanged.LastSeenAtUtc = now;
                unchanged.LastSeenScanId = scanId;
                continue;
            }

            MediaFileMetadata metadata;
            try
            {
                metadata = await _metadataReader.ReadAsync(candidate.FullPath, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                if (existing.TryGetValue(candidate.SourceEntityId, out var existingAsset))
                {
                    existingAsset.LastSeenAtUtc = now;
                    existingAsset.LastSeenScanId = scanId;
                    existingAsset.IsAvailable = true;
                    existingAsset.ProcessingFailureReason = Trim(ex.GetBaseException().Message, 2048);
                }

                _logger.LogWarning(ex, "Skipping unsupported or unreadable external media file {Path}", candidate.FullPath);
                continue;
            }

            var folder = Path.GetDirectoryName(candidate.RelativePath)?.Replace('\\', '/').Trim('/') ?? string.Empty;
            var collectionTitle = string.IsNullOrWhiteSpace(folder)
                ? source.Name
                : Path.GetFileName(folder.Replace('/', Path.DirectorySeparatorChar));
            var collectionKey = string.IsNullOrWhiteSpace(folder)
                ? $"external:{source.Key}:root"
                : $"external:{source.Key}:{folder.ToUpperInvariant()}";

            var isNew = !existing.TryGetValue(candidate.SourceEntityId, out var asset);
            if (isNew)
            {
                asset = new MediaAsset
                {
                    SourceId = source.Id,
                    SourceEntityId = candidate.SourceEntityId,
                    Origin = MediaAssetOrigin.ExternalFile,
                    IndexedAtUtc = now,
                    CacheVersion = 1
                };
                _db.Assets.Add(asset);
                existing[candidate.SourceEntityId] = asset;
            }
            else
            {
                asset!.CacheVersion++;
            }

            asset!.Kind = metadata.Kind;
            asset.RelativePath = candidate.RelativePath;
            asset.ParentEntityId = folder;
            asset.OriginalFileName = Path.GetFileName(candidate.FullPath);
            asset.ContentType = metadata.ContentType;
            asset.FileSizeBytes = metadata.FileSizeBytes;
            asset.FileModifiedAtUtc = metadata.FileModifiedAtUtc;
            asset.QuickFingerprint = candidate.QuickFingerprint;
            asset.ContextKey = collectionKey;
            asset.CollectionKey = collectionKey;
            asset.ContextTitle = string.IsNullOrWhiteSpace(collectionTitle) ? source.Name : collectionTitle;
            asset.ContextSubtitle = source.Name;
            asset.SourceLabel = source.Name;
            asset.Title = Path.GetFileNameWithoutExtension(candidate.FullPath);
            asset.MediaDateUtc = metadata.MediaDateUtc;
            asset.Width = metadata.Width;
            asset.Height = metadata.Height;
            asset.DurationSeconds = metadata.DurationSeconds;
            asset.IsAvailable = true;
            asset.IsDeleted = false;
            asset.LastSeenAtUtc = now;
            asset.LastSeenScanId = scanId;
            asset.DerivativeStatus = metadata.Kind == MediaAssetKind.Photo
                ? MediaProcessingStatus.Pending
                : MediaProcessingStatus.Unsupported;
            asset.AnalysisStatus = metadata.Kind == MediaAssetKind.Photo && _options.Classification.Enabled
                ? MediaProcessingStatus.Pending
                : MediaProcessingStatus.NotRequested;
            asset.ProcessingFailureReason = null;
            changedAssets.Add(asset);
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (changedAssets.Count > 0 && _options.IsProcessingWorkerEnabled)
        {
            var assetIds = changedAssets.Select(asset => asset.Id).ToArray();
            var jobs = await _db.ProcessingJobs
                .Where(job => assetIds.Contains(job.MediaAssetId)
                              && job.JobType == MediaProcessingJobType.AnalyseAsset)
                .ToDictionaryAsync(job => job.MediaAssetId, cancellationToken);

            foreach (var asset in changedAssets)
            {
                if (!jobs.TryGetValue(asset.Id, out var job))
                {
                    job = new MediaProcessingJob
                    {
                        MediaAssetId = asset.Id,
                        JobType = MediaProcessingJobType.AnalyseAsset,
                        CreatedAtUtc = now
                    };
                    _db.ProcessingJobs.Add(job);
                }

                job.Status = MediaProcessingJobStatus.Pending;
                job.AttemptCount = 0;
                job.MaxAttempts = _options.Processing.MaxAttempts;
                job.AvailableAfterUtc = now;
                job.StartedAtUtc = null;
                job.CompletedAtUtc = null;
                job.LockedBy = null;
                job.LockExpiresAtUtc = null;
                job.FailureCode = null;
                job.FailureMessage = null;
                job.UpdatedAtUtc = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(entry => entry.Entity is MediaAsset or MediaProcessingJob)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<bool> TryAcquireLeaseAsync(
        Guid sourceId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(leaseDuration);
        var affected = await _db.Sources
            .Where(source => source.Id == sourceId
                             && (source.ScanLockedBy == null
                                 || source.ScanLockExpiresAtUtc == null
                                 || source.ScanLockExpiresAtUtc <= now
                                 || source.ScanLockedBy == workerId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(source => source.ScanLockedBy, workerId)
                .SetProperty(source => source.ScanLockExpiresAtUtc, expires), cancellationToken);

        _db.ChangeTracker.Clear();
        return affected == 1;
    }

    private async Task RenewLeaseAsync(
        Guid sourceId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var affected = await _db.Sources
            .Where(source => source.Id == sourceId && source.ScanLockedBy == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(source => source.ScanLockExpiresAtUtc, DateTimeOffset.UtcNow.Add(leaseDuration)),
                cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException("The external media scan lease was lost.");
        }
    }

    private async Task ReleaseLeaseAsync(Guid sourceId, string workerId, CancellationToken cancellationToken)
    {
        await _db.Sources
            .Where(source => source.Id == sourceId && source.ScanLockedBy == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(source => source.ScanLockedBy, (string?)null)
                .SetProperty(source => source.ScanLockExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    private static string[] ParseExtensions(string json)
    {
        try
        {
            return MediaSourceBootstrapper.NormalizeExtensions(
                JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>());
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record FileCandidate(
        string FullPath,
        string RelativePath,
        string SourceEntityId,
        string QuickFingerprint);
}
