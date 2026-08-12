namespace ProjectManagement.Services.Publications;

/// <summary>
/// Canonical geometry for the reference-format Print / Compact brochure.
/// Phase 14 keeps publication typography fixed at 9 pt and exposes a bounded set of adaptive
/// geometry candidates (image width, line rhythm, paragraph rhythm and padding). The planner
/// chooses among those candidates; users are never asked to tune physical design controls.
/// </summary>
public static class BrochurePrintLayoutMetrics
{
    public const float ReferenceWidthPoints = 423.23f;
    public const float ReferenceHeightPoints = 846.755f;

    public const float ProjectMarginHorizontalPoints = 5f;
    public const float ProjectMarginTopPoints = 5f;
    public const float ProjectMarginTopWithHandlingPoints = 15f;
    public const float ProjectMarginBottomPoints = 5f;
    public const float HandlingHeaderHeightPoints = 10f;
    // QuestPDF performs final line shaping and page-break decisions after the Skia measurement
    // pass. Keep a small physical reserve at the bottom of every project sheet so the planner
    // never relies on the last few points of the page. This reserve is part of the canonical
    // planning geometry and is therefore shared by preflight and PDF generation.
    public const float ProjectComposerSafetyReservePoints = 12f;

    public const float ModuleBorderPoints = 1.05f;
    public const float ModuleHorizontalPaddingPoints = 7f;
    public const float TextImageGapPoints = 6f;
    public const float FloatRemainderGapPoints = 1.0f;
    public const float FloatWordContinuationGapPoints = .35f;
    public const float FloatSentenceContinuationGapPoints = .75f;
    public const float FloatParagraphContinuationGapPoints = 1.5f;
    public const float FloatBoundaryToleranceLines = 1.35f;
    public const float FloatPreferredBoundaryBandLines = 1.85f;
    public const float GalleryImageGapPoints = 3.5f;

    public const float SingleImageAspectRatio = 16f / 9f;
    public const float GalleryImageAspectRatio = 16f / 9f;

    public const float InterModuleSpacingPoints = 4.25f;
    public const float ClosingGapPoints = 4f;
    // SkiaSharp and QuestPDF use the same bundled face but not the same shaping pipeline. Reserve
    // one normal body line so a rare wrap difference cannot escape an otherwise exact-height card.
    public const float ProjectMeasurementSafetyPoints = 9.5f;

    // Four modules remains the preferred reference composition. A fifth is permitted only when
    // the measured 9 pt candidates genuinely fit; the planner's bounded search prunes impossible
    // combinations before composition. Project body copy is never reduced below 9 pt.
    public const int PreferredMaximumProjectsPerSheet = 4;
    public const int MaximumProjectsPerSheet = 5;
    public const float TargetMinimumUtilization = .90f;
    public const float PreferredUtilization = .96f;

    // Final polish happens only after page membership is fixed. Phase 14 deliberately does not
    // enlarge project imagery after planning: image size is now a first-class planning variable.
    public const float ResidualTargetUtilization = .95f;
    public const float ResidualMaximumExtraModuleVerticalPaddingPoints = 3.5f;
    public const float ResidualMaximumExtraInterModuleSpacingPoints = 2.5f;

    public const float ProjectTitlePreferredFontSize = 10f;
    public const float ProjectTitleMinimumFontSize = 9.25f;
    public const float ProjectTitleLineHeight = 1.0f;
    public const float ProjectBodyPreferredFontSize = 9f;
    public const float ProjectBodyMinimumFontSize = 9f;

    // Approved adaptive image window. This is deliberately narrower than Phase 13's post-plan
    // 160 pt expansion. The optimiser may compact an individual image, but never below 132 pt
    // for normal 9 pt candidates.
    public const float AdaptiveImageMinimumPoints = 132f;
    public const float AdaptiveImageMaximumPoints = 156f;
    public const float EmergencyImageWidthPoints = 128f;

