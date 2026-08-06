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
            new("COMPLETED", summary.CompletedCount, FormatPercentage(summary.CompletedCount, summary.ProjectCount), canvas.Theme.Positive),
            new("ONGOING", summary.OngoingCount, FormatPercentage(summary.OngoingCount, summary.ProjectCount), canvas.Theme.Accent)
        };

        if (summary.CancelledCount > 0)
        {
            statusCards.Add(new(
                "CANCELLED",
                summary.CancelledCount,
                FormatPercentage(summary.CancelledCount, summary.ProjectCount),
                canvas.Theme.Critical));
        }

        RenderPortfolioStatusCards(canvas, statusCards);

        if (data.Layout == ProjectBriefingLayout.ProjectUpdateSheet)
        {
            RenderRecordedCostPair(
                canvas,
                "RECORDED R&D COST",
                summary.TotalCostRdInRupees,
                summary.CostRdRecordedCount,
                canvas.Theme.Accent,
                canvas.Theme.AccentSoft,
                "RECORDED IPA COST",
                summary.TotalIpaCostInRupees,
                summary.IpaCostRecordedCount,
                canvas.Theme.SecondaryAccent,
                canvas.Theme.SecondaryAccentSoft,
                summary.ProjectCount,
                fixedTwoDecimalPrecision: true);
            return;
        }

        var showRd = data.CostMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both;
        var showProliferation = data.CostMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both;

        if (showRd && showProliferation)
        {
            RenderRecordedCostPair(
                canvas,
                "RECORDED R&D COST",
                summary.TotalCostRdInRupees,
                summary.CostRdRecordedCount,
                canvas.Theme.Accent,
                canvas.Theme.AccentSoft,
                "RECORDED PROLIFERATION COST",
                summary.TotalProliferationCostInRupees,
                summary.ProliferationCostRecordedCount,
                canvas.Theme.SecondaryAccent,
                canvas.Theme.SecondaryAccentSoft,
                summary.ProjectCount);
            return;
        }

        if (showRd)
        {
            RenderRecordedCostCard(
                canvas,
                2.00,
                3.48,
                9.33,
                "RECORDED R&D COST",
                summary.TotalCostRdInRupees,
                summary.CostRdRecordedCount,
                summary.ProjectCount,
                canvas.Theme.Accent,
                canvas.Theme.AccentSoft);
            return;
        }

        if (showProliferation)
        {
            RenderRecordedCostCard(
                canvas,
                2.00,
                3.48,
                9.33,
                "RECORDED PROLIFERATION COST",
                summary.TotalProliferationCostInRupees,
                summary.ProliferationCostRecordedCount,
                summary.ProjectCount,
                canvas.Theme.SecondaryAccent,
                canvas.Theme.SecondaryAccentSoft);
            return;
        }

        canvas.AddRoundedRect(
            ExecutiveSummaryContentLeft,
            3.48,
            ExecutiveSummaryContentWidth,
            1.96,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            ExecutiveSummaryCardRadius,
            "Portfolio cost exclusion message");
        canvas.AddText(
            .95,
            4.12,
            11.43,
            .42,
            "Cost information is not included in this deck.",
            17.5,
            canvas.Theme.TextPrimary,
            true,
            "c",
            name: "Portfolio cost exclusion text");
    }

    private static void RenderPortfolioStatusCards(
        SlideCanvas canvas,
        IReadOnlyList<ExecutivePortfolioStatus> cards)
    {
        const double startX = .65;
        const double y = 1.35;
        const double totalWidth = 12.03;
        const double gap = .24;
        const double height = 1.52;
        var cardWidth = (totalWidth - (gap * (cards.Count - 1))) / cards.Count;

        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var x = startX + (index * (cardWidth + gap));
            var softFill = BlendStageSummaryColor(card.Accent, canvas.Theme.Canvas, canvas.Theme.IsDark ? .72 : .90);

            canvas.AddRoundedRect(
                x,
                y,
                cardWidth,
                height,
                softFill,
                BlendStageSummaryColor(card.Accent, canvas.Theme.Canvas, .50),
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
                y + .20,
                cardWidth - .56,
                .25,
                card.Label,
                cards.Count > 3 ? 9.7 : 10.7,
                card.Accent,
                true,
                "l",
                name: $"{card.Label} portfolio label");

            var valueText = card.Count.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(card.Share))
            {
                canvas.AddText(
                    x + .28,
                    y + .57,
                    cardWidth - .56,
                    .55,
                    valueText,
                    cards.Count > 3 ? 25.5 : 29.0,
                    canvas.Theme.TextPrimary,
                    true,
                    "l",
                    name: $"{card.Label} portfolio count");
            }
            else
            {
                canvas.AddRichTextBox(
                    x + .28,
                    y + .53,
                    cardWidth - .56,
                    .62,
                    new[]
                    {
                        new RichTextParagraph(
                            new[]
                            {
                                new RichTextRun(
                                    valueText,
                                    cards.Count > 3 ? 24.0 : 28.0,
                                    canvas.Theme.TextPrimary,
                                    Bold: true),
                                new RichTextRun(
                                    $"  {card.Share}",
                                    cards.Count > 3 ? 11.0 : 12.5,
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
    }

    private static void RenderRecordedCostPair(
        SlideCanvas canvas,
        string leftTitle,
        decimal leftAmount,
        int leftRecorded,
        string leftAccent,
        string leftFill,
        string rightTitle,
        decimal rightAmount,
        int rightRecorded,
        string rightAccent,
        string rightFill,
        int total,
        bool fixedTwoDecimalPrecision = false)
    {
        RenderRecordedCostCard(
            canvas,
            .65,
            3.48,
            5.86,
            leftTitle,
            leftAmount,
            leftRecorded,
            total,
            leftAccent,
            leftFill,
            fixedTwoDecimalPrecision);
        RenderRecordedCostCard(
            canvas,
            6.82,
            3.48,
            5.86,
            rightTitle,
            rightAmount,
            rightRecorded,
            total,
            rightAccent,
            rightFill,
            fixedTwoDecimalPrecision);
    }

    private static void RenderRecordedCostCard(
        SlideCanvas canvas,
        double x,
        double y,
        double width,
        string title,
        decimal amount,
        int recorded,
        int total,
        string accent,
        string fill,
        bool fixedTwoDecimalPrecision = false)
    {
        canvas.AddRoundedRect(
            x,
            y,
            width,
            2.02,
            fill,
            BlendStageSummaryColor(accent, canvas.Theme.Canvas, .35),
            ExecutiveSummaryCardRadius,
            $"{title} card");

        canvas.AddText(
            x + .30,
            y + .24,
            width - .60,
            .27,
            title,
            10.8,
            accent,
            true,
            "l",
            name: $"{title} label");

        var amountDisplay = recorded > 0
            ? ProjectBriefingCurrencyFormatter.FormatRupees(
                amount,
                minimumDecimalPlaces: fixedTwoDecimalPrecision ? 2 : 0)
            : "Not recorded";
        canvas.AddText(
            x + .30,
            y + .65,
            width - .60,
            .48,
            amountDisplay,
            24.5,
            canvas.Theme.TextPrimary,
            true,
            "l",
            name: $"{title} value");

        var coverageText = total > 0
            ? $"{recorded.ToString(CultureInfo.InvariantCulture)} of {total.ToString(CultureInfo.InvariantCulture)} projects recorded"
            : "No projects selected";
        canvas.AddText(
            x + .30,
            y + 1.30,
            width - .60,
            .24,
            coverageText,
            9.7,
            canvas.Theme.TextMuted,
            false,
            "l",
            name: $"{title} coverage");

        var trackX = x + .30;
        var trackY = y + 1.67;
        var trackWidth = width - .60;
        canvas.AddRoundedRect(
            trackX,
            trackY,
            trackWidth,
            .10,
            BlendStageSummaryColor(canvas.Theme.SurfaceMuted, canvas.Theme.Canvas, .18),
            null,
            .05,
            $"{title} coverage track");
        if (recorded > 0 && total > 0)
        {
            canvas.AddRoundedRect(
                trackX,
                trackY,
                Math.Max(.10, trackWidth * recorded / total),
                .10,
                accent,
                null,
                .05,
                $"{title} coverage fill");
        }
    }

    private static void RenderAdaptiveCategorySummary(
        SlideCanvas canvas,
        string title,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        int totalProjects,
        int totalCategories,
        ThemeAccent accentRole,
        int pageNumber,
        int pageCount)
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

        if (points.Count <= 5)
        {
            RenderRankedCategoryBars(canvas, points, totalProjects, accent, compact: false);
            return;
        }

        if (points.Count <= 10)
        {
            RenderTwoColumnCategoryDistribution(canvas, points, totalProjects, accent);
            return;
        }

        RenderRankedCategoryBars(canvas, points, totalProjects, accent, compact: true);
    }

    private static void RenderRankedCategoryBars(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        int totalProjects,
        string accent,
        bool compact)
    {
        var maximum = Math.Max(1, points.Max(point => point.Count));
        var leaders = points.Count(point => point.Count == maximum);
        var top = compact ? 1.47 : 1.66;
        var height = compact ? 5.20 : 4.78;
        var rowHeight = height / points.Count;
        var labelFont = compact ? 10.4 : 12.3;
        var countFont = compact ? 11.2 : 13.0;
        var trackHeight = compact ? .16 : .22;

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var y = top + (index * rowHeight);
            var isUniqueLeader = leaders == 1 && point.Count == maximum;
            var rowAccent = isUniqueLeader
                ? accent
                : BlendStageSummaryColor(accent, canvas.Theme.Canvas, .18);

            canvas.AddText(
                .70,
                y,
                .42,
                rowHeight,
                (index + 1).ToString("00", CultureInfo.InvariantCulture),
                compact ? 8.5 : 9.2,
                canvas.Theme.TextMuted,
                true,
                "c",
                name: $"{point.Label} category rank");
            canvas.AddText(
                1.20,
                y,
                compact ? 3.15 : 3.35,
                rowHeight,
                Truncate(point.Label, compact ? 46 : 42),
                labelFont,
                canvas.Theme.TextPrimary,
                true,
                "l",
                name: $"{point.Label} category label");

            var trackX = compact ? 4.45 : 4.72;
            var trackWidth = compact ? 6.50 : 6.18;
            var trackY = y + ((rowHeight - trackHeight) / 2d);
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
                Math.Max(.16, trackWidth * point.Count / maximum),
                trackHeight,
                rowAccent,
                null,
                trackHeight / 2d,
                $"{point.Label} category bar");

            canvas.AddText(
                11.05,
                y,
                .55,
                rowHeight,
                point.Count.ToString(CultureInfo.InvariantCulture),
                countFont,
                canvas.Theme.TextPrimary,
                true,
                "r",
                name: $"{point.Label} category count");
            canvas.AddText(
                11.72,
                y,
                .68,
                rowHeight,
                FormatPercentage(point.Count, totalProjects),
                compact ? 9.2 : 10.1,
                canvas.Theme.TextMuted,
                false,
                "r",
                name: $"{point.Label} category share");
        }
    }

    private static void RenderTwoColumnCategoryDistribution(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        int totalProjects,
        string accent)
    {
        const double leftX = .65;
        const double rightX = 6.78;
        const double columnWidth = 5.90;
        const double top = 1.55;
        const double rowHeight = .93;
        var maximum = Math.Max(1, points.Max(point => point.Count));
        var leaders = points.Count(point => point.Count == maximum);
        var firstColumnCount = (int)Math.Ceiling(points.Count / 2d);

        for (var index = 0; index < points.Count; index++)
        {
            var column = index < firstColumnCount ? 0 : 1;
            var row = column == 0 ? index : index - firstColumnCount;
            var x = column == 0 ? leftX : rightX;
            var y = top + (row * rowHeight);
            var point = points[index];
            var isUniqueLeader = leaders == 1 && point.Count == maximum;
            var rowAccent = isUniqueLeader
                ? accent
                : BlendStageSummaryColor(accent, canvas.Theme.Canvas, .18);

            if (row > 0)
            {
                canvas.AddLine(
                    x,
                    y - .05,
                    x + columnWidth,
                    y - .05,
                    BlendStageSummaryColor(canvas.Theme.Divider, canvas.Theme.Canvas, .35),
                    .45);
            }

            canvas.AddText(
                x,
                y + .03,
                .40,
                .24,
                (index + 1).ToString("00", CultureInfo.InvariantCulture),
                8.4,
                canvas.Theme.TextMuted,
                true,
                "c",
                name: $"{point.Label} category rank");
            canvas.AddText(
                x + .48,
                y,
                3.65,
                .31,
                Truncate(point.Label, 38),
                10.6,
                canvas.Theme.TextPrimary,
                true,
                "l",
                name: $"{point.Label} category label");
            canvas.AddText(
                x + 4.25,
                y,
                .52,
                .31,
                point.Count.ToString(CultureInfo.InvariantCulture),
                11.5,
                rowAccent,
                true,
                "r",
                name: $"{point.Label} category count");
            canvas.AddText(
                x + 4.84,
                y,
                .94,
                .31,
                FormatPercentage(point.Count, totalProjects),
                9.0,
                canvas.Theme.TextMuted,
                false,
                "r",
                name: $"{point.Label} category share");

            const double trackXOffset = .48;
            const double trackWidth = 5.30;
            const double trackHeight = .13;
            canvas.AddRoundedRect(
                x + trackXOffset,
                y + .47,
                trackWidth,
                trackHeight,
                BlendStageSummaryColor(canvas.Theme.SurfaceMuted, canvas.Theme.Canvas, .12),
                null,
                trackHeight / 2d,
                $"{point.Label} category track");
            canvas.AddRoundedRect(
                x + trackXOffset,
                y + .47,
                Math.Max(.14, trackWidth * point.Count / maximum),
                trackHeight,
                rowAccent,
                null,
                trackHeight / 2d,
                $"{point.Label} category bar");
        }
    }

    private sealed record ExecutivePortfolioStatus(
        string Label,
        int Count,
        string? Share,
        string Accent);
}
