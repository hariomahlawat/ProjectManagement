using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaLibraryHealthCheckItem(
    MediaLibraryQueryOperation Operation,
    bool Healthy,
    string Reference,
    string Message,
    long DurationMilliseconds);

public sealed record MediaLibraryHealthReport(
    DateTimeOffset CheckedAtUtc,
    bool DatabaseReachable,
    bool SchemaCurrent,
    bool InternalSourcePresent,
    bool HasIndexedAssets,
    bool TimelineQueryHealthy,
    bool FacetsHealthy,
    int IndexedAssets,
    IReadOnlyList<MediaLibraryHealthCheckItem> Checks)
{
    public bool IsOperational => DatabaseReachable && SchemaCurrent && InternalSourcePresent && TimelineQueryHealthy;
}

public interface IMediaLibraryHealthService
{
    Task<MediaLibraryHealthReport> CheckAsync(CancellationToken cancellationToken);
}

public sealed class MediaLibraryHealthService : IMediaLibraryHealthService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaLibrarySchemaService _schema;
    private readonly IMediaLibraryDiagnostics _diagnostics;
    private readonly ILogger<MediaLibraryHealthService> _logger;

    public MediaLibraryHealthService(
        MediaLibraryDbContext db,
        IMediaLibrarySchemaService schema,
        IMediaLibraryDiagnostics diagnostics,
        ILogger<MediaLibraryHealthService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaLibraryHealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var checks = new List<MediaLibraryHealthCheckItem>();
        var schema = await _schema.GetStatusAsync(cancellationToken);
        if (!schema.IsAvailable || !schema.IsCurrent)
        {
            return new MediaLibraryHealthReport(
                checkedAt,
                schema.IsAvailable,
                schema.IsCurrent,
                false,
                false,
                false,
                false,
                0,
                checks);
        }

        var databaseReachable = await ExecuteAsync(
            MediaLibraryQueryOperation.PrismSourceStatus,
            async () => await _db.Database.CanConnectAsync(cancellationToken),
            checks,
            cancellationToken);

        if (!databaseReachable)
        {
            return new MediaLibraryHealthReport(checkedAt, false, true, false, false, false, false, 0, checks);
        }

        var sourcePresent = await ExecuteAsync(
            MediaLibraryQueryOperation.PrismSourceStatus,
            async () => await _db.Sources.AsNoTracking().AnyAsync(
                source => source.Key == MediaSourceBootstrapper.PrismSourceKey && !source.IsDeleted,
                cancellationToken),
            checks,
            cancellationToken);

        var indexedAssets = await ExecuteValueAsync(
            MediaLibraryQueryOperation.Statistics,
            async () => await _db.Assets.AsNoTracking().CountAsync(
                asset => asset.IsAvailable && asset.AvailabilityStatus == MediaAvailabilityStatus.Available && !asset.IsDeleted,
                cancellationToken),
            checks,
            cancellationToken);

        var timelineHealthy = await ExecuteAsync(
            MediaLibraryQueryOperation.PrimaryTimeline,
            async () =>
            {
                _ = await _db.Assets.AsNoTracking()
                    .Where(asset => asset.IsAvailable && asset.AvailabilityStatus == MediaAvailabilityStatus.Available && !asset.IsDeleted && !asset.IsArchived)
                    .OrderByDescending(asset => asset.MediaDateUtc)
                    .ThenBy(asset => asset.Id)
                    .Select(asset => new { asset.Id, asset.MediaDateUtc, asset.Title })
                    .Take(1)
                    .ToListAsync(cancellationToken);
                return true;
            },
            checks,
            cancellationToken);

        var statisticsHealthy = await ExecuteAsync(
            MediaLibraryQueryOperation.Statistics,
            async () =>
            {
                _ = await _db.Assets.AsNoTracking()
                    .Where(asset => asset.IsAvailable && asset.AvailabilityStatus == MediaAvailabilityStatus.Available && !asset.IsDeleted && !asset.IsArchived)
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Total = group.Count(),
                        Photos = group.Count(asset => asset.Kind == MediaAssetKind.Photo),
                        Videos = group.Count(asset => asset.Kind == MediaAssetKind.Video)
                    })
                    .FirstOrDefaultAsync(cancellationToken);
                return true;
            },
            checks,
            cancellationToken);

        var yearsHealthy = await ExecuteAsync(
            MediaLibraryQueryOperation.Years,
            async () =>
            {
                _ = await _db.Assets.AsNoTracking()
                    .Where(asset => asset.IsAvailable && asset.AvailabilityStatus == MediaAvailabilityStatus.Available && !asset.IsDeleted)
                    .GroupBy(asset => asset.MediaDateUtc.Year)
                    .Select(group => group.Key)
                    .Take(2)
                    .ToListAsync(cancellationToken);
                return true;
            },
            checks,
            cancellationToken);

        var projectsHealthy = await ExecuteAsync(
            MediaLibraryQueryOperation.Projects,
            async () =>
            {
                _ = await _db.Assets.AsNoTracking()
                    .Where(asset => asset.IsAvailable && asset.AvailabilityStatus == MediaAvailabilityStatus.Available && !asset.IsDeleted && asset.ProjectId.HasValue)
                    .Select(asset => new { Id = asset.ProjectId!.Value, Name = asset.ContextTitle })
                    .Distinct()
                    .Take(2)
                    .ToListAsync(cancellationToken);
                return true;
            },
            checks,
            cancellationToken);

        return new MediaLibraryHealthReport(
            checkedAt,
            true,
            schema.IsCurrent,
            sourcePresent,
            indexedAssets > 0,
            timelineHealthy,
            statisticsHealthy && yearsHealthy && projectsHealthy,
            indexedAssets,
            checks);
    }

    private async Task<bool> ExecuteAsync(
        MediaLibraryQueryOperation operation,
        Func<Task<bool>> action,
        ICollection<MediaLibraryHealthCheckItem> checks,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await action();
            stopwatch.Stop();
            var diagnostic = _diagnostics.RecordSuccess(operation, stopwatch.ElapsedMilliseconds);
            checks.Add(new MediaLibraryHealthCheckItem(operation, result, diagnostic.Reference,
                result ? "Healthy" : "Required catalogue data was not found.", stopwatch.ElapsedMilliseconds));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var diagnostic = _diagnostics.RecordFailure(operation, ex, stopwatch.ElapsedMilliseconds);
            _logger.LogWarning(ex, "Media catalogue health check {Operation} failed. Reference {Reference}.", operation, diagnostic.Reference);
            checks.Add(new MediaLibraryHealthCheckItem(operation, false, diagnostic.Reference, diagnostic.Message, stopwatch.ElapsedMilliseconds));
            return false;
        }
    }

    private async Task<int> ExecuteValueAsync(
        MediaLibraryQueryOperation operation,
        Func<Task<int>> action,
        ICollection<MediaLibraryHealthCheckItem> checks,
        CancellationToken cancellationToken)
    {
        var value = 0;
        var healthy = await ExecuteAsync(operation, async () =>
        {
            value = await action();
            return true;
        }, checks, cancellationToken);
        return healthy ? value : 0;
    }
}
