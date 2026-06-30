using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Produces a compact, deterministic revision for all PRISM-owned media that can
/// appear in the Photos module. The revision is intentionally independent of the
/// media catalogue so upload visibility can be checked even when catalogue
/// synchronisation is delayed or temporarily unhealthy.
/// </summary>
public interface IPrismMediaSourceSnapshotService
{
    Task<PrismMediaSourceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

public sealed record PrismMediaSourceSnapshot(
    string Fingerprint,
    int ProjectPhotoCount,
    int ProjectVideoCount,
    int VisitPhotoCount,
    int EventPhotoCount,
    int ActivityPhotoCount,
    DateTimeOffset? LatestChangeUtc)
{
    public int TotalCount => ProjectPhotoCount + ProjectVideoCount + VisitPhotoCount + EventPhotoCount + ActivityPhotoCount;
}

public sealed class PrismMediaSourceSnapshotService : IPrismMediaSourceSnapshotService
{
    private readonly ApplicationDbContext _db;

    public PrismMediaSourceSnapshotService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<PrismMediaSourceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var projectPhotos = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => !photo.Project.IsDeleted)
            .GroupBy(_ => 1)
            .Select(group => new SourceAggregate(
                group.Count(),
                group.Max(photo => (DateTime?)photo.UpdatedUtc),
                group.Sum(photo => (long)photo.Version)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? SourceAggregate.Empty;

        var projectVideos = await _db.ProjectVideos
            .AsNoTracking()
            .Where(video => !video.Project.IsDeleted)
            .GroupBy(_ => 1)
            .Select(group => new SourceAggregate(
                group.Count(),
                group.Max(video => (DateTime?)video.UpdatedUtc),
                group.Sum(video => (long)video.Version)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? SourceAggregate.Empty;

        var visitPhotos = await _db.VisitPhotos
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new OffsetSourceAggregate(
                group.Count(),
                group.Max(photo => (DateTimeOffset?)photo.CreatedAtUtc)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? OffsetSourceAggregate.Empty;

        var eventPhotos = await _db.SocialMediaEventPhotos
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new OffsetSourceAggregate(
                group.Count(),
                group.Max(photo => (DateTimeOffset?)(photo.LastModifiedAtUtc ?? photo.CreatedAtUtc))))
            .SingleOrDefaultAsync(cancellationToken)
            ?? OffsetSourceAggregate.Empty;

        var activityPhotos = await _db.ActivityAttachments
            .AsNoTracking()
            .Where(attachment => !attachment.Activity.IsDeleted
                                 && attachment.ContentType.ToLower().StartsWith("image/"))
            .GroupBy(_ => 1)
            .Select(group => new ActivitySourceAggregate(
                group.Count(),
                group.Max(attachment => (DateTimeOffset?)attachment.UploadedAtUtc),
                group.Max(attachment => attachment.Activity.LastModifiedAtUtc)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? ActivitySourceAggregate.Empty;

        var projectPhotoChange = ToUtcOffset(projectPhotos.LatestChangeUtc);
        var projectVideoChange = ToUtcOffset(projectVideos.LatestChangeUtc);
        var latestCandidates = new DateTimeOffset?[]
            {
                projectPhotoChange,
                projectVideoChange,
                visitPhotos.LatestChangeUtc,
                eventPhotos.LatestChangeUtc,
                activityPhotos.LatestUploadUtc,
                activityPhotos.LatestActivityChangeUtc
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        DateTimeOffset? latest = latestCandidates.Length == 0
            ? null
            : latestCandidates.Max();
        var revisionMaterial = string.Join("|",
            projectPhotos.Count,
            projectPhotos.LatestChangeUtc?.Ticks ?? 0,
            projectPhotos.VersionTotal,
            projectVideos.Count,
            projectVideos.LatestChangeUtc?.Ticks ?? 0,
            projectVideos.VersionTotal,
            visitPhotos.Count,
            visitPhotos.LatestChangeUtc?.UtcDateTime.Ticks ?? 0,
            eventPhotos.Count,
            eventPhotos.LatestChangeUtc?.UtcDateTime.Ticks ?? 0,
            activityPhotos.Count,
            activityPhotos.LatestUploadUtc?.UtcDateTime.Ticks ?? 0,
            activityPhotos.LatestActivityChangeUtc?.UtcDateTime.Ticks ?? 0);

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(revisionMaterial)));

        return new PrismMediaSourceSnapshot(
            fingerprint,
            projectPhotos.Count,
            projectVideos.Count,
            visitPhotos.Count,
            eventPhotos.Count,
            activityPhotos.Count,
            latest);
    }

    private static DateTimeOffset? ToUtcOffset(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc);
    }

    private sealed record SourceAggregate(int Count, DateTime? LatestChangeUtc, long VersionTotal)
    {
        public static SourceAggregate Empty { get; } = new(0, null, 0);
    }

    private sealed record OffsetSourceAggregate(int Count, DateTimeOffset? LatestChangeUtc)
    {
        public static OffsetSourceAggregate Empty { get; } = new(0, null);
    }

    private sealed record ActivitySourceAggregate(
        int Count,
        DateTimeOffset? LatestUploadUtc,
        DateTimeOffset? LatestActivityChangeUtc)
    {
        public static ActivitySourceAggregate Empty { get; } = new(0, null, null);
    }
}
