using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaClassificationDecisionPolicy : IMediaClassificationDecisionPolicy
{
    private readonly MediaClassificationOptions _options;
    public MediaClassificationDecisionPolicy(IOptions<MediaLibraryOptions> options)
        => _options = options.Value.Classification;

    public MediaClassificationDecision Decide(MediaClassification predictedClassification, double predictedScore,
        IReadOnlyDictionary<MediaClassification, double> categoryScores, IReadOnlyList<string> signals)
    {
        if (predictedClassification == MediaClassification.Unknown)
            return new(MediaClassification.Unknown, MediaClassificationDecisionStatus.NeedsReview, "NO_RELIABLE_PREDICTION");

        var threshold = predictedClassification switch
        {
            MediaClassification.Photograph => _options.PhotographThreshold,
            MediaClassification.Screenshot => _options.ScreenshotThreshold,
            MediaClassification.ScannedDocument => _options.DocumentThreshold,
            MediaClassification.Diagram => _options.DiagramThreshold,
            MediaClassification.PresentationSlide => _options.PresentationThreshold,
            MediaClassification.Graphic => _options.GraphicThreshold,
            _ => 1d
        };

        var conflicting = predictedClassification == MediaClassification.Photograph
            && signals.Any(x => x.Contains("strong non-photo", StringComparison.OrdinalIgnoreCase));
        if (conflicting)
            return new(MediaClassification.Unknown, MediaClassificationDecisionStatus.NeedsReview, "CONFLICTING_CLASSIFICATION_SIGNALS");

        return predictedScore >= threshold
            ? new(predictedClassification, MediaClassificationDecisionStatus.AutomaticallyAccepted, "CATEGORY_SCORE_ACCEPTED")
            : new(MediaClassification.Unknown, MediaClassificationDecisionStatus.NeedsReview, "CATEGORY_SCORE_BELOW_THRESHOLD");
    }
}
