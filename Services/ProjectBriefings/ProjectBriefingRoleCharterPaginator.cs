using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Geometry-aware pagination shared by the preflight estimator and the PowerPoint composer.
/// It preserves source order and never drops a configured role or charter entry.
/// </summary>
public static class ProjectBriefingRoleCharterPaginator
{
    private const int FirstPageTwoColumnMaximum = 10;
    private const int FirstPageSingleColumnMaximum = 8;
    private const int CharterOnlyMaximum = 12;
    private const int ContinuationMaximum = 12;

    public static IReadOnlyList<ProjectBriefingRoleCharterPage> Paginate(
        ProjectBriefingRoleCharterData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var pages = new List<ProjectBriefingRoleCharterPage>();
        var remaining = data.CharterItems.ToList();
        var showRole = data.Layout != ProjectBriefingRoleCharterLayout.CharterOnly
            && data.RoleStatements.Count > 0;
        var firstUsesTwoColumns = data.Layout == ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter;
        var firstMaximum = showRole
            ? (firstUsesTwoColumns ? FirstPageTwoColumnMaximum : FirstPageSingleColumnMaximum)
            : CharterOnlyMaximum;
        var firstLineBudget = showRole
            ? (firstUsesTwoColumns ? 13.0 : 24.0)
            : 31.0;
        var firstCount = ResolvePageItemCount(
            remaining,
            firstMaximum,
            firstUsesTwoColumns,
            firstLineBudget);

        pages.Add(new ProjectBriefingRoleCharterPage(
            IsContinuation: false,
            PageNumber: 1,
            RoleStatements: showRole
                ? data.RoleStatements
                : Array.Empty<ProjectBriefingRoleCharterEntry>(),
            CharterItems: remaining.Take(firstCount).ToArray()));
        remaining.RemoveRange(0, firstCount);

        while (remaining.Count > 0)
        {
            var count = ResolvePageItemCount(
                remaining,
                ContinuationMaximum,
                twoColumns: true,
                perColumnLineBudget: 17.0);
            pages.Add(new ProjectBriefingRoleCharterPage(
                IsContinuation: true,
                PageNumber: pages.Count + 1,
                RoleStatements: Array.Empty<ProjectBriefingRoleCharterEntry>(),
                CharterItems: remaining.Take(count).ToArray()));
            remaining.RemoveRange(0, count);
        }

        return pages;
    }

    public static int EstimateSlideCount(ProjectBriefingRoleCharterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = ProjectBriefingRoleCharterOptions.Normalize(
            options.IncludeSlide,
            options.Title,
            options.Layout,
            options.UseSharedContent,
            options.RoleStatements,
            options.CharterItems);
        if (!normalized.IncludeSlide)
        {
            return 0;
        }

        var data = new ProjectBriefingRoleCharterData
        {
            Title = normalized.Title,
            Layout = normalized.Layout,
            RoleStatements = normalized.Layout == ProjectBriefingRoleCharterLayout.CharterOnly
                ? Array.Empty<ProjectBriefingRoleCharterEntry>()
                : normalized.RoleStatements,
            CharterItems = normalized.CharterItems
        };
        return Paginate(data).Count;
    }

    public static int EstimateLineCount(
        ProjectBriefingRoleCharterEntry item,
        int charactersPerLine)
    {
        ArgumentNullException.ThrowIfNull(item);
        var combinedLength = (item.LeadPhrase?.Trim().Length ?? 0)
            + (item.Text?.Trim().Length ?? 0)
            + 3;
        return Math.Max(1, (int)Math.Ceiling(combinedLength / (double)Math.Max(1, charactersPerLine)));
    }

    private static int ResolvePageItemCount(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> remaining,
        int maximumItems,
        bool twoColumns,
        double perColumnLineBudget)
    {
        if (remaining.Count == 0)
        {
            return 0;
        }

        var upperBound = Math.Min(maximumItems, remaining.Count);
        for (var candidate = upperBound; candidate >= 1; candidate--)
        {
            var items = remaining.Take(candidate).ToArray();
            if (Fits(items, twoColumns, perColumnLineBudget))
            {
                return candidate;
            }
        }

        // A single exceptionally long item must still be preserved. The composer uses
        // text auto-fit as a final safety net for that isolated case.
        return 1;
    }

    private static bool Fits(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        bool twoColumns,
        double perColumnLineBudget)
    {
        if (!twoColumns)
        {
            return MeasureColumn(items, charactersPerLine: 104) <= perColumnLineBudget;
        }

        var leftCount = (int)Math.Ceiling(items.Count / 2d);
        var left = items.Take(leftCount).ToArray();
        var right = items.Skip(leftCount).ToArray();
        return MeasureColumn(left, charactersPerLine: 48) <= perColumnLineBudget
            && MeasureColumn(right, charactersPerLine: 48) <= perColumnLineBudget;
    }

    private static double MeasureColumn(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        int charactersPerLine)
        => items.Sum(item => EstimateLineCount(item, charactersPerLine) + .45);
}

public sealed record ProjectBriefingRoleCharterPage(
    bool IsContinuation,
    int PageNumber,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> RoleStatements,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> CharterItems);
