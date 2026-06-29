using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MediaClassificationDecisionPolicyTests
{
    [Fact]
    public void Decide_RequiresClearSeparationFromRunnerUp()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Screenshot, .70),
            (MediaClassification.Diagram, .65));

        var result = policy.Decide(
            MediaClassification.Screenshot,
            .70,
            Context(scores));

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal(MediaClassification.Unknown, result.EffectiveClassification);
        Assert.Equal("AMBIGUOUS_SCORE_MARGIN", result.ReasonCode);
    }

    [Fact]
    public void Decide_UsesStricterPhotographThreshold()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Photograph, .86),
            (MediaClassification.Graphic, .05));

        var result = policy.Decide(
            MediaClassification.Photograph,
            .86,
            Context(scores));

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("BELOW_CATEGORY_THRESHOLD", result.ReasonCode);
    }

    [Fact]
    public void Decide_BlocksPhotographWhenFinalNonPhotoProbabilityRemainsMaterial()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Photograph, .89),
            (MediaClassification.Screenshot, .10),
            (MediaClassification.Unknown, .01));

        var result = policy.Decide(
            MediaClassification.Photograph,
            .89,
            Context(scores));

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("CONFLICTING_NON_PHOTO_EVIDENCE", result.ReasonCode);
    }

    [Fact]
    public void Decide_BlocksPhotographBeforeThresholdWhenDocumentStructureVetoIsActive()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Photograph, .99),
            (MediaClassification.ScannedDocument, .005));
        var safety = SafePhotographSafety() with
        {
            DocumentStructureVeto = true,
            DocumentStructureScore = .74,
            FaceEvidenceDetected = true,
            FaceEvidenceUsed = false
        };

        var result = policy.Decide(
            MediaClassification.Photograph,
            .99,
            Context(scores, safety));

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal(MediaClassification.Unknown, result.EffectiveClassification);
        Assert.Equal("DOCUMENT_STRUCTURE_VETO", result.ReasonCode);
    }

    [Fact]
    public void Decide_BlocksPhotographWhenPreFaceNonPhotoEvidenceConflicts()
    {
        var policy = CreatePolicy();
        var scores = Scores((MediaClassification.Photograph, .94));
        var safety = SafePhotographSafety() with
        {
            StrongestBaseNonPhotoScore = .42
        };

        var result = policy.Decide(
            MediaClassification.Photograph,
            .94,
            Context(scores, safety));

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("CONFLICTING_BASE_NON_PHOTO_EVIDENCE", result.ReasonCode);
    }

    [Fact]
    public void Decide_BlocksFaceLikeStructureThatFailedNaturalPhotoGate()
    {
        var policy = CreatePolicy();
        var scores = Scores((MediaClassification.Photograph, .96));
        var safety = SafePhotographSafety() with
        {
            FaceEvidenceDetected = true,
            FaceEvidenceUsed = false
        };

        var result = policy.Decide(
            MediaClassification.Photograph,
            .96,
            Context(scores, safety));

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("FACE_EVIDENCE_NOT_NATURAL_PHOTO", result.ReasonCode);
    }

    [Fact]
    public void Decide_AcceptsClearHighConfidenceNonPhotoPrediction()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Diagram, .91),
            (MediaClassification.Graphic, .04));

        var result = policy.Decide(
            MediaClassification.Diagram,
            .91,
            Context(scores));

        Assert.Equal(MediaClassificationDecisionStatus.AutomaticallyAccepted, result.Status);
        Assert.Equal(MediaClassification.Diagram, result.EffectiveClassification);
        Assert.Equal("AUTO_ACCEPTED_HIGH_CONFIDENCE", result.ReasonCode);
    }

    [Fact]
    public void Decide_DoesNotAcceptDisabledCategory()
    {
        var options = new MediaLibraryOptions
        {
            Classification = new MediaClassificationOptions
            {
                DiagramDetectionEnabled = false
            }
        };
        var policy = new MediaClassificationDecisionPolicy(Options.Create(options));
        var scores = Scores((MediaClassification.Diagram, .99));

        var result = policy.Decide(
            MediaClassification.Diagram,
            .99,
            Context(scores));

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("CATEGORY_DISABLED", result.ReasonCode);
    }

    private static MediaClassificationDecisionPolicy CreatePolicy()
        => new(Options.Create(new MediaLibraryOptions()));

    private static MediaClassificationDecisionContext Context(
        IReadOnlyDictionary<MediaClassification, double> finalScores,
        ClassificationSafetyAssessment? safety = null)
        => new(
            finalScores,
            finalScores,
            finalScores,
            safety ?? SafePhotographSafety(),
            Array.Empty<string>());

    private static ClassificationSafetyAssessment SafePhotographSafety()
        => new(
            NaturalPhotoBaselineSatisfied: true,
            DocumentStructureVeto: false,
            GraphicStructureVeto: false,
            DiagramStructureVeto: false,
            ExplicitNonPhotoFilenameVeto: false,
            NaturalPhotoScore: .82,
            DocumentStructureScore: .04,
            GraphicStructureScore: .05,
            DiagramStructureScore: .03,
            BasePhotographScore: .92,
            StrongestBaseNonPhotoScore: .05,
            FaceProbeAttempted: false,
            FaceEvidenceDetected: false,
            FaceEvidenceUsed: false,
            FaceEvidenceDecisionCode: null);

    private static IReadOnlyDictionary<MediaClassification, double> Scores(
        params (MediaClassification Classification, double Score)[] values)
    {
        var scores = Enum.GetValues<MediaClassification>()
            .ToDictionary(classification => classification, _ => 0d);
        foreach (var (classification, score) in values)
        {
            scores[classification] = score;
        }

        return scores;
    }
}
