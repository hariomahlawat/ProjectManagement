namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Single physical-geometry contract for the Phase 24 A4 portrait Compendium.
/// Browser crop, image-quality policy, page planning and QuestPDF composition must stay aligned to
/// these values; changing geometry is therefore a publication contract change rather than a local
/// renderer tweak.
/// </summary>
public static class CompendiumLayoutMetrics
{
    public const float PageWidthPoints = 595.28f;
    public const float PageHeightPoints = 841.89f;

    public const float HorizontalMarginPoints = 38f;
    public const float TopMarginPoints = 28f;
    public const float FooterHeightPoints = 35f;

    public const float ContentWidthPoints = PageWidthPoints - (2f * HorizontalMarginPoints);

    // Reviewed publication photograph viewport. This is deliberately one geometry for every normal
    // project page so focal crop and effective-DPI calculations remain deterministic.
    public const float ProjectImageWidthPoints = ContentWidthPoints;
    public const float ProjectImageHeightPoints = 214f;

    public const float ProjectTitleFontSize = 20f;
    public const float ProjectBodyFontSize = 10f;
    public const float ProjectBodyMinimumFontSize = 9.5f;
    public const float ContinuationBodyFontSize = 10f;

    // Conservative text budgets. QuestPDF remains the final shaping engine; these budgets reserve
    // enough physical space that planned pages do not depend on emergency font shrinking.
    public const int FirstPageDescriptionBudgetWithPhoto = 1900;
    public const int FirstPageDescriptionBudgetWithoutPhoto = 2850;
    public const int ContinuationDescriptionBudget = 3300;

    // Index planning uses simple row units: a category band costs two units and a project row one.
    // The limit intentionally leaves reserve for the index heading and page footer.
    public const int IndexPageRowUnits = 22;
    public const int IndexCategoryHeaderUnits = 2;
    public const int IndexProjectRowUnits = 1;
}
