namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Shared slide-planning rules for the category distributions. The same rules are used by the
/// workspace estimate and the PowerPoint composer so that automatic suppression and slide counts
/// cannot drift apart.
/// </summary>
public static class ProjectBriefingSummaryPlanning
{
    public static bool ShouldRenderCategorySummary(int distinctCategoryCount)
        => distinctCategoryCount > 1;

    /// <summary>
    /// The category slides: each distribution is one donut slide, and both chosen share one slide
    /// (docs/ux/briefing-slides-review.md).
    /// </summary>
    public static int EstimateCategorySlideCount(bool projectCategories, bool technicalCategories)
        => projectCategories || technicalCategories ? 1 : 0;
}
