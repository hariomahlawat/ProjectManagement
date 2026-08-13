namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Single physical-geometry contract for the A4 portrait Compendium. Phase 24.1 adds
/// content-aware project-image geometry while retaining deterministic planning.
/// </summary>
public static class CompendiumLayoutMetrics
{
    public const float PageWidthPoints = 595.28f;
    public const float PageHeightPoints = 841.89f;

    public const float HorizontalMarginPoints = 38f;
    public const float TopMarginPoints = 28f;
    public const float FooterHeightPoints = 35f;
    public const float ContentWidthPoints = PageWidthPoints - (2f * HorizontalMarginPoints);

    public const float ProjectImageWidthPoints = ContentWidthPoints;
    public const float ProjectImageLongHeightPoints = 190f;
    public const float ProjectImageMediumHeightPoints = 240f;
    public const float ProjectImageShortHeightPoints = 300f;

    public const float ProjectTitleFontSize = 20f;
    public const float ProjectBodyFontSize = 10f;
    public const float ProjectBodyMinimumFontSize = 9.5f;
    public const float ContinuationBodyFontSize = 10f;

    public const int FirstPageDescriptionBudgetPhotoLong = 2250;
    public const int FirstPageDescriptionBudgetPhotoMedium = 1500;
    public const int FirstPageDescriptionBudgetPhotoShort = 650;
    public const int FirstPageDescriptionBudgetWithoutPhoto = 2850;
    public const int ContinuationDescriptionBudget = 3300;

    public const int IndexPageRowUnits = 22;
    public const int IndexCategoryHeaderUnits = 2;
    public const int IndexProjectRowUnits = 1;

    public static float ProjectImageHeightPoints(CompendiumProjectLayoutVariant variant)
        => variant switch
        {
            CompendiumProjectLayoutVariant.PhotoShort => ProjectImageShortHeightPoints,
            CompendiumProjectLayoutVariant.PhotoMedium => ProjectImageMediumHeightPoints,
            CompendiumProjectLayoutVariant.PhotoLong => ProjectImageLongHeightPoints,
            _ => 0f
        };

    public static int FirstPageDescriptionBudget(CompendiumProjectLayoutVariant variant)
        => variant switch
        {
            CompendiumProjectLayoutVariant.PhotoShort => FirstPageDescriptionBudgetPhotoShort,
            CompendiumProjectLayoutVariant.PhotoMedium => FirstPageDescriptionBudgetPhotoMedium,
            CompendiumProjectLayoutVariant.PhotoLong => FirstPageDescriptionBudgetPhotoLong,
            _ => FirstPageDescriptionBudgetWithoutPhoto
        };
}
