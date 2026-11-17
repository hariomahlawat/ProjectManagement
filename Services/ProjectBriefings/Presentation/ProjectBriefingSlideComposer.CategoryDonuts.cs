using System.Globalization;
using ProjectManagement.Services.Analytics.Presentation;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

// The category distributions as donut charts (owner, 17 Nov 2026; docs/ux/briefing-slides-review.md):
// a native PowerPoint doughnut with the deck's project total in its centre and a legend of every
// category with its count and share. With both distributions chosen they share one slide, side by
// side; one alone takes the slide with a larger ring and its concentration note.
public sealed partial class ProjectBriefingSlideComposer
{
    internal const string CombinedCategoryTitle = "Project and technical categories";

    // A slice colour for each category, largest first; distinct hues that read on both themes.
    // "Not categorised" is always the neutral grey, so it never looks like a real category.
    private static readonly string[] CategoryPaletteLight =
        { "0F766E", "2563EB", "D97706", "7C3AED", "DB2777", "16A34A", "EA580C", "0284C7", "65A30D" };
    private static readonly string[] CategoryPaletteDark =
        { "2DD4BF", "60A5FA", "FBBF24", "A78BFA", "F472B6", "4ADE80", "FB923C", "38BDF8", "A3E635" };

    // A ring shows at most this many slices; beyond it the smallest are one "Other categories" slice.
    internal const int MaximumCategorySlices = 9;

    private sealed record CategoryPanel(string Heading, IReadOnlyList<ProjectBriefingSummaryPoint> Points);

    private sealed record CategoryLegendRow(string Label, int Count, string Color, bool IsMuted);

    private static void AddCategoryDonutSlide(
        List<SlidePlan> plans,
        ProjectBriefingPresentationData data,
        string title,
        IReadOnlyList<CategoryPanel> panels)
        => plans.Add(new SlidePlan(
            SlidePlanKind.Summary,
            canvas => RenderCategoryDonutSlide(canvas, title, panels, data.Summary.ProjectCount),
            ProjectBriefingSlideRole.CategorySummary));

    private static void RenderCategoryDonutSlide(
        SlideCanvas canvas,
        string title,
        IReadOnlyList<CategoryPanel> panels,
        int totalProjects)
    {
        AddSlideTitle(canvas, title);
        if (panels.Count == 0 || panels.All(panel => panel.Points.All(point => point.Count <= 0)))
        {
            AddEmptyMessage(canvas, "No category data is available for the selected projects.");
            return;
        }

        const double top = 1.30;
        const double bottom = 6.88;
        const double left = .60;
        const double right = 12.73;
        if (panels.Count == 1)
        {
            RenderCategoryPanel(canvas, panels[0], totalProjects, left, top, right - left, bottom - top, wide: true);
            return;
        }

        const double gap = .25;
        var width = ((right - left) - gap) / 2d;
        for (var index = 0; index < panels.Count; index++)
        {
            RenderCategoryPanel(canvas, panels[index], totalProjects, left + (index * (width + gap)), top, width, bottom - top, wide: false);
        }
    }

