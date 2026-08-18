using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Canonical read-visibility policy for the unified Photos experience.
/// Every catalogue query, direct media request and bulk export must pass through
/// this policy so an asset cannot be hidden in one path but retrievable in another.
/// </summary>
public interface IMediaAssetVisibilityPolicy
{
    IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> query);
    bool IsVisible(MediaAsset asset);
}

public sealed class MediaAssetVisibilityPolicy : IMediaAssetVisibilityPolicy
{
    private readonly MediaLibraryOptions _options;

    public MediaAssetVisibilityPolicy(IOptions<MediaLibraryOptions> options)
        => _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query
            .Where(asset => asset.IsAvailable
                            && asset.AvailabilityStatus == MediaAvailabilityStatus.Available
                            && !asset.IsDeleted
                            && !asset.IsArchived)
            .Where(asset => !asset.Source.IsDeleted)
            .Where(asset => asset.Origin != MediaAssetOrigin.ExternalFile
                            || (_options.IsExternalSourceFeatureEnabled
                                && asset.Source.IsEnabled
                                && asset.Source.IsVisibleInLibrary
                                && asset.Source.SourceType == MediaLibrarySourceType.FileSystem));
    }

    public bool IsVisible(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!asset.IsAvailable
            || asset.AvailabilityStatus != MediaAvailabilityStatus.Available
            || asset.IsDeleted
            || asset.IsArchived
            || asset.Source is null
            || asset.Source.IsDeleted)
        {
            return false;
        }

        if (asset.Origin != MediaAssetOrigin.ExternalFile)
        {
            return true;
        }

        return _options.IsExternalSourceFeatureEnabled
               && asset.Source.IsEnabled
               && asset.Source.IsVisibleInLibrary
               && asset.Source.SourceType == MediaLibrarySourceType.FileSystem;
    }
}