    public static readonly float[] VisualImageWidths = { 156f, 152f };
    public static readonly float[] BalancedImageWidths = { 152f, 148f, 144f };
    public static readonly float[] DenseImageWidths = { 144f, 140f, 136f, 132f };

    public const int MaximumParetoCandidatesPerProject = 6;
    public const float CandidateDominanceHeightTolerancePoints = .35f;
    public const float CandidateDominanceQualityTolerance = .05f;

    // Smart Flow is intentionally local and conservative. It may suggest a different order but
    // never applies one silently. Restricting movement keeps editorial intent recognisable and
    // makes optimisation predictable even for large brochures.
    public const int SmartFlowMaximumMoveDistance = 3;
    public const int SmartFlowMaximumPasses = 2;
    public const int SmartFlowBeamWidth = 3;
    public const int SmartFlowMaximumBoundaryMovesPerState = 8;
    public const int SmartFlowMinimumFillImprovementPercent = 8;
    public const int SmartFlowMinimumAverageImprovementPercent = 3;

    // Closing matter remains deliberately more prominent than normal project copy.
    public const float ClosingVisionBodyFontSize = 9.8f;
    public const float ClosingVisionBodyLineHeight = 1.05f;
    public const float ClosingVisionParagraphSpacingPoints = 2.0f;
    public const float ClosingVisionHeadingFontSize = 10.6f;
    // Phase 19 restores the reference brochure's distinctive closing-frame hierarchy without
    // changing the measured content box: the stronger border is exactly offset by lower padding.
    // This preserves line wrapping, closing height and the already verified page plan.
    public const float ClosingVisionBorderPoints = 2f;
    public const float ClosingVisionHorizontalPaddingPoints = 8.1f;
    public const float ClosingVisionVerticalPaddingPoints = 6.1f;
    public const float ClosingVisionHeadingHorizontalPaddingPoints = 8f;
    public const float ClosingVisionHeadingVerticalPaddingPoints = 2f;
    public const float ClosingSectionSpacingPoints = 5f;
    public const float ClosingNewSimulatorsFontSize = 8.8f;
    public const float ClosingNewSimulatorsLineHeight = 1.06f;
    public const float ClosingStraplineFontSize = 8.2f;

    public const float FrontCentrePreferredFontSize = 11.2f;
    public const float FrontCentreLineHeight = 1.05f;
    public const float FrontBodyPreferredFontSize = 9.0f;
    public const float FrontBodyMinimumFontSize = 8.6f;
    public const float FrontBodyLineHeight = 1.07f;
    public const float FrontContactPreferredFontSize = 8.5f;
    public const float FrontContactMinimumFontSize = 8.1f;
    public const float FrontContactLineHeight = 1.05f;
    public const float FrontContactBadgeHeightPoints = 18f;
    public const float FrontContactAgencyHeadingHeightPoints = 17f;
    public const float FrontContactDevelopingFraction = .61f;
    public const float FrontContactManufacturingFraction = .39f;
    public const float FrontStraplineHeightPoints = 22f;
    public const float FrontMinimumHeroHeightPoints = 300f;
    public const float FrontMaximumHeroHeightPoints = 338f;
    public const float FrontInstitutionalCentreOverlayHeightPoints = 44f;

    public static float ProjectContentCapacity(bool hasHandlingMarking)
    {
        var top = hasHandlingMarking
            ? ProjectMarginTopWithHandlingPoints + HandlingHeaderHeightPoints
            : ProjectMarginTopPoints;

        return ReferenceHeightPoints
               - top
               - ProjectMarginBottomPoints
               - ProjectComposerSafetyReservePoints;
    }

    public static float ModuleWidthPoints
        => ReferenceWidthPoints - (ProjectMarginHorizontalPoints * 2f);