    private static void RenderCategoryPanel(
        SlideCanvas canvas,
        CategoryPanel panel,
        int totalProjects,
        double x,
        double y,
        double width,
        double height,
        bool wide)
    {
        var theme = canvas.Theme;
        var rows = CategoryLegendRows(panel.Points, theme.IsDark);
        var categoryCount = panel.Points.Count(point => point.Count > 0);
        var total = rows.Sum(row => row.Count);

        canvas.AddRoundedRect(x, y, width, height, theme.Surface, theme.Border, .06, $"{panel.Heading} panel");
        canvas.AddText(x + .32, y + .22, width - .64, .34, panel.Heading.ToUpperInvariant(), wide ? 14 : 13, theme.NarrativeAccent, true, "l", "ctr", $"{panel.Heading} heading");
        canvas.AddText(
            x + .32,
            y + .56,
            width - .64,
            .30,
            $"{total.ToString(CultureInfo.InvariantCulture)} projects · {categoryCount.ToString(CultureInfo.InvariantCulture)} categories",
            wide ? 13 : 12,
            theme.TextSecondary,
            false,
            "l",
            "ctr",
            $"{panel.Heading} figures");

        // The headline: the largest category and its share, in words.
        var leader = rows.FirstOrDefault(row => !row.IsMuted);
        if (leader is not null && total > 0)
        {
            canvas.AddRichTextBox(
                x + .32,
                y + .92,
                width - .64,
                wide ? .46 : .40,
                new[]
                {
                    new RichTextParagraph(new[]
                    {
                        new RichTextRun(leader.Label, wide ? 18 : 15, leader.Color, Bold: true),
                        new RichTextRun(
                            $" is the largest: {leader.Count.ToString(CultureInfo.InvariantCulture)} of {total.ToString(CultureInfo.InvariantCulture)} projects ({RoundedShare(leader.Count, total)})",
                            wide ? 18 : 15,
                            theme.TextPrimary,
                            Bold: true)
                    })
                },
                $"{panel.Heading} headline",
                verticalAnchor: "ctr",
                allowAutoFit: true,
                leftInset: 0,
                rightInset: 0,
                topInset: 0,
                bottomInset: 0);
        }

        // The ring, with the total in its hole.
        // As large as the panel allows below the headline (and above the closing note when wide).
        var contentTop = 1.45;
        var contentBottomReserve = wide ? .85 : .25;
        var ringSize = Math.Min(wide ? 4.30 : 2.45, height - contentTop - contentBottomReserve - .10);
        var ringX = x + (wide ? .55 : .28);
        var ringY = y + contentTop + ((height - contentTop - contentBottomReserve - ringSize) / 2d);
        canvas.AddDoughnutChart(
            ringX,
            ringY,
            ringSize,
            rows.Select(row => new ChartSlice(row.Label, row.Count, row.Color)).ToArray(),
            "Projects",
            theme.Surface,
            $"{panel.Heading} chart");
        var centreSize = wide ? 40 : 30;
        canvas.AddText(ringX, ringY + (ringSize / 2d) - (wide ? .48 : .38), ringSize, wide ? .62 : .46, total.ToString(CultureInfo.InvariantCulture), centreSize, theme.TextPrimary, true, "ctr", "ctr", $"{panel.Heading} total");
        canvas.AddText(ringX, ringY + (ringSize / 2d) + (wide ? .12 : .06), ringSize, .30, "PROJECTS", wide ? 12 : 10.5, theme.TextMuted, true, "ctr", "ctr", $"{panel.Heading} total label");

        // The legend: every category with its count and share, largest first.
        var legendX = ringX + ringSize + (wide ? .60 : .28);
        var legendWidth = Math.Min(wide ? 5.60 : double.MaxValue, (x + width - .24) - legendX);
        var nameSize = wide ? 16.0 : 12.0;
        var countSize = wide ? 18.0 : 14.0;
        var shareSize = wide ? 13.0 : 11.0;
        var countWidth = wide ? .70 : .42;
        var shareWidth = wide ? .80 : .54;
        var nameWidth = legendWidth - .30 - countWidth - shareWidth;
        var lineHeight = nameSize * 1.22 / 72d;
        var rowPadding = wide ? .16 : .12;
        var heights = rows
            .Select(row => (Math.Min(3, AnalyticsSlideFrame.LineCount(row.Label, nameWidth - .12, nameSize)) * lineHeight) + rowPadding)
            .ToArray();
        var legendTop = y + contentTop;
        var legendBottom = y + height - contentBottomReserve;
        var scale = Math.Min(1d, (legendBottom - legendTop) / Math.Max(.01, heights.Sum()));
        var legendY = legendTop + Math.Max(0, ((legendBottom - legendTop) - heights.Sum()) / 2d);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowHeight = heights[index] * scale;
            canvas.AddRoundedRect(legendX, legendY + (rowHeight / 2d) - .09, .18, .18, row.Color, null, .25, $"{row.Label} key");
            canvas.AddText(legendX + .30, legendY, nameWidth, rowHeight, Truncate(row.Label, 60), nameSize, row.IsMuted ? theme.TextMuted : theme.TextPrimary, false, "l", "ctr", $"{row.Label} name");
            canvas.AddText(legendX + .30 + nameWidth, legendY, countWidth, rowHeight, row.Count.ToString(CultureInfo.InvariantCulture), countSize, theme.TextPrimary, true, "r", "ctr", $"{row.Label} count");
            canvas.AddText(legendX + .30 + nameWidth + countWidth, legendY, shareWidth, rowHeight, RoundedShare(row.Count, total), shareSize, theme.TextMuted, false, "r", "ctr", $"{row.Label} share");
            if (index < rows.Count - 1)
            {
                canvas.AddLine(legendX, legendY + rowHeight, legendX + legendWidth, legendY + rowHeight, theme.Divider, .5);
            }

            legendY += rowHeight;
        }

