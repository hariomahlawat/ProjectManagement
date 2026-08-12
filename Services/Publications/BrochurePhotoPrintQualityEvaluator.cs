namespace ProjectManagement.Services.Publications;

/// <summary>
/// Physical publication placement used to evaluate whether the pixels that survive the
/// publication crop are sufficient for the size at which the photograph can actually render.
/// </summary>
public enum BrochurePhotoPrintPlacement
{
    ProjectCard = 1,
    ProjectFeature = 2,
    CoverHero = 3,
    ProjectEditorialSplit = 4
}

public sealed record BrochurePhotoPrintAssessment(
    double EffectiveWidthPixels,
    double EffectiveHeightPixels,
    double RenderedWidthInches,
    double RenderedHeightInches,
    double EffectiveDpi,
    double RecommendedDpi,
    string PlacementLabel)
{
    public bool MeetsRecommendation => EffectiveDpi + .01d >= RecommendedDpi;
}

/// <summary>
/// Publication-quality check based on effective cropped pixels divided by the largest physical
/// frame the selected profile can use. This intentionally avoids warning on raw pixel dimensions
/// alone: a 1024 px source can be excellent in a two-inch compact frame, while the same source can
/// be soft in a full-page hero.
/// </summary>
public static class BrochurePhotoPrintQualityEvaluator
{
    private const double PointsPerInch = 72d;
    private const double DigitalCoverHeroWidthPoints = 543.276d; // A4 width minus 26 pt side gutters

    // Print / Compact is intended for hard copy. 240 effective dpi is a conservative warning floor;
    // images above it remain suitable for the small project frames even if they are below a generic
    // 1800 px asset threshold. Digital / Comfortable is screen-first, so 180 dpi is sufficient.
    public const double PrintCompactRecommendedDpi = 240d;
    public const double DigitalComfortableRecommendedDpi = 180d;

    // Largest frames used by the Digital / Comfortable composer. Evaluating against the largest
    // frame is conservative: if the image passes here, it also passes in smaller card layouts.
    private const double DigitalCardMaximumWidthPoints = 164d;
    private const double DigitalEditorialSplitMaximumWidthPoints = 225d;
    private const double DigitalFeatureMaximumWidthPoints = 382d;

    public static BrochurePhotoPrintAssessment Assess(
        int sourceWidth,
        int sourceHeight,
        BrochurePublicationProfile publicationProfile,
        BrochurePhotoPrintPlacement placement)
    {
        var targetAspect = TargetAspect(publicationProfile, placement);
        var (effectiveWidth, effectiveHeight) = BrochurePhotoService.EffectiveCropDimensions(
            sourceWidth,
            sourceHeight,
            targetAspect);

        var renderedWidthPoints = RenderedWidthPoints(publicationProfile, placement);
        var renderedWidthInches = renderedWidthPoints / PointsPerInch;
        var renderedHeightInches = renderedWidthInches / targetAspect;
        var widthDpi = renderedWidthInches > 0d ? effectiveWidth / renderedWidthInches : 0d;
        var heightDpi = renderedHeightInches > 0d ? effectiveHeight / renderedHeightInches : 0d;
        var effectiveDpi = Math.Min(widthDpi, heightDpi);
        var recommendedDpi = publicationProfile == BrochurePublicationProfile.PrintCompact
            ? PrintCompactRecommendedDpi
            : DigitalComfortableRecommendedDpi;

        return new BrochurePhotoPrintAssessment(
            effectiveWidth,
            effectiveHeight,
            renderedWidthInches,
            renderedHeightInches,
            effectiveDpi,
            recommendedDpi,
            PlacementLabel(publicationProfile, placement));
    }

    private static double TargetAspect(
        BrochurePublicationProfile publicationProfile,
        BrochurePhotoPrintPlacement placement)
    {
        if (placement != BrochurePhotoPrintPlacement.CoverHero)
        {
            return 16d / 9d;
        }

        var targetHeight = publicationProfile == BrochurePublicationProfile.PrintCompact
            ? 1055d
            : 1360d;
        return 1800d / targetHeight;
    }

    private static double RenderedWidthPoints(
        BrochurePublicationProfile publicationProfile,
        BrochurePhotoPrintPlacement placement)
    {
        if (publicationProfile == BrochurePublicationProfile.PrintCompact)
        {
            return placement == BrochurePhotoPrintPlacement.CoverHero
                ? BrochurePrintLayoutMetrics.ReferenceWidthPoints
                : BrochurePrintLayoutMetrics.AdaptiveImageMaximumPoints;
        }

        return placement switch
        {
            BrochurePhotoPrintPlacement.CoverHero => DigitalCoverHeroWidthPoints,
            BrochurePhotoPrintPlacement.ProjectFeature => DigitalFeatureMaximumWidthPoints,
            BrochurePhotoPrintPlacement.ProjectEditorialSplit => DigitalEditorialSplitMaximumWidthPoints,
            _ => DigitalCardMaximumWidthPoints
        };
    }

    private static string PlacementLabel(
        BrochurePublicationProfile publicationProfile,
        BrochurePhotoPrintPlacement placement)
    {
        if (publicationProfile == BrochurePublicationProfile.PrintCompact)
        {
            return placement == BrochurePhotoPrintPlacement.CoverHero
                ? "Print / Compact Cover B hero"
                : "largest Print / Compact project frame";
        }

        return placement switch
        {
            BrochurePhotoPrintPlacement.CoverHero => "Digital / Comfortable Cover B hero",
            BrochurePhotoPrintPlacement.ProjectFeature => "largest Digital / Comfortable feature frame",
            BrochurePhotoPrintPlacement.ProjectEditorialSplit => "Digital / Comfortable editorial split frame",
            _ => "largest Digital / Comfortable project card"
        };
    }
}
