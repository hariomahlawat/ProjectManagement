using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaCatalogueConsistencyReport(
    int PrismSourceRecords,
    int CatalogueRecords,
    int MissingFromCatalogue,
    int OrphanedCatalogueRecords,
    int AvailableCatalogueRecords,
    int UnavailableCatalogueRecords)
{
    public bool IsConsistent => MissingFromCatalogue == 0 && OrphanedCatalogueRecords == 0;
}

public interface IMediaCatalogueConsistencyService
{
    Task<MediaCatalogueConsistencyReport> CheckAsync(CancellationToken cancellationToken);
}

public sealed class MediaCatalogueConsistencyService : IMediaCatalogueConsistencyService
{
    private readonly ApplicationDbContext _applicationDb;
    private readonly MediaLibraryDbContext _mediaDb;

    public MediaCatalogueConsistencyService(
        ApplicationDbContext applicationDb,
        MediaLibraryDbContext mediaDb)
    {
        _applicationDb = applicationDb ?? throw new ArgumentNullException(nameof(applicationDb));
        _mediaDb = mediaDb ?? throw new ArgumentNullException(nameof(mediaDb));
    }

    public async Task<MediaCatalogueConsistencyReport> CheckAsync(CancellationToken cancellationToken)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in await _applicationDb.ProjectPhotos.AsNoTracking()
                     .Where(photo => !photo.Project.IsDeleted)
                     .Select(photo => photo.Id)
                     .ToListAsync(cancellationToken))
        {
            expected.Add($"project-photo:{id}");
        }

        foreach (var id in await _applicationDb.ProjectVideos.AsNoTracking()
                     .Where(video => !video.Project.IsDeleted)
                     .Select(video => video.Id)
                     .ToListAsync(cancellationToken))
        {
            expected.Add($"project-video:{id}");
        }

        foreach (var id in await _applicationDb.VisitPhotos.AsNoTracking()
                     .Select(photo => photo.Id)
                     .ToListAsync(cancellationToken))
        {
            expected.Add($"visit-photo:{id}");
        }

        foreach (var id in await _applicationDb.SocialMediaEventPhotos.AsNoTracking()
                     .Select(photo => photo.Id)
                     .ToListAsync(cancellationToken))
        {
            expected.Add($"event-photo:{id}");
        }

        var catalogue = await _mediaDb.Assets.AsNoTracking()
            .Where(asset => asset.Origin != MediaAssetOrigin.ExternalFile && !asset.IsDeleted)
            .Select(asset => new { asset.SourceEntityId, asset.IsAvailable })
            .ToListAsync(cancellationToken);

        var availableIds = catalogue
            .Where(asset => asset.IsAvailable && asset.AvailabilityStatus == MediaAvailabilityStatus.Available)
            .Select(asset => asset.SourceEntityId)
            .ToHashSet(StringComparer.Ordinal);

        var missing = expected.Count(id => !availableIds.Contains(id));
        var orphaned = availableIds.Count(id => !expected.Contains(id));

        return new MediaCatalogueConsistencyReport(
            expected.Count,
            catalogue.Count,
            missing,
            orphaned,
            catalogue.Count(asset => asset.IsAvailable),
            catalogue.Count(asset => !asset.IsAvailable));
    }
}
