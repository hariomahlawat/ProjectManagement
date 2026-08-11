namespace ProjectManagement.Services.Publications;

/// <summary>
/// Deterministic print-sheet planner for the narrow hard-copy brochure.
/// QuestPDF still performs final typography/layout, but page membership is decided here so the
/// closing institutional matter can share the final sheet and project pages remain deliberately balanced.
/// </summary>
public static class BrochurePrintCompactPlanner
{
    public const float ContentCapacityPoints = 810f;
    public const float InterModuleSpacingPoints = 4f;
    public const float ClosingGapPoints = 4f;
    public const float MaximumModuleExpansionPoints = 14f;

    public static BrochurePrintCompactPlan Plan(
        IReadOnlyList<BrochurePrintPlanningItem> projects,
        string? visionaryText,
        string? newSimulatorsText)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var closingHeight = EstimateClosingHeight(visionaryText, newSimulatorsText);
        if (projects.Count == 0)
        {
            return new BrochurePrintCompactPlan(
                Array.Empty<BrochurePrintCompactPage>(),
                closingHeight,
                EstimatedTotalPageCount: 1,
                AverageContentUtilizationPercent: 0,
                ClosingMatterSharesFinalPage: false,
                ClosingPageProjectCount: 0);
        }

        var heights = projects.Select(EstimateProjectHeight).ToArray();
        var lowerBound = Math.Max(
            1,
            (int)Math.Ceiling((heights.Sum() + closingHeight) / ContentCapacityPoints));

        for (var pageCount = lowerBound; pageCount <= projects.Count; pageCount++)
        {
            if (!TryPlanSharedClosingPage(heights, closingHeight, pageCount, out var ranges))
            {
                continue;
            }

            return BuildPlan(ranges, heights, closingHeight, sharesClosingMatter: true);
        }

        // A very tall final project can make a shared closing page physically impossible while
        // preserving publication order. In that exceptional case, keep the projects balanced and
        // use one dedicated closing sheet rather than risking a QuestPDF layout exception.
        var projectPageCount = Math.Max(1, (int)Math.Ceiling(heights.Sum() / ContentCapacityPoints));
        IReadOnlyList<PageRange>? projectRanges = null;
        for (var pageCount = projectPageCount; pageCount <= projects.Count; pageCount++)
        {
            if (TryPlanProjectOnlyPages(heights, pageCount, out var candidate))
            {
                projectRanges = candidate;
                break;
            }
        }

        projectRanges ??= Enumerable.Range(0, projects.Count)
            .Select(index => new PageRange(index, index + 1))
            .ToArray();