    /// <summary>
    /// Returns the base geometry for a density class. Image width is supplied separately because
    /// Phase 14 measures a bounded continuum of approved widths rather than three fixed templates.
    /// </summary>
    public static BrochurePrintVariantSpec VariantSpec(
        BrochurePrintLayoutVariant variant,
        float imageWidthPoints,
        bool useSecondaryImage = false)
        => variant switch
        {
            BrochurePrintLayoutVariant.Visual => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: ProjectBodyPreferredFontSize,
                BodyLineHeight: 1.05f,
                TitleFontSize: ProjectTitlePreferredFontSize,
                TitleMinimumHeightPoints: 18f,
                ImageWidthPoints: Math.Clamp(imageWidthPoints, AdaptiveImageMinimumPoints, AdaptiveImageMaximumPoints),
                BodyPaddingPoints: 5.8f,
                ParagraphSpacingPoints: 2.25f,
                QualityRank: 4,
                VisualQualityScore: QualityScore(variant, imageWidthPoints, useSecondaryImage),
                UseSecondaryImage: useSecondaryImage),

            BrochurePrintLayoutVariant.Balanced => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: ProjectBodyPreferredFontSize,
                BodyLineHeight: 1.03f,
                TitleFontSize: ProjectTitlePreferredFontSize,
                TitleMinimumHeightPoints: 17.5f,
                ImageWidthPoints: Math.Clamp(imageWidthPoints, AdaptiveImageMinimumPoints, AdaptiveImageMaximumPoints),
                BodyPaddingPoints: 5.15f,
                ParagraphSpacingPoints: 1.75f,
                QualityRank: 3,
                VisualQualityScore: QualityScore(variant, imageWidthPoints, useSecondaryImage),
                UseSecondaryImage: useSecondaryImage),

            BrochurePrintLayoutVariant.Dense => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: ProjectBodyPreferredFontSize,
                BodyLineHeight: 1.01f,
                TitleFontSize: ProjectTitlePreferredFontSize,
                TitleMinimumHeightPoints: 16.75f,
                ImageWidthPoints: Math.Clamp(imageWidthPoints, AdaptiveImageMinimumPoints, AdaptiveImageMaximumPoints),
                BodyPaddingPoints: 4.6f,
                ParagraphSpacingPoints: 1.35f,
                QualityRank: 2,
                VisualQualityScore: QualityScore(variant, imageWidthPoints, useSecondaryImage),
                UseSecondaryImage: useSecondaryImage),

            _ => new BrochurePrintVariantSpec(
                BrochurePrintLayoutVariant.Compact,
                BodyFontSize: ProjectBodyMinimumFontSize,
                BodyLineHeight: 1.02f,
                TitleFontSize: 9.5f,
                TitleMinimumHeightPoints: 16.5f,
                ImageWidthPoints: EmergencyImageWidthPoints,
                BodyPaddingPoints: 4.35f,
                ParagraphSpacingPoints: 1.2f,
                QualityRank: 1,
                VisualQualityScore: 48f + (useSecondaryImage ? 2f : 0f),
                UseSecondaryImage: useSecondaryImage)
        };

    private static float QualityScore(
        BrochurePrintLayoutVariant variant,
        float imageWidthPoints,
        bool useSecondaryImage)
    {
        var densityBase = variant switch
        {
            BrochurePrintLayoutVariant.Visual => 100f,
            BrochurePrintLayoutVariant.Balanced => 94f,
            BrochurePrintLayoutVariant.Dense => 86f,
            _ => 48f
        };
        var imageBonus = (Math.Clamp(imageWidthPoints, AdaptiveImageMinimumPoints, AdaptiveImageMaximumPoints)
                          - AdaptiveImageMinimumPoints) * .18f;
        var galleryBonus = useSecondaryImage ? 2.5f : 0f;
        return densityBase + imageBonus + galleryBonus;
    }
}

public sealed record BrochurePrintVariantSpec(
    BrochurePrintLayoutVariant Variant,
    float BodyFontSize,
    float BodyLineHeight,
    float TitleFontSize,
    float TitleMinimumHeightPoints,
    float ImageWidthPoints,
    float BodyPaddingPoints,
    float ParagraphSpacingPoints,
    int QualityRank,
    float VisualQualityScore,
    bool UseSecondaryImage);