        // One category alone on the slide also says how concentrated the deck is.
        if (wide && rows.Count >= 2 && total > 0)
        {
            var leaders = rows.Where(row => !row.IsMuted).Take(2).ToArray();
            if (leaders.Length == 2)
            {
                var share = leaders.Sum(row => row.Count);
                canvas.AddText(
                    x + .55,
                    y + height - .72,
                    width - 1.10,
                    .42,
                    $"{leaders[0].Label} and {leaders[1].Label} account for {share.ToString(CultureInfo.InvariantCulture)} of {total.ToString(CultureInfo.InvariantCulture)} projects ({RoundedShare(share, total)}).",
                    14,
                    theme.TextSecondary,
                    false,
                    "l",
                    "ctr",
                    $"{panel.Heading} concentration");
            }
        }
    }

    // Largest first (ties by name); past the ring's limit the smallest become "Other categories".
    private static IReadOnlyList<CategoryLegendRow> CategoryLegendRows(IReadOnlyList<ProjectBriefingSummaryPoint> points, bool dark)
    {
        var palette = dark ? CategoryPaletteDark : CategoryPaletteLight;
        var grey = dark ? "6B7280" : "9AA3B2";
        var ordered = points
            .Where(point => point.Count > 0)
            .OrderBy(point => IsUncategorisedCategory(point.Label) ? 1 : 0)
            .ThenByDescending(point => point.Count)
            .ThenBy(point => point.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<CategoryLegendRow>();
        var named = ordered.Where(point => !IsUncategorisedCategory(point.Label)).ToList();
        var uncategorised = ordered.Where(point => IsUncategorisedCategory(point.Label)).ToList();
        var room = MaximumCategorySlices - (uncategorised.Count > 0 ? 1 : 0);
        var shown = named.Count > room ? named.Take(room - 1).ToList() : named;
        for (var index = 0; index < shown.Count; index++)
        {
            rows.Add(new CategoryLegendRow(shown[index].Label, shown[index].Count, palette[index % palette.Length], IsMuted: false));
        }

        if (shown.Count < named.Count)
        {
            var rest = named.Skip(shown.Count).ToList();
            rows.Add(new CategoryLegendRow(
                $"Other categories ({rest.Count.ToString(CultureInfo.InvariantCulture)})",
                rest.Sum(point => point.Count),
                dark ? "94A3B8" : "64748B",
                IsMuted: true));
        }

        if (uncategorised.Count > 0)
        {
            rows.Add(new CategoryLegendRow("Not categorised", uncategorised.Sum(point => point.Count), grey, IsMuted: true));
        }

        return rows;
    }

    private static string RoundedShare(int count, int total)
        => total <= 0
            ? "0%"
            : Math.Round(count * 100d / total, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
}