        return BuildPlan(projectRanges, heights, closingHeight, sharesClosingMatter: false);
    }

    public static BrochurePrintCompactPlan Plan(
        IReadOnlyList<BrochurePublicationProject> projects,
        string? visionaryText,
        string? newSimulatorsText)
        => Plan(
            projects.Select(project => new BrochurePrintPlanningItem(
                project.ProjectId,
                project.ProjectName,
                project.NarrativeWordCount,
                project.ImageMode,
                project.PrimaryPhoto is not null,
                project.SecondaryPhoto is not null)).ToArray(),
            visionaryText,
            newSimulatorsText);

    public static float EstimateProjectHeight(BrochurePrintPlanningItem project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var titleHeight = project.ProjectName.Length switch
        {
            > 115 => 30f,
            > 82 => 26f,
            _ => 22f
        };
        var bodySize = project.NarrativeWordCount switch
        {
            > 190 => 7.65f,
            > 155 => 7.85f,
            > 120 => 8.0f,
            _ => 8.15f
        };
        var imageWidth = project.NarrativeWordCount switch
        {
            > 180 => 112f,
            > 145 => 120f,
            > 110 => 128f,
            _ => 136f
        };

        var hasPrimary = project.HasPrimaryPhoto;
        var hasSecond = project.HasSecondaryPhoto && project.ImageMode != BrochureImageMode.Single;
        var wordsPerLine = hasPrimary
            ? project.NarrativeWordCount > 190 ? 8.8f : 9.15f
            : 14.2f;
        var lineCount = Math.Max(1, (int)Math.Ceiling(project.NarrativeWordCount / wordsPerLine));
        var textHeight = lineCount * bodySize * 1.08f;

        var imageHeight = 0f;
        if (hasPrimary)
        {
            var singleImageHeight = imageWidth * 9f / 16f;
            imageHeight = hasSecond
                ? (singleImageHeight * 2f) + 4f
                : singleImageHeight;
        }

        var rowHeight = Math.Max(textHeight, imageHeight);
        return titleHeight + rowHeight + 12f;
    }

    public static float EstimateClosingHeight(string? visionaryText, string? newSimulatorsText)
    {
        var visionaryWords = BrochureLayoutPlanner.CountWords(visionaryText);
        var newSimulatorWords = BrochureLayoutPlanner.CountWords(newSimulatorsText);

        var visionaryLines = Math.Max(1, (int)Math.Ceiling(visionaryWords / 14.8f));
        var newSimulatorLines = Math.Max(1, (int)Math.Ceiling(newSimulatorWords / 14.2f));

        var visionaryPanel = 45f + (visionaryLines * 7.6f * 1.08f);
        var newSimulatorPanel = 16f + (newSimulatorLines * 7.25f * 1.05f);
        var strapline = 14f;

        return Math.Clamp(visionaryPanel + newSimulatorPanel + strapline + 13f, 185f, 292f);
    }

    private static bool TryPlanSharedClosingPage(
        IReadOnlyList<float> heights,
        float closingHeight,
        int pageCount,
        out IReadOnlyList<PageRange> ranges)
        => TryPartition(
            heights,
            pageCount,
            finalPageReservedHeight: closingHeight + ClosingGapPoints,
            out ranges);

    private static bool TryPlanProjectOnlyPages(
        IReadOnlyList<float> heights,
        int pageCount,
        out IReadOnlyList<PageRange> ranges)
        => TryPartition(heights, pageCount, finalPageReservedHeight: 0f, out ranges);

    private static bool TryPartition(
        IReadOnlyList<float> heights,
        int pageCount,
        float finalPageReservedHeight,
        out IReadOnlyList<PageRange> ranges)
    {
        ranges = Array.Empty<PageRange>();
        var count = heights.Count;
        if (pageCount <= 0 || pageCount > count)
        {
            return false;
        }

        var prefix = new float[count + 1];
        for (var index = 0; index < count; index++)
        {
            prefix[index + 1] = prefix[index] + heights[index];
        }

        float GroupHeight(int start, int endExclusive)
        {
            var itemCount = endExclusive - start;
            if (itemCount <= 0)
            {
                return 0f;
            }

            return (prefix[endExclusive] - prefix[start])
                   + (InterModuleSpacingPoints * Math.Max(0, itemCount - 1));
        }

        var totalEstimated = heights.Sum()
                             + (InterModuleSpacingPoints * Math.Max(0, count - pageCount))
                             + finalPageReservedHeight;
        var targetPhysicalHeight = totalEstimated / pageCount;
        var infinity = double.PositiveInfinity;
        var cost = new double[pageCount + 1, count + 1];
        var previous = new int[pageCount + 1, count + 1];
        for (var page = 0; page <= pageCount; page++)
        {
            for (var index = 0; index <= count; index++)
            {
                cost[page, index] = infinity;
                previous[page, index] = -1;
            }
        }
        cost[0, 0] = 0d;

        for (var page = 1; page <= pageCount; page++)
        {
            var isFinal = page == pageCount;
            var reserved = isFinal ? finalPageReservedHeight : 0f;
            var available = ContentCapacityPoints - reserved;
            if (available <= 0f)
            {
                return false;
            }

            var minimumEnd = page;
            var maximumEnd = count - (pageCount - page);
            for (var end = minimumEnd; end <= maximumEnd; end++)
            {
                for (var start = page - 1; start < end; start++)
                {
                    if (double.IsPositiveInfinity(cost[page - 1, start]))
                    {
                        continue;
                    }

                    var groupHeight = GroupHeight(start, end);
                    if (groupHeight > available)
                    {
                        continue;
                    }

                    var physicalHeight = groupHeight + reserved;
                    var normalizedDeviation = (physicalHeight - targetPhysicalHeight) / ContentCapacityPoints;
                    var underfill = Math.Max(0d, .56d - (physicalHeight / ContentCapacityPoints));
                    var candidateCost = cost[page - 1, start]
                                        + (normalizedDeviation * normalizedDeviation)
                                        + (underfill * underfill * .45d);
                    if (candidateCost >= cost[page, end])
                    {
                        continue;
                    }

                    cost[page, end] = candidateCost;
                    previous[page, end] = start;
                }
            }
        }

        if (double.IsPositiveInfinity(cost[pageCount, count]))
        {
            return false;
        }

        var result = new PageRange[pageCount];
        var cursor = count;
        for (var page = pageCount; page >= 1; page--)
        {
            var start = previous[page, cursor];
            if (start < 0)
            {
                return false;
            }

            result[page - 1] = new PageRange(start, cursor);
            cursor = start;
        }

        ranges = result;
        return cursor == 0;
    }

    private static BrochurePrintCompactPlan BuildPlan(
        IReadOnlyList<PageRange> ranges,
        IReadOnlyList<float> heights,
        float closingHeight,
        bool sharesClosingMatter)
    {
        var pages = new List<BrochurePrintCompactPage>(ranges.Count + (sharesClosingMatter ? 0 : 1));

        for (var pageIndex = 0; pageIndex < ranges.Count; pageIndex++)
        {
            var range = ranges[pageIndex];
            var projectIndexes = Enumerable.Range(range.Start, range.EndExclusive - range.Start).ToArray();
            var groupHeight = projectIndexes.Sum(index => heights[index])
                              + (InterModuleSpacingPoints * Math.Max(0, projectIndexes.Length - 1));
            var includesClosing = sharesClosingMatter && pageIndex == ranges.Count - 1;
            var reserved = includesClosing ? closingHeight + ClosingGapPoints : 0f;
            var availableForProjects = ContentCapacityPoints - reserved;
            var expansion = projectIndexes.Length == 0
                ? 0f
                : Math.Clamp(
                    (availableForProjects - groupHeight) / projectIndexes.Length,
                    0f,
                    MaximumModuleExpansionPoints);
            var physicalUsed = groupHeight + (expansion * projectIndexes.Length) + reserved;

            pages.Add(new BrochurePrintCompactPage(
                projectIndexes,
                groupHeight,
                physicalUsed,
                ContentCapacityPoints,
                includesClosing,
                expansion));
        }

        if (!sharesClosingMatter)
        {
            pages.Add(new BrochurePrintCompactPage(
                Array.Empty<int>(),
                0f,
                closingHeight,
                ContentCapacityPoints,
                IncludesClosingMatter: true,
                ModuleExpansionPoints: 0f));
        }

        var averageUtilization = pages.Count == 0
            ? 0
            : (int)Math.Round(pages.Average(page => page.EstimatedPhysicalUsedPoints / page.CapacityPoints) * 100d);
        var closingPage = pages.Last(page => page.IncludesClosingMatter);

        return new BrochurePrintCompactPlan(
            pages,
            closingHeight,
            EstimatedTotalPageCount: 1 + pages.Count,
            AverageContentUtilizationPercent: Math.Clamp(averageUtilization, 0, 100),
            ClosingMatterSharesFinalPage: sharesClosingMatter,
            ClosingPageProjectCount: closingPage.ProjectIndexes.Count);
    }

    private sealed record PageRange(int Start, int EndExclusive);
}
