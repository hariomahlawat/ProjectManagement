using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Splits the Role &amp; Charter slide and sizes its charter type. The slide stays a single
/// institutional statement while its charter fits at <see cref="MinimumReadableFontSize"/> or
/// larger. A longer charter keeps the role and as many charter items as fit readably on the first
/// slide and continues, in source order, on "— Continued" slides. A continuation slide is only
/// made for items that remain, so there is never an empty one. The slide estimate and the
/// composer both use this, so the counted slides are the generated slides.
/// </summary>
public static class ProjectBriefingRoleCharterPaginator
{
    /// <summary>Charter type is never shrunk below this to keep everything on one slide.</summary>
    public const double MinimumReadableFontSize = 10.0;

    // Slide geometry, shared with the composer (inches).
    public const double SingleColumnWidth = 11.90;
    public const double TwoColumnWidth = 5.83;
    public const double CharterHeightBelowRole = 3.78;
    public const double CharterHeightFullSlide = 5.18;

    public static IReadOnlyList<ProjectBriefingRoleCharterPage> Paginate(
        ProjectBriefingRoleCharterData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var showRole = data.Layout != ProjectBriefingRoleCharterLayout.CharterOnly
            && data.RoleStatements.Count > 0;
        var roleStatements = showRole
            ? data.RoleStatements
            : Array.Empty<ProjectBriefingRoleCharterEntry>();
        var firstHeight = showRole ? CharterHeightBelowRole : CharterHeightFullSlide;
        var items = data.CharterItems;

        if (items.Count == 0 || FitsReadably(items, data.Layout, firstHeight))
        {
            return new[]
            {
                new ProjectBriefingRoleCharterPage(
                    IsContinuation: false,
                    PageNumber: 1,
                    RoleStatements: roleStatements,
                    CharterItems: items)
            };
        }

        var pages = new List<ProjectBriefingRoleCharterPage>();
        var start = 0;
        while (start < items.Count)
        {
            var isFirst = pages.Count == 0;
            var height = isFirst ? firstHeight : CharterHeightFullSlide;

            // The longest run of the remaining items that still fits readably; at least one item
            // per slide, so a single very long item is shrunk rather than looping for ever.
            var count = 1;
            for (var candidate = items.Count - start; candidate > 1; candidate--)
            {
                if (FitsReadably(Slice(items, start, candidate), data.Layout, height))
                {
                    count = candidate;
                    break;
                }
            }

            pages.Add(new ProjectBriefingRoleCharterPage(
                IsContinuation: !isFirst,
                PageNumber: pages.Count + 1,
                RoleStatements: isFirst ? roleStatements : Array.Empty<ProjectBriefingRoleCharterEntry>(),
                CharterItems: Slice(items, start, count)));
            start += count;
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

        return Paginate(new ProjectBriefingRoleCharterData
        {
            Title = normalized.Title,
            Layout = normalized.Layout,
            RoleStatements = normalized.Layout == ProjectBriefingRoleCharterLayout.CharterOnly
                ? Array.Empty<ProjectBriefingRoleCharterEntry>()
                : normalized.RoleStatements,
            CharterItems = normalized.CharterItems
        }).Count;
    }

    /// <summary>
    /// The largest charter typography that fits the items in the given layout and height,
    /// exactly as the slide lays them out (two columns split the items in half).
    /// </summary>
    public static ProjectBriefingCharterTypography ResolveTypography(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        ProjectBriefingRoleCharterLayout layout,
        double availableHeightInches)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (layout != ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter)
        {
            return ResolveTypography(
                items.Count,
                fontSize => TextLines(items, SingleColumnWidth, fontSize),
                availableHeightInches);
        }

        var leftCount = (int)Math.Ceiling(items.Count / 2d);
        var left = items.Take(leftCount).ToArray();
        var right = items.Skip(leftCount).ToArray();
        return ResolveTypography(
            Math.Max(left.Length, right.Length),
            fontSize => Math.Max(
                TextLines(left, TwoColumnWidth, fontSize),
                TextLines(right, TwoColumnWidth, fontSize)),
            availableHeightInches);
    }

