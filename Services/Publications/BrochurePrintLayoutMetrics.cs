namespace ProjectManagement.Services.Publications;

/// <summary>
/// Canonical hard-copy geometry shared by the measurement service, page planner and QuestPDF compositor.
/// Phase 11 locks the approved narrow reference sheet, 16:9 publication imagery, reference-style
/// right-hand float composition and a 9 pt normal project typography floor.
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

    public const float ModuleBorderPoints = 1.05f;
    public const float ModuleHorizontalPaddingPoints = 7f;
    public const float TextImageGapPoints = 6f;
    public const float FloatRemainderGapPoints = 2.5f;
    public const float FloatBoundaryToleranceLines = 1.25f;
    public const float FloatPreferredBoundaryBandLines = 1.65f;
    public const float GalleryImageGapPoints = 4f;

    // Project publication images are normalised to 1920x1080 upstream. Keep the compositor and
    // planner on the same 16:9 geometry so measured height exactly matches the rendered image.
    public const float SingleImageAspectRatio = 16f / 9f;
    public const float GalleryImageAspectRatio = 16f / 9f;

    public const float InterModuleSpacingPoints = 4f;
    public const float ClosingGapPoints = 4f;
    public const float ProjectMeasurementSafetyPoints = 3.5f;

    public const int MaximumProjectsPerSheet = 4;
    public const float TargetMinimumUtilization = .90f;
    public const float PreferredUtilization = .96f;

    // Residual-space optimisation is deliberately bounded. Page membership never changes during
    // this pass; only project imagery is enlarged until the page approaches a professional fill.
    public const float ResidualTargetUtilization = .95f;
    public const float ResidualImageExpansionStepPoints = 4f;
    public const float ResidualMaximumImageExpansionPoints = 24f;
    public const float ResidualMaximumImageWidthPoints = 176f;

    // Print / Compact project typography. Visual and Balanced both retain the 9 pt publication
    // body. Compact is an emergency layout only and may use the 8.5 pt hard floor.
    public const float ProjectTitlePreferredFontSize = 10f;
    public const float ProjectTitleMinimumFontSize = 9.25f;
    public const float ProjectTitleLineHeight = 1.0f;
    public const float ProjectBodyPreferredFontSize = 9f;
    public const float ProjectBodyMinimumFontSize = 8.5f;

    // Closing matter is intentionally more prominent than project body copy, matching the role it
    // plays on the approved reference brochure's final sheet.
    public const float ClosingVisionBodyFontSize = 10.4f;
    public const float ClosingVisionBodyLineHeight = 1.08f;
    public const float ClosingVisionHeadingFontSize = 11.2f;
    public const float ClosingNewSimulatorsFontSize = 8.8f;
    public const float ClosingNewSimulatorsLineHeight = 1.06f;
    public const float ClosingStraplineFontSize = 8.2f;

    public const float FrontCentrePreferredFontSize = 11.2f;
    public const float FrontCentreLineHeight = 1.05f;
    public const float FrontBodyPreferredFontSize = 9.0f;
    public const float FrontBodyMinimumFontSize = 8.4f;
    public const float FrontBodyLineHeight = 1.07f;
    public const float FrontContactPreferredFontSize = 8.5f;
    public const float FrontContactMinimumFontSize = 8.1f;
    public const float FrontContactLineHeight = 1.05f;
    public const float FrontContactHeaderHeightPoints = 18f;
    public const float FrontContactCentreWidthPoints = 84f;
    public const float FrontStraplineHeightPoints = 22f;
    public const float FrontMinimumHeroHeightPoints = 215f;
    public const float FrontMaximumHeroHeightPoints = 355f;

    public static float ProjectContentCapacity(bool hasHandlingMarking)
    {
        var top = hasHandlingMarking
            ? ProjectMarginTopWithHandlingPoints + HandlingHeaderHeightPoints
            : ProjectMarginTopPoints;

        return ReferenceHeightPoints - top - ProjectMarginBottomPoints;
    }

    public static float ModuleWidthPoints
        => ReferenceWidthPoints - (ProjectMarginHorizontalPoints * 2f);

    public static BrochurePrintVariantSpec VariantSpec(
        BrochurePrintLayoutVariant variant,
        int narrativeWordCount)
    {
        // Text wraps under imagery, so long briefs only need a modest width reduction. Short copy
        // can support a slightly stronger visual without compromising the reference body size.
        var imageAdjustment = narrativeWordCount switch
        {
            > 195 => -8f,
            > 160 => -4f,
            < 90 => 8f,
            < 120 => 4f,
            _ => 0f
        };

        return variant switch
        {
            BrochurePrintLayoutVariant.Visual => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: ProjectBodyPreferredFontSize,
                BodyLineHeight: 1.05f,
                TitleFontSize: ProjectTitlePreferredFontSize,
                ImageWidthPoints: 150f + imageAdjustment,
                BodyPaddingPoints: 6.0f,
                QualityRank: 3),

            // Balanced reduces image footprint only. It deliberately preserves 9 pt copy so page
            // count never wins by silently reducing normal publication typography.
            BrochurePrintLayoutVariant.Balanced => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: ProjectBodyPreferredFontSize,
                BodyLineHeight: 1.05f,
                TitleFontSize: ProjectTitlePreferredFontSize,
                ImageWidthPoints: 140f + imageAdjustment,
                BodyPaddingPoints: 5.6f,
                QualityRank: 2),

            _ => new BrochurePrintVariantSpec(
                BrochurePrintLayoutVariant.Compact,
                BodyFontSize: ProjectBodyMinimumFontSize,
                BodyLineHeight: 1.04f,
                TitleFontSize: 9.5f,
                ImageWidthPoints: 130f + imageAdjustment,
                BodyPaddingPoints: 5.2f,
                QualityRank: 1)
        };
    }
}

public sealed record BrochurePrintVariantSpec(
    BrochurePrintLayoutVariant Variant,
    float BodyFontSize,
    float BodyLineHeight,
    float TitleFontSize,
    float ImageWidthPoints,
    float BodyPaddingPoints,
    int QualityRank);
