using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record FaceEligibilityDecision(bool IsEligible, string Code, string Reason);

public interface IFaceEligibilityPolicy
{
    FaceEligibilityDecision Evaluate(MediaAsset asset);
    Expression<Func<MediaAsset, bool>> BuildEligiblePredicate();
}

/// <summary>
/// Single source of truth for deciding whether an asset may enter face processing.
/// Automatic classification must be complete, current and sufficiently confident;
/// a human classification remains authoritative.
/// </summary>
public sealed class FaceEligibilityPolicy : IFaceEligibilityPolicy
{
    private readonly MediaLibraryOptions _options;

    public FaceEligibilityPolicy(IOptions<MediaLibraryOptions> options)
        => _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public FaceEligibilityDecision Evaluate(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!asset.IsAvailable || asset.IsDeleted || asset.IsArchived)
            return new(false, "unavailable", "The media asset is unavailable, deleted or archived.");
        if (asset.Kind != MediaAssetKind.Photo)
            return new(false, "not-photo", "Only still images can be processed for faces.");
        if (!_options.People.ProcessPhotographsOnly)
            return new(true, "eligible", "Still-image processing is enabled without photograph-only filtering.");
        if (asset.ClassificationIsManual)
            return asset.Classification == MediaClassification.Photograph
                ? new(true, "manual-photograph", "A reviewer classified this asset as a photograph.")
                : new(false, "manual-exclusion", "A reviewer classified this asset as non-photographic.");
        if (asset.AnalysisStatus != MediaProcessingStatus.Ready)
            return new(false, "classification-pending", "Automatic classification has not completed successfully.");
        if (!string.Equals(asset.ClassifierVersion, MediaClassifier.ClassifierVersion, StringComparison.Ordinal))
            return new(false, "classifier-stale", "The asset was classified by an older classifier version.");
        if (asset.Classification != MediaClassification.Photograph)
            return new(false, "not-photograph", "Automatic classification did not identify a photograph.");
        if (asset.ClassificationDecisionStatus != MediaClassificationDecisionStatus.AutomaticallyAccepted)
            return new(false, "not-auto-accepted", "Automatic classification was not accepted by the decision policy.");
        if (asset.PredictedClassification != MediaClassification.Photograph)
            return new(false, "prediction-mismatch", "The automatic prediction was not a photograph.");
        if (asset.PredictedClassificationScore < Convert.ToDecimal(_options.People.MinimumClassificationConfidence))
            return new(false, "confidence-low", $"Classification confidence is below the configured {_options.People.MinimumClassificationConfidence:P0} threshold.");

        return new(true, "eligible", "Current automatic classification identifies a sufficiently confident photograph.");
    }

    public Expression<Func<MediaAsset, bool>> BuildEligiblePredicate()
    {
        var photographsOnly = _options.People.ProcessPhotographsOnly;
        var threshold = _options.People.MinimumClassificationConfidence;
        var version = MediaClassifier.ClassifierVersion;

        return asset => asset.IsAvailable
                        && !asset.IsDeleted
                        && !asset.IsArchived
                        && asset.Kind == MediaAssetKind.Photo
                        && (!photographsOnly
                            || (asset.ClassificationIsManual
                                ? asset.Classification == MediaClassification.Photograph
                                : asset.AnalysisStatus == MediaProcessingStatus.Ready
                                  && asset.ClassifierVersion == version
                                  && asset.Classification == MediaClassification.Photograph
                                  && asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.AutomaticallyAccepted
                                  && asset.PredictedClassification == MediaClassification.Photograph
                                  && asset.PredictedClassificationScore >= Convert.ToDecimal(threshold))));
    }
}
