using System.Globalization;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private const double StageSummaryBandX = .45;
    private const double StageSummaryBandY = 1.24;
    private const double StageSummaryBandWidth = 12.43;
    private const double StageSummaryBandHeight = .86;

    private static void RenderStageWiseExecutiveSummary(
        SlideCanvas canvas,
        ProjectBriefingPresentationData data)
    {
        AddSlideTitle(canvas, "Stage-wise summary");

        if (data.Summary.ProjectCount <= 0)
        {
            AddEmptyMessage(canvas, "No stage data is available for the selected projects.");
            return;
        }

        RenderPortfolioStatusBand(canvas, data.Summary);

        var points = NormaliseOngoingStageSummary(data.Summary);
        if (data.Summary.OngoingCount <= 0)
        {
            RenderNoOngoingProjects(canvas, data.Summary);
            return;
        }

        if (points.Count <= 6)
        {
            RenderOngoingStageColumns(canvas, data.Summary, points);
            return;
        }

        if (points.Count <= 10)
        {
            RenderOngoingStageCardGrid(canvas, data.Summary, points);
            return;
        }

        RenderOngoingStageCompactBars(canvas, data.Summary, points);
    }

    private static IReadOnlyList<ProjectBriefingSummaryPoint> NormaliseOngoingStageSummary(
        ProjectBriefingPresentationSummary summary)
    {
        var points = summary.OngoingStageSummary
            .Where(point => point.Count > 0)
            .OrderBy(point => point.Order)
            .ThenBy(point => point.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var represented = points.Sum(point => point.Count);
        if (represented < summary.OngoingCount)
        {
            points.Add(new ProjectBriefingSummaryPoint(
                "Stage unresolved",
                summary.OngoingCount - represented,
                ProjectBriefingStageOrder.Unknown));
        }

        return points;
    }

    private static void RenderPortfolioStatusBand(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary)
    {
        var statuses = new List<StageSummaryStatus>
        {
            new(
                "Completed",
                summary.CompletedCount,
                canvas.Theme.Positive,
                "✓"),
            new(
                "Ongoing",
                summary.OngoingCount,
                canvas.Theme.Accent,
                "↻")
        };

        if (summary.CancelledCount > 0)
        {
            statuses.Add(new StageSummaryStatus(
                "Cancelled",
                summary.CancelledCount,
                canvas.Theme.Critical,
                "×"));
        }

        var gap = statuses.Count == 2 ? .02 : .04;
        var panelWidth = (StageSummaryBandWidth - (gap * (statuses.Count - 1))) / statuses.Count;

        for (var index = 0; index < statuses.Count; index++)
        {
            var x = StageSummaryBandX + (index * (panelWidth + gap));
            RenderPortfolioStatusPanel(
                canvas,
                statuses[index],
                summary.ProjectCount,
                x,
                StageSummaryBandY,
                panelWidth,
                StageSummaryBandHeight,
                compact: statuses.Count > 2);
        }
    }

    private static void RenderPortfolioStatusPanel(
        SlideCanvas canvas,
        StageSummaryStatus status,
        int projectCount,
        double x,
        double y,
        double width,
        double height,
        bool compact)
    {
        canvas.AddRoundedRect(x, y, width, height, status.Fill, status.Fill, .06, $"{status.Label} status panel");

        var iconSize = compact ? .42 : .50;
        var iconX = x + (compact ? .24 : .40);
        var iconY = y + ((height - iconSize) / 2d);
        canvas.AddEllipse(iconX, iconY, iconSize, iconSize, status.Fill, canvas.Theme.TextOnAccent, 1.1, $"{status.Label} icon outline");
        canvas.AddText(
            iconX,
            iconY - .01,
            iconSize,
            iconSize,
            status.Symbol,
            compact ? 18.0 : 22.0,
            canvas.Theme.TextOnAccent,
            true,
            "c",
            name: $"{status.Label} icon");

        var labelX = iconX + iconSize + (compact ? .14 : .22);
        var dividerX = x + (width * (compact ? .58 : .61));
        var labelWidth = Math.Max(.78, dividerX - labelX - .12);
        canvas.AddText(
            labelX,
            y + .08,
            labelWidth,
            height - .16,
            status.Label,
            compact ? 14.8 : 19.0,
            canvas.Theme.TextOnAccent,
            true,
            "c",
            name: $"{status.Label} label");

        canvas.AddLine(
            dividerX,
            y + .18,
            dividerX,
            y + height - .18,
            canvas.Theme.TextOnAccent,
            .8);

        var percentage = FormatPercentage(status.Count, projectCount);
        canvas.AddText(
            dividerX + .10,
            y + .08,
            x + width - dividerX - .20,
            height - .16,
            $"{status.Count.ToString(CultureInfo.InvariantCulture)} ({percentage})",
            compact ? 14.6 : 18.6,
            canvas.Theme.TextOnAccent,
            true,
            "c",
            name: $"{status.Label} count and share");
    }

    private static void RenderOngoingStageColumns(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        const double chartX = .45;
        const double chartWidth = 8.72;
        const double calloutX = 9.47;
        const double calloutWidth = 3.40;
        const double sectionHeadingY = 2.34;
        const double badgeY = 2.80;
        const double badgeSize = .48;
        const double labelY = 3.34;
        const double labelHeight = .76;
        const double baselineY = 6.50;
        const double maximumColumnHeight = 1.58;

        canvas.AddText(
            chartX,
            sectionHeadingY,
            chartWidth,
            .34,
            "Breakdown of ongoing projects by stage",
            15.2,
            canvas.Theme.TextPrimary,
            true,
            "c",
            name: "Ongoing stage breakdown heading");

        var maximum = Math.Max(1, points.Max(point => point.Count));
        var cellWidth = chartWidth / points.Count;
        var labelFont = points.Count <= 5 ? 11.3 : 10.2;
        var badgeFont = points.Count <= 5 ? 10.6 : 9.6;
        var barWidth = Math.Clamp(cellWidth * .38, .50, .72);

        canvas.AddLine(chartX + .02, baselineY, chartX + chartWidth - .02, baselineY, canvas.Theme.TextSecondary, .75);

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var cellX = chartX + (index * cellWidth);
            var centreX = cellX + (cellWidth / 2d);
            var isDevelopment = point.Order == ProjectBriefingStageOrder.Development;
            var accent = isDevelopment ? canvas.Theme.SecondaryAccent : canvas.Theme.Accent;
            var soft = isDevelopment ? canvas.Theme.SecondaryAccentSoft : canvas.Theme.AccentSoft;

            if (index > 0)
            {
                canvas.AddLine(cellX, badgeY + .06, cellX, baselineY, canvas.Theme.Divider, .35);
            }

            canvas.AddEllipse(
                centreX - (badgeSize / 2d),
                badgeY,
                badgeSize,
                badgeSize,
                soft,
                null,
                name: $"{point.Label} stage badge");
            canvas.AddText(
                centreX - (badgeSize / 2d),
                badgeY,
                badgeSize,
                badgeSize,
                ResolveStageBadge(point),
                badgeFont,
                accent,
                true,
                "c",
                name: $"{point.Label} stage badge text");

            canvas.AddText(
                cellX + .05,
                labelY,
                cellWidth - .10,
                labelHeight,
                $"{index + 1}. {point.Label}",
                labelFont,
                canvas.Theme.TextPrimary,
                true,
                "c",
                verticalAnchor: "t",
                name: $"{point.Label} stage label");

            var columnHeight = Math.Max(.30, maximumColumnHeight * point.Count / maximum);
            var barX = centreX - (barWidth / 2d);
            var barY = baselineY - columnHeight;
            canvas.AddRect(
                barX,
                barY,
                barWidth,
                columnHeight,
                accent,
                accent,
                .5,
                $"{point.Label} stage column");
            canvas.AddText(
                centreX - .34,
                barY - .34,
                .68,
                .30,
                point.Count.ToString(CultureInfo.InvariantCulture),
                14.4,
                accent,
                true,
                "c",
                name: $"{point.Label} stage count");
        }

        RenderKeyTakeawayCard(
            canvas,
            summary,
            points,
            calloutX,
            3.08,
            calloutWidth,
            2.56);
    }

    private static void RenderOngoingStageCardGrid(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        canvas.AddText(
            .55,
            2.28,
            12.23,
            .32,
            "Breakdown of ongoing projects by stage",
            14.8,
            canvas.Theme.TextPrimary,
            true,
            "c",
            name: "Ongoing stage breakdown heading");

        const int columns = 5;
        const double gridX = .55;
        const double gridWidth = 12.23;
        const double gap = .16;
        const double cardHeight = 1.46;
        var cardWidth = (gridWidth - (gap * (columns - 1))) / columns;
        var maximum = Math.Max(1, points.Max(point => point.Count));

        for (var index = 0; index < points.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var x = gridX + (column * (cardWidth + gap));
            var y = 2.76 + (row * 1.64);
            var point = points[index];
            var isDevelopment = point.Order == ProjectBriefingStageOrder.Development;
            var accent = isDevelopment ? canvas.Theme.SecondaryAccent : canvas.Theme.Accent;
            var soft = isDevelopment ? canvas.Theme.SecondaryAccentSoft : canvas.Theme.AccentSoft;

            canvas.AddSubtleRoundedRect(x, y, cardWidth, cardHeight, canvas.Theme.Surface, canvas.Theme.Border, $"{point.Label} compact stage card");
            canvas.AddEllipse(x + .12, y + .12, .38, .38, soft, null, name: $"{point.Label} compact stage badge");
            canvas.AddText(x + .12, y + .12, .38, .38, ResolveStageBadge(point), 8.8, accent, true, "c");
            canvas.AddText(x + .57, y + .10, cardWidth - .69, .44, point.Label, 9.5, canvas.Theme.TextPrimary, true, "l", verticalAnchor: "t");
            canvas.AddText(x + .14, y + .58, .48, .34, point.Count.ToString(CultureInfo.InvariantCulture), 16.2, accent, true, "c");
            canvas.AddRoundedRect(x + .70, y + .67, cardWidth - .86, .18, canvas.Theme.SurfaceMuted, canvas.Theme.SurfaceMuted, .03);
            canvas.AddRoundedRect(
                x + .70,
                y + .67,
                Math.Max(.12, (cardWidth - .86) * point.Count / maximum),
                .18,
                accent,
                accent,
                .03,
                $"{point.Label} compact stage bar");
            canvas.AddText(
                x + .14,
                y + 1.04,
                cardWidth - .28,
                .24,
                $"{FormatPercentage(point.Count, Math.Max(1, summary.OngoingCount))} of ongoing",
                8.5,
                canvas.Theme.TextMuted,
                false,
                "l");
        }

        RenderInsightStrip(canvas, BuildStageTakeaway(summary, points));
    }

    private static void RenderOngoingStageCompactBars(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        canvas.AddText(
            .55,
            2.27,
            12.23,
            .32,
            "Breakdown of ongoing projects by stage",
            14.6,
            canvas.Theme.TextPrimary,
            true,
            "c",
            name: "Ongoing stage breakdown heading");

        var leftCount = (points.Count + 1) / 2;
        var columns = new[]
        {
            points.Take(leftCount).ToArray(),
            points.Skip(leftCount).ToArray()
        };
        var maximum = Math.Max(1, points.Max(point => point.Count));

        for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            var columnPoints = columns[columnIndex];
            var x = columnIndex == 0 ? .60 : 6.78;
            var rowHeight = Math.Min(.50, 3.30 / Math.Max(1, columnPoints.Length));

            for (var index = 0; index < columnPoints.Length; index++)
            {
                var point = columnPoints[index];
                var y = 2.78 + (index * rowHeight);
                var isDevelopment = point.Order == ProjectBriefingStageOrder.Development;
                var accent = isDevelopment ? canvas.Theme.SecondaryAccent : canvas.Theme.Accent;
                canvas.AddText(x, y, 2.23, rowHeight, Truncate(point.Label, 32), 9.5, canvas.Theme.TextPrimary, true, "l");
                canvas.AddRoundedRect(x + 2.30, y + ((rowHeight - .18) / 2d), 2.92, .18, canvas.Theme.SurfaceMuted, canvas.Theme.SurfaceMuted, .03);
                canvas.AddRoundedRect(
                    x + 2.30,
                    y + ((rowHeight - .18) / 2d),
                    Math.Max(.12, 2.92 * point.Count / maximum),
                    .18,
                    accent,
                    accent,
                    .03,
                    $"{point.Label} compact stage bar");
                canvas.AddText(x + 5.28, y, .32, rowHeight, point.Count.ToString(CultureInfo.InvariantCulture), 10.4, canvas.Theme.TextPrimary, true, "r");
            }
        }

        RenderInsightStrip(canvas, BuildStageTakeaway(summary, points));
    }

    private static void RenderNoOngoingProjects(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary)
    {
        canvas.AddText(
            .65,
            2.55,
            12.03,
            .38,
            "Breakdown of ongoing projects by stage",
            15.2,
            canvas.Theme.TextPrimary,
            true,
            "c");
        canvas.AddSubtleRoundedRect(2.12, 3.20, 9.10, 2.10, canvas.Theme.PositiveSoft, canvas.Theme.Positive, "All projects completed message panel");
        canvas.AddEllipse(3.00, 3.78, .72, .72, canvas.Theme.Positive, null, name: "All projects completed icon");
        canvas.AddText(3.00, 3.77, .72, .72, "✓", 28, canvas.Theme.TextOnAccent, true, "c");
        canvas.AddText(
            4.02,
            3.56,
            6.30,
            .54,
            summary.CompletedCount == summary.ProjectCount
                ? "All selected projects are completed"
                : "No selected project is currently ongoing",
            20.0,
            canvas.Theme.Positive,
            true,
            "l");
        canvas.AddText(
            4.02,
            4.18,
            6.30,
            .42,
            BuildStageTakeaway(summary, Array.Empty<ProjectBriefingSummaryPoint>()),
            12.5,
            canvas.Theme.TextPrimary,
            false,
            "l");
    }

    private static void RenderKeyTakeawayCard(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points,
        double x,
        double y,
        double width,
        double height)
    {
        canvas.AddSubtleRoundedRect(x, y, width, height, canvas.Theme.PositiveSoft, canvas.Theme.Positive, "Stage summary key takeaway panel");
        canvas.AddEllipse(x + .28, y + .50, .64, .64, canvas.Theme.PositiveSoft, canvas.Theme.Positive, 1.0, "Stage summary insight icon outline");
        canvas.AddText(x + .28, y + .49, .64, .64, "i", 22.0, canvas.Theme.Positive, true, "c", name: "Stage summary insight icon");
        canvas.AddText(x + 1.05, y + .30, width - 1.28, .34, "Key takeaway:", 13.7, canvas.Theme.Positive, true, "l");
        canvas.AddText(
            x + 1.05,
            y + .70,
            width - 1.28,
            height - .92,
            BuildStageTakeaway(summary, points),
            12.3,
            canvas.Theme.TextPrimary,
            false,
            "l",
            verticalAnchor: "t",
            name: "Stage summary key takeaway");
    }

    private static void RenderInsightStrip(SlideCanvas canvas, string message)
    {
        canvas.AddSubtleRoundedRect(2.05, 6.36, 9.23, .42, canvas.Theme.AccentSoft, canvas.Theme.Accent, "Stage summary insight strip");
        canvas.AddEllipse(2.25, 6.43, .27, .27, canvas.Theme.Accent, null, name: "Stage summary insight strip icon");
        canvas.AddText(2.25, 6.425, .27, .27, "i", 9.4, canvas.Theme.TextOnAccent, true, "c");
        canvas.AddText(2.63, 6.38, 8.40, .36, message, 10.6, canvas.Theme.TextPrimary, true, "c");
    }

    private static string BuildStageTakeaway(
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        if (summary.ProjectCount <= 0)
        {
            return "No selected projects are available for stage analysis.";
        }

        if (summary.OngoingCount <= 0)
        {
            return summary.CompletedCount == summary.ProjectCount
                ? "All selected projects are completed."
                : "No selected project is currently ongoing.";
        }

        var completedShare = FormatPercentage(summary.CompletedCount, summary.ProjectCount);
        if (points.Count == 0)
        {
            return $"{completedShare} of selected projects are completed; the ongoing stage position is not yet resolved.";
        }

        var maximum = points.Max(point => point.Count);
        var leaders = points.Where(point => point.Count == maximum).ToArray();
        var lifecyclePrefix = summary.CancelledCount > 0
            ? $"{completedShare} completed, {FormatPercentage(summary.OngoingCount, summary.ProjectCount)} ongoing and {FormatPercentage(summary.CancelledCount, summary.ProjectCount)} cancelled; "
            : $"{completedShare} of selected projects are completed; ";

        if (leaders.Length != 1)
        {
            return lifecyclePrefix + "no single ongoing stage currently dominates the portfolio.";
        }

        return lifecyclePrefix
            + $"among ongoing cases, {ResolveTakeawayStageName(leaders[0])} is the principal concentration.";
    }

    private static string ResolveStageBadge(ProjectBriefingSummaryPoint point)
        => point.Order switch
        {
            ProjectBriefingStageOrder.Development => "DEV",
            ProjectBriefingStageOrder.TechnicalEvaluation => "TEC",
            ProjectBriefingStageOrder.BiddingTendering => "BID",
            ProjectBriefingStageOrder.AcceptanceOfNecessity => "AoN",
            ProjectBriefingStageOrder.SowVetting => "SoW",
            ProjectBriefingStageOrder.InPrincipleApproval => "IPA",
            ProjectBriefingStageOrder.FeasibilityStudy => "FS",
            ProjectBriefingStageOrder.Benchmarking => "BM",
            ProjectBriefingStageOrder.CommercialBidOpening => "COB",
            ProjectBriefingStageOrder.Pnc => "PNC",
            ProjectBriefingStageOrder.EasApproval => "EAS",
            ProjectBriefingStageOrder.SupplyOrder => "SO",
            ProjectBriefingStageOrder.AcceptanceTesting => "ATP",
            ProjectBriefingStageOrder.Payment => "PAY",
            ProjectBriefingStageOrder.TransferOfTechnology => "ToT",
            _ => "—"
        };

    private static string ResolveTakeawayStageName(ProjectBriefingSummaryPoint point)
        => point.Order switch
        {
            ProjectBriefingStageOrder.AcceptanceOfNecessity => "AoN",
            ProjectBriefingStageOrder.InPrincipleApproval => "IPA",
            ProjectBriefingStageOrder.TechnicalEvaluation => "TEC",
            ProjectBriefingStageOrder.BiddingTendering => "bidding / tendering",
            ProjectBriefingStageOrder.SowVetting => "SoW vetting",
            ProjectBriefingStageOrder.FeasibilityStudy => "feasibility study",
            ProjectBriefingStageOrder.SupplyOrder => "supply order",
            ProjectBriefingStageOrder.AcceptanceTesting => "acceptance testing",
            ProjectBriefingStageOrder.TransferOfTechnology => "ToT",
            _ => point.Label
        };

    private static string FormatPercentage(int count, int total)
        => total <= 0
            ? "0%"
            : (count * 100d / total).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private sealed record StageSummaryStatus(
        string Label,
        int Count,
        string Fill,
        string Symbol);
}
