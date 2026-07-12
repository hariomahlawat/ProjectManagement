using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Converts normalized classifier scores into a deliberately conservative automatic decision.
/// Photograph acceptance additionally requires a clean pre-face safety assessment because it
/// is the admission boundary for biometric processing.
/// </summary>
public sealed class MediaClassificationDecisionPolicy : IMediaClassificationDecisionPolicy
{
    private readonly MediaClassificationOptions _options;

    public MediaClassificationDecisionPolicy(IOptions<MediaLibraryOptions> options)
        => _options = options?.Value.Classification
            ?? throw new ArgumentNullException(nameof(options));

    public MediaClassificationDecision Decide(
        MediaClassification predictedClassification,
        double predictedScore,
        MediaClassificationDecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.FinalCategoryScores);
        ArgumentNullException.ThrowIfNull(context.BaseCategoryScores);
        ArgumentNullException.ThrowIfNull(context.RawEvidence);
        ArgumentNullException.ThrowIfNull(context.Safety);
        ArgumentNullException.ThrowIfNull(context.Signals);

        if (predictedClassification == MediaClassification.Unknown)
        {
            return NeedsReview("NO_RELIABLE_PREDICTION");
        }

        if (!IsCategoryEnabled(predictedClassification))
        {
            return NeedsReview("CATEGORY_DISABLED");
        }

        if (predictedClassification == MediaClassification.Photograph)
        {
            var safetyDecision = EvaluatePhotographSafety(context.Safety);
            if (safetyDecision is not null)
            {
                return safetyDecision;
            }
        }

        var ordered = context.FinalCategoryScores
            .Where(pair => pair.Key != MediaClassification.Unknown && IsCategoryEnabled(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ToArray();
        var runnerUpScore = ordered
            .Where(pair => pair.Key != predictedClassification)
            .Select(pair => pair.Value)
            .DefaultIfEmpty(0d)
            .Max();
        var margin = Math.Max(0d, predictedScore - runnerUpScore);
        var threshold = Math.Max(_options.MinimumConfidence, ThresholdFor(predictedClassification));
        var requiredMargin = predictedClassification == MediaClassification.Photograph
            ? _options.PhotographMinimumScoreMargin
            : _options.MinimumScoreMargin;

        if (predictedScore < threshold)
        {
            return NeedsReview("BELOW_CATEGORY_THRESHOLD");
        }

        if (margin < requiredMargin)
        {
            return NeedsReview("AMBIGUOUS_SCORE_MARGIN");
        }

        if (predictedClassification == MediaClassification.Photograph)
        {
            var strongestFinalNonPhoto = context.FinalCategoryScores
                .Where(pair => pair.Key is not MediaClassification.Unknown and not MediaClassification.Photograph)
                .Select(pair => pair.Value)
                .DefaultIfEmpty(0d)
                .Max();
            if (strongestFinalNonPhoto >= _options.StrongConflictScore)
            {
                return NeedsReview("CONFLICTING_NON_PHOTO_EVIDENCE");
            }
        }

        return new MediaClassificationDecision(
            predictedClassification,
            MediaClassificationDecisionStatus.AutomaticallyAccepted,
            "AUTO_ACCEPTED_HIGH_CONFIDENCE");
    }

    private MediaClassificationDecision? EvaluatePhotographSafety(
        ClassificationSafetyAssessment safety)
    {
        if (safety.ExplicitNonPhotoFilenameVeto)
        {
            return NeedsReview("EXPLICIT_NON_PHOTO_FILENAME_VETO");
        }

        if (safety.DocumentStructureVeto)
        {
            return NeedsReview("DOCUMENT_STRUCTURE_VETO");
        }

        if (safety.DiagramStructureVeto)
        {
            return NeedsReview("DIAGRAM_STRUCTURE_VETO");
        }

        if (safety.GraphicStructureVeto)
        {
            return NeedsReview("GRAPHIC_STRUCTURE_VETO");
        }

        if (!safety.NaturalPhotoBaselineSatisfied)
        {
            return NeedsReview("PHOTO_BASELINE_INSUFFICIENT");
        }

        if (safety.StrongestBaseNonPhotoScore >= _options.BaseNonPhotoConflictScore)
        {
            return NeedsReview("CONFLICTING_BASE_NON_PHOTO_EVIDENCE");
        }

        if (safety.FaceEvidenceDetected && !safety.FaceEvidenceUsed)
        {
            return NeedsReview("FACE_EVIDENCE_NOT_NATURAL_PHOTO");
        }

        return null;
    }

    private static MediaClassificationDecision NeedsReview(string reasonCode)
        => new(MediaClassification.Unknown, MediaClassificationDecisionStatus.NeedsReview, reasonCode);

    private bool IsCategoryEnabled(MediaClassification category)
        => category switch
        {
            MediaClassification.Screenshot => _options.ScreenshotDetectionEnabled,
            MediaClassification.ScannedDocument or MediaClassification.PresentationSlide => _options.DocumentDetectionEnabled,
            MediaClassification.Diagram => _options.DiagramDetectionEnabled,
            _ => true
        };

    private double ThresholdFor(MediaClassification category)
        => category switch
        {
            MediaClassification.Photograph => Math.Max(
                _options.PhotographThreshold,
                _options.NaturalPhotoAutoAcceptThreshold),
            MediaClassification.Screenshot => _options.ScreenshotThreshold,
            MediaClassification.ScannedDocument => _options.DocumentThreshold,
            MediaClassification.Diagram => _options.DiagramThreshold,
            MediaClassification.PresentationSlide => _options.PresentationThreshold,
            MediaClassification.Graphic => _options.GraphicThreshold,
            _ => 1d
        };
}
