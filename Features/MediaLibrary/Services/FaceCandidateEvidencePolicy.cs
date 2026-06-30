using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Conservative open-set evidence policy. Similarity remains a ranking signal, never an
/// identity probability. A strong label requires multiple trusted references and an actual
/// second-best person against which separation can be measured.
/// </summary>
public static class FaceCandidateEvidencePolicy
{
    public static FaceCandidateEvidence Evaluate(
        double similarity,
        double bestReferenceSimilarity,
        double meanTopSimilarity,
        int referenceCount,
        double? nextPersonSimilarity,
        MediaPeopleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var marginAvailable = nextPersonSimilarity.HasValue;
        var margin = marginAvailable
            ? Math.Max(0d, similarity - nextPersonSimilarity!.Value)
            : (double?)null;
        var reviewThreshold = referenceCount <= 1
            ? options.CandidateSingleReferenceSimilarityThreshold
            : options.CandidateSimilarityThreshold;

        if (!double.IsFinite(similarity) || similarity < reviewThreshold)
        {
            return new FaceCandidateEvidence(
                FaceCandidateConfidenceLevel.None,
                margin,
                marginAvailable,
                reviewThreshold,
                "Below the conservative open-set review threshold.");
        }

        var strong = referenceCount >= options.CandidateMinimumTrustedReferencesForStrong
                     && similarity >= options.CandidateStrongSimilarityThreshold
                     && bestReferenceSimilarity >= options.CandidateStrongSimilarityThreshold
                     && meanTopSimilarity >= options.CandidateStrongMeanSimilarityThreshold
                     && marginAvailable
                     && margin >= options.CandidateMinimumMargin;

        if (strong)
        {
            return new FaceCandidateEvidence(
                FaceCandidateConfidenceLevel.Strong,
                margin,
                true,
                reviewThreshold,
                "Multiple trusted references agree and the best person is separated from the runner-up.");
        }

        var explanation = referenceCount <= 1
            ? "Possible match based on one trusted reference; separation evidence is insufficient for a strong label."
            : !marginAvailable
                ? "Possible match; there is no second known person against which separation can be measured."
                : "Possible match requiring human review; strong-evidence gates were not all satisfied.";

        return new FaceCandidateEvidence(
            FaceCandidateConfidenceLevel.Possible,
            margin,
            marginAvailable,
            reviewThreshold,
            explanation);
    }
}

public sealed record FaceCandidateEvidence(
    FaceCandidateConfidenceLevel ConfidenceLevel,
    double? MarginToNext,
    bool MarginAvailable,
    double ReviewThreshold,
    string Explanation)
{
    public bool ShouldSuggest => ConfidenceLevel is FaceCandidateConfidenceLevel.Possible
        or FaceCandidateConfidenceLevel.Strong;
}
