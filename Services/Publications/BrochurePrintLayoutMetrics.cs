namespace ProjectManagement.Services.Publications;

/// <summary>
/// Canonical hard-copy geometry shared by the measurement service, page planner and QuestPDF compositor.
/// Phase 10 keeps the approved narrow reference sheet while restoring the reference brochure's
/// right-hand image / wrap-under text grammar and print-readable typography.
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
    public const float GalleryImageGapPoints = 4f;
    public const float SingleImageAspectRatio = 1.45f;
    public const float GalleryImageAspectRatio = 1.65f;
    public const float InterModuleSpacingPoints = 4f;
    public const float ClosingGapPoints = 4f;
    public const float ProjectMeasurementSafetyPoints = 3.5f;

    public const int MaximumProjectsPerSheet = 4;
    public const float TargetMinimumUtilization = .90f;
    public const float PreferredUtilization = .96f;

    // Print / Compact project typography. Phase 10 deliberately keeps the normal body at 9 pt
    // and never drops below 8.5 pt simply to protect an existing page break.
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
        // Images remain visually useful even for long briefs because text is allowed to wrap under
        // them. Width adjustment is therefore deliberately modest rather than collapsing imagery.
        var imageAdjustment = narrativeWordCount switch
        {
            > 195 => -7f,
            > 160 => -3f,
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
                ImageWidthPoints: 136f + imageAdjustment,
                BodyPaddingPoints: 6.0f,
                QualityRank: 3),

            BrochurePrintLayoutVariant.Balanced => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: 8.75f,
                BodyLineHeight: 1.05f,
                TitleFontSize: 9.75f,
                ImageWidthPoints: 124f + imageAdjustment,
                BodyPaddingPoints: 5.6f,
                QualityRank: 2),

            _ => new BrochurePrintVariantSpec(
                BrochurePrintLayoutVariant.Compact,
                BodyFontSize: ProjectBodyMinimumFontSize,
                BodyLineHeight: 1.04f,
                TitleFontSize: 9.5f,
                ImageWidthPoints: 112f + imageAdjustment,
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
