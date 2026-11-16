using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static void RenderAdaptiveCategorySummary(
        SlideCanvas canvas,
        string title,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        int totalProjects,
        int totalCategories,
        ThemeAccent accentRole,
        int pageNumber,
        int pageCount,
        int rankOffset)
    {
        var slideTitle = pageNumber > 1 ? $"{title} — Continued" : title;
        var subtitle = pageCount > 1 ? $"Page {pageNumber} of {pageCount}" : null;
        AddSlideTitle(canvas, slideTitle, subtitle);

        if (points.Count == 0)
        {
            AddEmptyMessage(canvas, "No category data is available for the selected projects.");
            return;
        }

        var accent = ResolveAccent(canvas.Theme, accentRole);
        canvas.AddText(
            4.30,
            1.08,
            4.73,
            .20,
            $"{totalProjects.ToString(CultureInfo.InvariantCulture)} SELECTED PROJECTS · " +
            $"{totalCategories.ToString(CultureInfo.InvariantCulture)} CATEGORIES",
            10.6,
            canvas.Theme.TextSecondary,
            true,
            "c",
            name: $"{title} summary eyebrow");

        var showInsight = pageNumber == 1 && points.Count >= 2 && totalProjects > 0;
        RenderRankedCategoryBars(
            canvas,
            points,
            totalProjects,
            accent,
            rankOffset,
            showInsight);

        if (showInsight)
        {
            RenderCategoryConcentrationInsight(canvas, points, totalProjects, accent, title);
        }
    }

    private static void RenderRankedCategoryBars(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        int totalProjects,
        string accent,
        int rankOffset,
        bool reserveInsight)
    {
        var maximum = Math.Max(1, points.Max(point => point.Count));
        var compact = points.Count >= 6;
        var dense = points.Count >= 11;
        var top = dense ? 1.40 : compact ? 1.50 : 1.67;
        var bottom = reserveInsight ? 6.24 : 6.73;
        var availableHeight = bottom - top;
        var rowHeight = Math.Min(
            dense ? .43 : compact ? .64 : .88,
            availableHeight / points.Count);
        var usedHeight = rowHeight * points.Count;
        if (!compact && usedHeight < availableHeight)
        {
            top += Math.Min(.48, (availableHeight - usedHeight) * .18);
        }

        var labelFont = dense ? 9.3 : compact ? 10.6 : 12.2;
        var countFont = dense ? 10.5 : compact ? 11.7 : 13.1;
        var shareFont = dense ? 8.6 : compact ? 9.4 : 10.1;
        var trackHeight = dense ? .105 : compact ? .13 : .17;

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var y = top + (index * rowHeight);
            var rowAccent = ResolveCategoryBarAccent(
                canvas,
                point,
                index,
                accent);

            canvas.AddText(
                .70,
                y + .01,
                .38,
                .25,
                (rankOffset + index + 1).ToString("00", CultureInfo.InvariantCulture),
                dense ? 8.2 : compact ? 8.6 : 9.2,
                canvas.Theme.TextMuted,
                true,
                "c",
                name: $"{point.Label} category rank");
            canvas.AddText(
                1.18,
                y,
                7.55,
                .29,
                Truncate(point.Label, dense ? 52 : 58),
                labelFont,
                IsUncategorisedCategory(point.Label)
                    ? canvas.Theme.Warning
                    : canvas.Theme.TextPrimary,
                true,
                "l",
                name: $"{point.Label} category label");
            canvas.AddText(
                9.65,
                y,
                .62,
                .29,
                point.Count.ToString(CultureInfo.InvariantCulture),
                countFont,
                rowAccent,
                true,
                "r",
                name: $"{point.Label} category count");
            canvas.AddText(
                10.52,
                y,
                1.16,
                .29,
                FormatPercentage(point.Count, totalProjects),
                shareFont,
                canvas.Theme.TextMuted,
                false,
                "r",
                name: $"{point.Label} category share");

            const double trackX = 1.18;
            const double trackWidth = 11.12;
            var trackY = y + rowHeight - trackHeight - .08;
            canvas.AddRoundedRect(
                trackX,
                trackY,
                trackWidth,
                trackHeight,
                BlendStageSummaryColor(canvas.Theme.SurfaceMuted, canvas.Theme.Canvas, .12),
                null,
                trackHeight / 2d,
                $"{point.Label} category track");
            canvas.AddRoundedRect(
                trackX,
                trackY,
                Math.Max(trackHeight, trackWidth * point.Count / maximum),
                trackHeight,
                rowAccent,
                null,
                trackHeight / 2d,
                $"{point.Label} category bar");

            if (index < points.Count - 1)
            {
                canvas.AddLine(
                    .68,
                    y + rowHeight - .015,
                    12.42,
                    y + rowHeight - .015,
                    BlendStageSummaryColor(canvas.Theme.Divider, canvas.Theme.Canvas, .58),
                    .35);
            }
        }
    }

    private static string ResolveCategoryBarAccent(
        SlideCanvas canvas,
        ProjectBriefingSummaryPoint point,
        int index,
        string accent)
    {
        if (IsUncategorisedCategory(point.Label))
        {
            return BlendStageSummaryColor(
                canvas.Theme.Warning,
                canvas.Theme.Canvas,
                canvas.Theme.IsDark ? .10 : .20);
        }

        return index switch
        {
            0 => accent,
            1 => BlendStageSummaryColor(accent, canvas.Theme.Canvas, .10),
            _ => BlendStageSummaryColor(
                accent,
                canvas.Theme.Canvas,
                canvas.Theme.IsDark ? .28 : .35)
        };
    }

    private static bool IsUncategorisedCategory(string label)
    {
        var normalised = label.Trim().ToLowerInvariant();
        return normalised.Contains("not categorised", StringComparison.Ordinal)
               || normalised.Contains("not categorized", StringComparison.Ordinal)
               || normalised.Contains("uncategorised", StringComparison.Ordinal)
               || normalised.Contains("uncategorized", StringComparison.Ordinal);
    }

    private static void RenderCategoryConcentrationInsight(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        int totalProjects,
        string accent,
        string title)
    {
        var first = points[0];
        var second = points[1];
        var combined = first.Count + second.Count;
        var combinedShare = FormatPercentage(combined, totalProjects);
        var firstLabel = Truncate(first.Label, 28);
        var secondLabel = Truncate(second.Label, 28);
        var insightText =
            $"{firstLabel} and {secondLabel} account for " +
            $"{combined.ToString(CultureInfo.InvariantCulture)} of " +
            $"{totalProjects.ToString(CultureInfo.InvariantCulture)} projects " +
            $"({combinedShare})";

        var fill = BlendStageSummaryColor(
            accent,
            canvas.Theme.Canvas,
            canvas.Theme.IsDark ? .72 : .90);
        var border = BlendStageSummaryColor(
            accent,
            canvas.Theme.Canvas,
            canvas.Theme.IsDark ? .28 : .42);

        canvas.AddRoundedRect(
            1.05,
            6.42,
            11.23,
            .43,
            fill,
            border,
            .05,
            $"{title} concentration insight");
        canvas.AddRoundedRect(
            1.05,
            6.42,
            .06,
            .43,
            accent,
            null,
            .03,
            $"{title} concentration insight accent");
        var insightFontSize = insightText.Length switch
        {
            > 112 => 9.2,
            > 94 => 9.7,
            _ => 10.3
        };

        canvas.AddText(
            1.34,
            6.50,
            10.65,
            .25,
            insightText,
            insightFontSize,
            canvas.Theme.IsDark
                ? canvas.Theme.TextPrimary
                : canvas.Theme.TextSecondary,
            true,
            "l",
            name: $"{title} concentration insight text");
    }
}
