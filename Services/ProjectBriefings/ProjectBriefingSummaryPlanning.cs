namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Shared slide-planning rules for portfolio category summaries. The same rules are
/// used by the workspace estimate and the PowerPoint composer so that automatic
/// suppression and continuation counts cannot drift apart.
/// </summary>
public static class ProjectBriefingSummaryPlanning
{
    public const int MaximumCategoriesPerSlide = 12;

    public static bool ShouldRenderCategorySummary(int distinctCategoryCount)
        => distinctCategoryCount > 1;

    public static int EstimateCategorySlideCount(int distinctCategoryCount)
    {
        if (!ShouldRenderCategorySummary(distinctCategoryCount))
        {
            return 0;
        }

        return (int)Math.Ceiling(distinctCategoryCount / (double)MaximumCategoriesPerSlide);
    }

    public static IReadOnlyList<IReadOnlyList<ProjectBriefingSummaryPoint>> PaginateCategories(
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var populated = points
            .Where(point => point.Count > 0)
            .OrderByDescending(point => point.Count)
            .ThenBy(point => point.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!ShouldRenderCategorySummary(populated.Length))
        {
            return Array.Empty<IReadOnlyList<ProjectBriefingSummaryPoint>>();
        }

        var pageCount = EstimateCategorySlideCount(populated.Length);
        var basePageSize = populated.Length / pageCount;
        var remainder = populated.Length % pageCount;
        var pages = new List<IReadOnlyList<ProjectBriefingSummaryPoint>>(pageCount);
        var offset = 0;

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageSize = basePageSize + (pageIndex < remainder ? 1 : 0);
            pages.Add(populated.Skip(offset).Take(pageSize).ToArray());
            offset += pageSize;
        }

        return pages;
    }
}
