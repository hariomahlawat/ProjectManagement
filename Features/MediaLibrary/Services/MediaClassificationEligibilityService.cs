using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public interface IMediaClassificationEligibilityService
{
    MediaClassificationEligibility Evaluate(MediaAsset asset, MediaFileMetadata metadata);
}

public sealed record MediaClassificationEligibility(bool IsEligible, string Reason);

public sealed class MediaClassificationEligibilityService : IMediaClassificationEligibilityService
{
    private readonly MediaLibraryOptions _options;

    public MediaClassificationEligibilityService(IOptions<MediaLibraryOptions> options)
        => _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public MediaClassificationEligibility Evaluate(MediaAsset asset, MediaFileMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!_options.Classification.Enabled) return new(false, "Classification is disabled.");
        if (!asset.IsAvailable || asset.IsDeleted || asset.IsArchived) return new(false, "Asset is unavailable, deleted or archived.");
        if (asset.Kind != MediaAssetKind.Photo || metadata.Kind != MediaAssetKind.Photo) return new(false, "Only photographs and still images are eligible.");
        if (asset.ClassificationIsManual) return new(false, "Manual classification is authoritative.");
        if (metadata.Width is < 64 || metadata.Height is < 64) return new(false, "Image dimensions are too small for reliable classification.");
        if (metadata.FileSizeBytes > _options.Processing.MaxImageFileSizeBytes) return new(false, "Image exceeds the configured processing limit.");

        return new(true, "Eligible.");
    }
}
