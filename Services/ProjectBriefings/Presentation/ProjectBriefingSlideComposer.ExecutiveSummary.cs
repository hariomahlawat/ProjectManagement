using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private const double ExecutiveSummaryCardRadius = .07;
    private const double ExecutiveSummaryContentLeft = .65;
    private const double ExecutiveSummaryContentWidth = 12.03;

    private static void RenderExecutivePortfolioSummary(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        AddSlideTitle(canvas, "Portfolio at a glance");

        var summary = data.Summary;
        var statusCards = new List<ExecutivePortfolioStatus>
        {
            new("SELECTED PROJECTS", summary.ProjectCount, null, canvas.Theme.HeaderAccent),
            new(
                "COMPLETED",
                summary.CompletedCount,
                FormatPortfolioShare(summary.CompletedCount, summary.ProjectCount),
                canvas.Theme.Positive),
            new(
                "ONGOING",
                summary.OngoingCount,
                FormatPortfolioShare(summary.OngoingCount, summary.ProjectCount),
                canvas.Theme.Accent)
        };

        if (summary.CancelledCount > 0)
        {
            statusCards.Add(new(
                "CANCELLED",
                summary.CancelledCount,
                FormatPortfolioShare(summary.CancelledCount, summary.ProjectCount),
                canvas.Theme.Critical));
        }

        RenderPortfolioStatusCards(canvas, statusCards);

        var costMetrics = BuildPortfolioCostMetrics(canvas, data);
        if (costMetrics.Count == 0)
        {
            RenderPortfolioCostExclusionMessage(canvas);
            return;
        }

        RenderPortfolioCostMetrics(canvas, costMetrics, summary.ProjectCount);
    }

    private static IReadOnlyList<ExecutiveCostMetric> BuildPortfolioCostMetrics(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        var summary = data.Summary;
        var metrics = new List<ExecutiveCostMetric>();

        if (data.Layout == ProjectBriefingLayout.ProjectUpdateSheet)
        {
            metrics.Add(CreateCostMetric(
                canvas,
                "RECORDED R&D COST",
                summary.TotalCostRdInRupees,
                summary.CostRdRecordedCount,
                PortfolioFinancialRole.ResearchAndDevelopment,
                fixedTwoDecimalPrecision: true));
            metrics.Add(CreateCostMetric(
                canvas,
                "RECORDED IPA COST",
                summary.TotalIpaCostInRupees,
                summary.IpaCostRecordedCount,
                PortfolioFinancialRole.InPrincipleApproval,
                fixedTwoDecimalPrecision: true));
            return metrics;
        }

        if (data.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both)
        {
            metrics.Add(CreateCostMetric(
                canvas,
                "RECORDED R&D COST",
                summary.TotalCostRdInRupees,
                summary.CostRdRecordedCount,
                PortfolioFinancialRole.ResearchAndDevelopment));
        }

        if (data.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both)
        {
            metrics.Add(CreateCostMetric(
                canvas,
                "RECORDED PROLIFERATION COST",
                summary.TotalProliferationCostInRupees,
                summary.ProliferationCostRecordedCount,
                PortfolioFinancialRole.Proliferation));
        }

        return metrics;
    }

    private static ExecutiveCostMetric CreateCostMetric(
        SlideCanvas canvas,
        string title,
        decimal amount,
        int recorded,
        PortfolioFinancialRole role,
        bool fixedTwoDecimalPrecision = false)
    {
        var accent = ResolveFinancialAccent(canvas.Theme, role);
        var fill = BlendStageSummaryColor(
            accent,
            canvas.Theme.Canvas,
            canvas.Theme.IsDark ? .72 : .90);
        return new ExecutiveCostMetric(
            title,
            amount,
            recorded,
            accent,
            fill,
            fixedTwoDecimalPrecision);
    }

    private static string ResolveFinancialAccent(
        ProjectBriefingThemeDefinition theme,
        PortfolioFinancialRole role)
        => role switch
        {
            PortfolioFinancialRole.Proliferation => theme.SecondaryAccent,
            PortfolioFinancialRole.InPrincipleApproval => theme.HeaderAccent,
            _ => theme.Accent
        };

    private static void RenderPortfolioStatusCards(
        SlideCanvas canvas,
        IReadOnlyList<ExecutivePortfolioStatus> cards)
    {
        const double startX = .65;
        const double y = 1.38;
        const double totalWidth = 12.03;
        const double gap = .24;
        const double height = 1.40;
        var cardWidth = (totalWidth - (gap * (cards.Count - 1))) / cards.Count;

        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var x = startX + (index * (cardWidth + gap));
            var softFill = BlendStageSummaryColor(
                card.Accent,
                canvas.Theme.Canvas,
                canvas.Theme.IsDark ? .74 : .90);
            var border = BlendStageSummaryColor(
                card.Accent,
                canvas.Theme.Canvas,
                canvas.Theme.IsDark ? .54 : .50);
            var labelColour = canvas.Theme.IsDark
                ? canvas.Theme.TextSecondary
                : card.Accent;

            canvas.AddRoundedRect(
                x,
                y,
                cardWidth,
                height,
                softFill,
                border,
                ExecutiveSummaryCardRadius,
                $"{card.Label} portfolio card");
            canvas.AddRoundedRect(
                x,
                y,
                .07,
                height,
                card.Accent,
                null,
                .035,
                $"{card.Label} portfolio accent");

            canvas.AddText(
                x + .28,
                y + .18,
                cardWidth - .56,
                .24,
                card.Label,
                cards.Count > 3 ? 9.5 : 10.5,
                labelColour,
                true,
                "l",
                name: $"{card.Label} portfolio label");

            var valueText = card.Count.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(card.Share))
            {
                canvas.AddText(
                    x + .28,
                    y + .51,
                    cardWidth - .56,
                    .54,
                    valueText,
                    cards.Count > 3 ? 24.5 : 28.0,
                    canvas.Theme.TextPrimary,
                    true,
                    "l",
                    name: $"{card.Label} portfolio count");
                continue;
            }

            canvas.AddRichTextBox(
                x + .28,
                y + .47,
                cardWidth - .56,
                .62,
                new[]
                {
                    new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                valueText,
                                cards.Count > 3 ? 23.0 : 27.0,
                                canvas.Theme.TextPrimary,
                                Bold: true),
                            new RichTextRun(
                                $"  {card.Share}",
                                cards.Count > 3 ? 10.7 : 12.1,
                                card.Accent,
                                Bold: true)
                        },
                        Align: "l")
                },
                $"{card.Label} portfolio count and share",
                verticalAnchor: "ctr",
                allowAutoFit: false,
                leftInset: 0,
                rightInset: 0,
                topInset: 0,
                bottomInset: 0);
        }
    }

    private static string FormatPortfolioShare(int count, int total)
        => $"({FormatPercentage(count, total)})";

    private static void RenderPortfolioCostMetrics(
        SlideCanvas canvas,
        IReadOnlyList<ExecutiveCostMetric> metrics,
        int totalProjects)
    {
        var visible = metrics.Take(4).ToArray();

        switch (visible.Length)
        {
            case 1:
                RenderSingleRecordedCostCard(
                    canvas,
                    visible[0],
                    totalProjects);
                return;

            case 2:
                RenderRecordedCostCard(canvas, .65, 3.48, 5.86, 2.02, visible[0], totalProjects);
                RenderRecordedCostCard(canvas, 6.82, 3.48, 5.86, 2.02, visible[1], totalProjects);
                return;

            case 3:
            {
                const double gap = .22;
                var width = (ExecutiveSummaryContentWidth - (2 * gap)) / 3d;
                for (var index = 0; index < visible.Length; index++)
                {
                    RenderRecordedCostCard(
                        canvas,
                        ExecutiveSummaryContentLeft + (index * (width + gap)),
                        3.45,
                        width,
                        2.04,
                        visible[index],
                        totalProjects);
                }

                return;
            }

            default:
                RenderRecordedCostCard(canvas, .65, 3.23, 5.86, 1.44, visible[0], totalProjects);
                RenderRecordedCostCard(canvas, 6.82, 3.23, 5.86, 1.44, visible[1], totalProjects);
                RenderRecordedCostCard(canvas, .65, 4.89, 5.86, 1.44, visible[2], totalProjects);
                RenderRecordedCostCard(canvas, 6.82, 4.89, 5.86, 1.44, visible[3], totalProjects);
                return;
        }
    }

    private static void RenderSingleRecordedCostCard(
        SlideCanvas canvas,
        ExecutiveCostMetric metric,
        int totalProjects)
    {
        const double x = 2.15;
        const double y = 3.48;
        const double width = 9.03;
        const double height = 1.78;
        var labelColour = canvas.Theme.IsDark
            ? canvas.Theme.TextSecondary
            : metric.Accent;

        canvas.AddRoundedRect(
            x,
            y,
            width,
            height,
            metric.Fill,
            BlendStageSummaryColor(metric.Accent, canvas.Theme.Canvas, .36),
            ExecutiveSummaryCardRadius,
            $"{metric.Title} card");

        canvas.AddText(
            x + .36,
            y + .24,
            4.45,
            .24,
            metric.Title,
            10.6,
            labelColour,
            true,
            "l",
            name: $"{metric.Title} label");

        canvas.AddText(
            x + .36,
            y + .63,
            4.48,
            .46,
            FormatRecordedAmount(metric),
            24.0,
            canvas.Theme.TextPrimary,
            true,
            "l",
            name: $"{metric.Title} value");

        canvas.AddLine(
            x + 5.17,
            y + .26,
            x + 5.17,
            y + 1.50,
            BlendStageSummaryColor(canvas.Theme.Divider, canvas.Theme.Canvas, .26),
            .65);

        var coveragePercent = totalProjects > 0
            ? metric.Recorded * 100d / totalProjects
            : 0d;
        canvas.AddText(
            x + 5.55,
            y + .24,
            2.95,
            .22,
            "DATA COVERAGE",
            9.9,
            labelColour,
            true,
            "l",
            name: $"{metric.Title} coverage label");
        canvas.AddText(
            x + 5.55,
            y + .58,
            2.95,
            .34,
            totalProjects > 0
                ? $"{metric.Recorded.ToString(CultureInfo.InvariantCulture)} of {totalProjects.ToString(CultureInfo.InvariantCulture)} projects"
                : "No projects selected",
            17.5,
            canvas.Theme.TextPrimary,
            true,
            "l",
            name: $"{metric.Title} coverage value");
        canvas.AddText(
            x + 5.55,
            y + .96,
            2.95,
            .22,
            totalProjects > 0
                ? $"{coveragePercent:0.#}% recorded"
                : "0% recorded",
            9.6,
            canvas.Theme.TextMuted,
            false,
            "l",
            name: $"{metric.Title} coverage percentage");

        const double trackWidth = 2.93;
        canvas.AddRoundedRect(
            x + 5.55,
            y + 1.35,
            trackWidth,
            .10,
            BlendStageSummaryColor(canvas.Theme.SurfaceMuted, canvas.Theme.Canvas, .16),
            null,
            .05,
            $"{metric.Title} coverage track");
        if (metric.Recorded > 0 && totalProjects > 0)
        {
            canvas.AddRoundedRect(
                x + 5.55,
                y + 1.35,
                Math.Max(.10, trackWidth * metric.Recorded / totalProjects),
                .10,
                metric.Accent,
                null,
                .05,
                $"{metric.Title} coverage fill");
        }
    }

    private static void RenderRecordedCostCard(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        double height,
        ExecutiveCostMetric metric,
        int totalProjects)
    {
        var compact = height < 1.70 || width < 4.30;
        var labelColour = canvas.Theme.IsDark
            ? canvas.Theme.TextSecondary
            : metric.Accent;
        var horizontalPadding = compact ? .24 : .30;

        canvas.AddRoundedRect(
            x,
            y,
            width,
            height,
            metric.Fill,
            BlendStageSummaryColor(metric.Accent, canvas.Theme.Canvas, .36),
            ExecutiveSummaryCardRadius,
            $"{metric.Title} card");

        canvas.AddText(
            x + horizontalPadding,
            y + (compact ? .14 : .24),
            width - (2 * horizontalPadding),
            .24,
            metric.Title,
            compact ? 8.9 : 10.8,
            labelColour,
            true,
            "l",
            name: $"{metric.Title} label");

        canvas.AddText(
            x + horizontalPadding,
            y + (compact ? .43 : .65),
            width - (2 * horizontalPadding),
            compact ? .34 : .48,
            FormatRecordedAmount(metric),
            compact ? 18.0 : 24.5,
            canvas.Theme.TextPrimary,
            true,
            "l",
            name: $"{metric.Title} value");

        var coverageText = totalProjects > 0
            ? $"{metric.Recorded.ToString(CultureInfo.InvariantCulture)} of " +
              $"{totalProjects.ToString(CultureInfo.InvariantCulture)} projects recorded"
            : "No projects selected";
        canvas.AddText(
            x + horizontalPadding,
            y + height - (compact ? .49 : .72),
            width - (2 * horizontalPadding),
            .22,
            coverageText,
            compact ? 8.1 : 9.7,
            canvas.Theme.TextMuted,
            false,
            "l",
            name: $"{metric.Title} coverage");

        var trackX = x + horizontalPadding;
        var trackY = y + height - (compact ? .20 : .35);
        var trackWidth = width - (2 * horizontalPadding);
        var trackHeight = compact ? .08 : .10;
        canvas.AddRoundedRect(
            trackX,
            trackY,
            trackWidth,
            trackHeight,
            BlendStageSummaryColor(canvas.Theme.SurfaceMuted, canvas.Theme.Canvas, .16),
            null,
            trackHeight / 2d,
            $"{metric.Title} coverage track");
        if (metric.Recorded > 0 && totalProjects > 0)
        {
            canvas.AddRoundedRect(
                trackX,
                trackY,
                Math.Max(trackHeight, trackWidth * metric.Recorded / totalProjects),
                trackHeight,
                metric.Accent,
                null,
                trackHeight / 2d,
                $"{metric.Title} coverage fill");
        }
    }

    private static string FormatRecordedAmount(ExecutiveCostMetric metric)
        => metric.Recorded > 0
            ? ProjectBriefingCurrencyFormatter.FormatRupees(
                metric.Amount,
                minimumDecimalPlaces: metric.FixedTwoDecimalPrecision ? 2 : 0)
            : "Not recorded";

    private static void RenderPortfolioCostExclusionMessage(SlideCanvas canvas)
    {
        canvas.AddRoundedRect(
            ExecutiveSummaryContentLeft,
            3.48,
            ExecutiveSummaryContentWidth,
            1.82,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            ExecutiveSummaryCardRadius,
            "Portfolio cost exclusion message");
        canvas.AddText(
            .95,
            4.08,
            11.43,
            .42,
            "Cost information is not included in this deck.",
            17.0,
            canvas.Theme.TextPrimary,
            true,
            "c",
            name: "Portfolio cost exclusion text");
    }

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

    private sealed record ExecutivePortfolioStatus(
        string Label,
        int Count,
        string? Share,
        string Accent);

    private sealed record ExecutiveCostMetric(
        string Title,
        decimal Amount,
        int Recorded,
        string Accent,
        string Fill,
        bool FixedTwoDecimalPrecision);

    private enum PortfolioFinancialRole
    {
        ResearchAndDevelopment,
        Proliferation,
        InPrincipleApproval
    }
}
