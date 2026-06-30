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

        foreach (var id in await _applicationDb.ActivityAttachments.AsNoTracking()
                     .Where(attachment => !attachment.Activity.IsDeleted
                                          && attachment.ContentType.ToLower().StartsWith("image/"))
                     .Select(attachment => attachment.Id)
                     .ToListAsync(cancellationToken))
        {
            expected.Add($"activity-photo:{id}");
        }

        var catalogue = await _mediaDb.Assets.AsNoTracking()
            .Where(asset => asset.Origin != MediaAssetOrigin.ExternalFile && !asset.IsDeleted)
            .Select(asset => new
            {
                asset.SourceEntityId,
                asset.IsAvailable,
                asset.AvailabilityStatus
            })
            .ToListAsync(cancellationToken);

        // Catalogue integrity and source availability are deliberately separate concerns.
        // An unavailable asset still exists in the catalogue and must not be reported as
        // "missing from catalogue" merely because its physical source cannot be read.
        var catalogueIds = catalogue
            .Select(asset => asset.SourceEntityId)
            .ToHashSet(StringComparer.Ordinal);

        var missing = expected.Count(id => !catalogueIds.Contains(id));
        var orphaned = catalogueIds.Count(id => !expected.Contains(id));

        var available = catalogue.Count(asset =>
            asset.IsAvailable
            && asset.AvailabilityStatus == MediaAvailabilityStatus.Available);

        var unavailable = catalogue.Count - available;

        return new MediaCatalogueConsistencyReport(
            expected.Count,
            catalogue.Count,
            missing,
            orphaned,
            available,
            unavailable);
    }
}
