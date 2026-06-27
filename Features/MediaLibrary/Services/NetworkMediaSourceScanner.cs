using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SixLabors.ImageSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class NetworkMediaSourceScanner : INetworkMediaSourceScanner
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly INetworkSharePathResolver _pathResolver;
    private readonly SafeFileEnumerator _enumerator;
    private readonly IMediaMetadataReader _metadataReader;
    private readonly ILogger<NetworkMediaSourceScanner> _logger;

    public NetworkMediaSourceScanner(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        INetworkSharePathResolver pathResolver,
        SafeFileEnumerator enumerator,
        IMediaMetadataReader metadataReader,
        ILogger<NetworkMediaSourceScanner> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ScanAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var source = await _db.Sources.SingleAsync(item => item.Id == sourceId, cancellationToken);
        if (!source.IsEnabled || source.SourceType != MediaLibrarySourceType.NetworkShare)
        {
            return;
        }

        var configured = _options.Sources.FirstOrDefault(item =>
            string.Equals(MediaSourceBootstrapper.NormalizeKey(item.Key), source.Key, StringComparison.OrdinalIgnoreCase));

        if (configured is null || !configured.Enabled)
        {
            source.ScanStatus = "Disabled";
            source.LastError = "The source is not enabled in configuration.";
            source.LastScanCompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var scanId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        source.LastScanStartedAtUtc = started;
        source.ScanStatus = "Scanning";
        source.LastError = null;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var root = _pathResolver.ResolveRoot(configured.RootPath);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Media source root '{root}' is not reachable by the worker account.");
            }

            var allowed = MediaSourceBootstrapper.NormalizeExtensions(configured.AllowedExtensions)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var batch = new List<FileCandidate>(_options.ScanBatchSize);

            foreach (var fullPath in _enumerator.EnumerateFiles(root, configured.IncludeSubfolders, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                    _logger.LogError(ex, "Unable to inspect NAS media file {Path}", fullPath);
                    throw;
                }

                var relative = _pathResolver.ToRelativePath(root, fullPath);
                var normalizedKey = relative.ToUpperInvariant();
                var fingerprint = $"{file.Length:X}-{file.LastWriteTimeUtc.Ticks:X}";
                batch.Add(new FileCandidate(fullPath, relative, normalizedKey, fingerprint));

                if (batch.Count >= _options.ScanBatchSize)
                {
                    await ProcessBatchAsync(source, scanId, batch, cancellationToken);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await ProcessBatchAsync(source, scanId, batch, cancellationToken);
            }

            await _db.Assets
                .Where(asset => asset.SourceId == source.Id && asset.LastSeenScanId != scanId && asset.IsAvailable)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(asset => asset.IsAvailable, false)
                    .SetProperty(asset => asset.LastSeenAtUtc, DateTimeOffset.UtcNow), cancellationToken);

            source.IndexedAssetCount = await _db.Assets.CountAsync(
                asset => asset.SourceId == source.Id && asset.IsAvailable && !asset.IsDeleted,
                cancellationToken);
            source.LastScanCompletedAtUtc = DateTimeOffset.UtcNow;
            source.LastSuccessfulScanAtUtc = source.LastScanCompletedAtUtc;
            source.ScanStatus = "Healthy";
            source.LastError = null;
            source.ScanRequestedAtUtc = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            source.LastScanCompletedAtUtc = DateTimeOffset.UtcNow;
            source.ScanStatus = "Failed";
            source.LastError = Trim(ex.GetBaseException().Message, 2048);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "NAS media scan failed for source {SourceKey}", source.Key);
            throw;
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
            catch (Exception ex) when (ex is InvalidDataException or UnknownImageFormatException or IOException or UnauthorizedAccessException)
            {
                if (existing.TryGetValue(candidate.SourceEntityId, out var existingAsset))
                {
                    existingAsset.LastSeenAtUtc = now;
                    existingAsset.LastSeenScanId = scanId;
                    existingAsset.IsAvailable = true;
                    existingAsset.ProcessingFailureReason = Trim(ex.GetBaseException().Message, 2048);
                }

                _logger.LogWarning(ex, "Skipping unsupported or unreadable NAS media file {Path}", candidate.FullPath);
                continue;
            }

            var folder = Path.GetDirectoryName(candidate.RelativePath)?.Replace('\\', '/').Trim('/') ?? string.Empty;
            var collectionTitle = string.IsNullOrWhiteSpace(folder)
                ? source.Name
                : Path.GetFileName(folder.Replace('/', Path.DirectorySeparatorChar));
            var collectionKey = string.IsNullOrWhiteSpace(folder)
                ? $"nas:{source.Key}:root"
                : $"nas:{source.Key}:{folder.ToUpperInvariant()}";

            var isNew = !existing.TryGetValue(candidate.SourceEntityId, out var asset);
            if (isNew)
            {
                asset = new MediaAsset
                {
                    SourceId = source.Id,
                    SourceEntityId = candidate.SourceEntityId,
                    Origin = MediaAssetOrigin.NetworkFile,
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
            asset.AnalysisStatus = metadata.Kind == MediaAssetKind.Photo
                ? MediaProcessingStatus.Pending
                : MediaProcessingStatus.NotRequested;
            asset.ProcessingFailureReason = null;
            changedAssets.Add(asset);
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (changedAssets.Count > 0)
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
                job.MaxAttempts = _options.MaxAttempts;
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

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record FileCandidate(
        string FullPath,
        string RelativePath,
        string SourceEntityId,
        string QuickFingerprint);
}