    public static int EstimateLineCount(
        ProjectBriefingRoleCharterEntry item,
        int charactersPerLine)
    {
        ArgumentNullException.ThrowIfNull(item);

        var combinedLength = (item.LeadPhrase?.Trim().Length ?? 0)
            + (item.Text?.Trim().Length ?? 0)
            + 3;

        return Math.Max(
            1,
            (int)Math.Ceiling(
                combinedLength / (double)Math.Max(1, charactersPerLine)));
    }

    private static bool FitsReadably(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        ProjectBriefingRoleCharterLayout layout,
        double height)
        // The typography search steps down by 0.1 pt, so allow for floating-point drift.
        => ResolveTypography(items, layout, height).FontSize >= MinimumReadableFontSize - .05;

    private static IReadOnlyList<ProjectBriefingRoleCharterEntry> Slice(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        int start,
        int count)
        => items.Skip(start).Take(count).ToArray();

    // Wrapped lines the items take at a font size. The character budgets are for the largest
    // charter size (13.2 pt); smaller type fits proportionally more on a line. Measured on the
    // rendered slide a two-column line holds about 60 characters at 13.2 pt; the budgets stay
    // below that because PowerPoint only re-fits overflowing text once it is edited.
    private static double TextLines(
        IReadOnlyList<ProjectBriefingRoleCharterEntry> items,
        double width,
        double fontSize)
    {
        var charactersAtLargestSize = width >= 8d ? 116 : 54;
        var charactersPerLine = Math.Max(1, (int)Math.Floor(charactersAtLargestSize * 13.2d / Math.Max(1d, fontSize)));
        return items.Sum(item => EstimateLineCount(item, charactersPerLine));
    }

    private static ProjectBriefingCharterTypography ResolveTypography(
        int itemCount,
        Func<double, double> textLinesAt,
        double availableHeightInches)
    {
        if (itemCount <= 0)
        {
            return new ProjectBriefingCharterTypography(13.2, 8.0, 15.0);
        }

        // Estimate the usable vertical budget in points and choose the largest typography that
        // fits. PowerPoint normal auto-fit remains enabled as a final safeguard for unusual
        // wrapping caused by user-authored text.
        var usableHeightPoints = Math.Max(72d, (availableHeightInches - .34d) * 72d);

        for (var fontSize = 13.2d; fontSize >= 6.5d; fontSize -= .1d)
        {
            var density = fontSize switch
            {
                >= 12.4d => new ProjectBriefingCharterTypography(fontSize, 6.0d, fontSize * 1.14d),
                >= 11.2d => new ProjectBriefingCharterTypography(fontSize, 4.0d, fontSize * 1.12d),
                >= 10.0d => new ProjectBriefingCharterTypography(fontSize, 2.5d, fontSize * 1.10d),
                >= 8.5d => new ProjectBriefingCharterTypography(fontSize, 1.25d, fontSize * 1.08d),
                _ => new ProjectBriefingCharterTypography(fontSize, .5d, fontSize * 1.05d)
            };

            var requiredHeightPoints =
                (Math.Max(1d, textLinesAt(fontSize)) * density.LineSpacingPoints)
                + (Math.Max(0, itemCount - 1) * density.SpaceAfterPoints);

            if (requiredHeightPoints <= usableHeightPoints)
            {
                return density;
            }
        }

        // Only reached by a single, extremely long item on its own slide: the low baseline and
        // normal auto-fit prevent clipping while preserving the text.
        return new ProjectBriefingCharterTypography(6.5d, .25d, 6.8d);
    }
}

public sealed record ProjectBriefingRoleCharterPage(
    bool IsContinuation,
    int PageNumber,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> RoleStatements,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> CharterItems);

public sealed record ProjectBriefingCharterTypography(
    double FontSize,
    double SpaceAfterPoints,
    double LineSpacingPoints);
