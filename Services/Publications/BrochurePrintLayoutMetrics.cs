namespace ProjectManagement.Services.Publications;

/// <summary>
/// Canonical hard-copy geometry shared by the measurement service, page planner and QuestPDF compositor.
/// Keeping these values in one place prevents the planner and renderer from drifting apart.
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
    public const float ModuleHorizontalPaddingPoints = 6f;
    public const float TextImageGapPoints = 6f;
    public const float InterModuleSpacingPoints = 4f;
    public const float ClosingGapPoints = 4f;
    public const float ProjectMeasurementSafetyPoints = 3.5f;

    public const int MaximumProjectsPerSheet = 4;
    public const float TargetMinimumUtilization = .90f;
    public const float PreferredUtilization = .96f;

    public const float ClosingVisionBodyFontSize = 7.6f;
    public const float ClosingVisionBodyLineHeight = 1.08f;
    public const float ClosingVisionHeadingFontSize = 8.8f;
    public const float ClosingNewSimulatorsFontSize = 7.25f;
    public const float ClosingNewSimulatorsLineHeight = 1.05f;
    public const float ClosingStraplineFontSize = 6.7f;

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
        var imageAdjustment = narrativeWordCount switch
        {
            > 190 => -10f,
            > 155 => -5f,
            < 95 => 5f,
            _ => 0f
        };

        return variant switch
        {
            BrochurePrintLayoutVariant.Visual => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: 8.15f,
                BodyLineHeight: 1.05f,
                TitleFontSize: 8.5f,
                ImageWidthPoints: 136f + imageAdjustment,
                BodyPaddingPoints: 5.0f,
                QualityRank: 3),

            BrochurePrintLayoutVariant.Balanced => new BrochurePrintVariantSpec(
                variant,
                BodyFontSize: 7.9f,
                BodyLineHeight: 1.05f,
                TitleFontSize: 8.25f,
                ImageWidthPoints: 124f + imageAdjustment,
                BodyPaddingPoints: 4.65f,
                QualityRank: 2),

            _ => new BrochurePrintVariantSpec(
                BrochurePrintLayoutVariant.Compact,
                BodyFontSize: 7.65f,
                BodyLineHeight: 1.05f,
                TitleFontSize: 8.0f,
                ImageWidthPoints: 112f + imageAdjustment,
                BodyPaddingPoints: 4.35f,
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
