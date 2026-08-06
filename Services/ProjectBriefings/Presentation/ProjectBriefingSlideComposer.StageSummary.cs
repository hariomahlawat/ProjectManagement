using System.Globalization;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private const double StageSummaryBandX = .45;
    private const double StageSummaryBandY = 1.31;
    private const double StageSummaryBandWidth = 12.43;
    private const double StageSummaryBandHeight = .72;

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

        RenderSelectedProjectEyebrow(canvas, data.Summary.ProjectCount);
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

    private static void RenderSelectedProjectEyebrow(SlideCanvas canvas, int projectCount)
    {
        canvas.AddText(
            4.65,
            1.075,
            4.03,
            .18,
            $"{projectCount.ToString(CultureInfo.InvariantCulture)} SELECTED PROJECTS",
            10.8,
            canvas.Theme.TextSecondary,
            true,
            "c",
            name: "Stage summary selected project total");
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

        var iconSize = compact ? .36 : .42;
        var iconX = x + (compact ? .21 : .38);
        var iconY = y + ((height - iconSize) / 2d);
        canvas.AddEllipse(iconX, iconY, iconSize, iconSize, status.Fill, canvas.Theme.TextOnAccent, 1.0, $"{status.Label} icon outline");
        canvas.AddText(
            iconX,
            iconY - .005,
            iconSize,
            iconSize,
            status.Symbol,
            compact ? 15.6 : 18.8,
            canvas.Theme.TextOnAccent,
            true,
            "c",
            name: $"{status.Label} icon");

        var labelX = iconX + iconSize + (compact ? .11 : .22);
        var dividerX = x + (width * (compact ? .57 : .61));
        var labelWidth = Math.Max(.72, dividerX - labelX - .11);
        canvas.AddText(
            labelX,
            y + .06,
            labelWidth,
            height - .12,
            status.Label,
            compact ? 13.6 : 17.3,
            canvas.Theme.TextOnAccent,
            true,
            "c",
            name: $"{status.Label} label");

        canvas.AddLine(
            dividerX,
            y + .14,
            dividerX,
            y + height - .14,
            canvas.Theme.TextOnAccent,
            .75);

        var percentage = FormatPercentage(status.Count, projectCount);
        canvas.AddRichTextBox(
            dividerX + .08,
            y + .05,
            x + width - dividerX - .16,
            height - .10,
            new[]
            {
                new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(
                            status.Count.ToString(CultureInfo.InvariantCulture),
                            compact ? 15.8 : 19.2,
                            canvas.Theme.TextOnAccent,
                            Bold: true),
                        new RichTextRun(
                            $" ({percentage})",
                            compact ? 11.0 : 13.2,
                            canvas.Theme.TextOnAccent,
                            Bold: true)
                    },
                    Align: "ctr")
            },
            $"{status.Label} count and share",
            verticalAnchor: "ctr",
            allowAutoFit: false,
            leftInset: 0,
            rightInset: 0,
            topInset: 0,
            bottomInset: 0);
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
        const double sectionHeadingY = 2.31;
        const double badgeY = 2.67;
        const double badgeSize = .46;
        const double sequenceY = 3.16;
        const double labelY = 3.36;
        const double labelHeight = .52;
        const double trackTop = 4.12;
        const double baselineY = 6.08;
        const double trackHeight = baselineY - trackTop;

        canvas.AddText(
            chartX + .05,
            sectionHeadingY,
            chartWidth - .10,
            .24,
            "ONGOING PROJECTS BY PRESENT STAGE",
            11.8,
            canvas.Theme.TextSecondary,
            true,
            "l",
            name: "Ongoing stage breakdown heading");

        var maximum = Math.Max(1, points.Max(point => point.Count));
        var cellWidth = chartWidth / points.Count;
        var labelFont = points.Count <= 5 ? 10.7 : 9.7;
        var badgeFont = points.Count <= 5 ? 10.0 : 9.2;
        var trackWidth = Math.Clamp(cellWidth * .42, .62, .78);

        canvas.AddLine(chartX + .02, baselineY, chartX + chartWidth - .02, baselineY, canvas.Theme.TextSecondary, .70);

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var cellX = chartX + (index * cellWidth);
            var centreX = cellX + (cellWidth / 2d);
            var isLeader = point.Count == maximum;
            var accent = ResolveOngoingStageAccent(canvas, isLeader);

            canvas.AddEllipse(
                centreX - (badgeSize / 2d),
                badgeY,
                badgeSize,
                badgeSize,
                canvas.Theme.AccentSoft,
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
                centreX - .24,
                sequenceY,
                .48,
                .18,
                (index + 1).ToString("00", CultureInfo.InvariantCulture),
                8.3,
                canvas.Theme.TextMuted,
                true,
                "c",
                name: $"{point.Label} stage sequence");

            canvas.AddText(
                cellX + .05,
                labelY,
                cellWidth - .10,
                labelHeight,
                point.Label,
                labelFont,
                canvas.Theme.TextPrimary,
                true,
                "c",
                verticalAnchor: "t",
                name: $"{point.Label} stage label");

            var trackX = centreX - (trackWidth / 2d);
            canvas.AddRoundedRect(
                trackX,
                trackTop,
                trackWidth,
                trackHeight,
                canvas.Theme.SurfaceMuted,
                null,
                .30,
                $"{point.Label} stage track");

            var columnHeight = Math.Max(.30, trackHeight * point.Count / maximum);
            var barY = baselineY - columnHeight;
            canvas.AddRoundedRect(
                trackX,
                barY,
                trackWidth,
                columnHeight,
                accent,
                accent,
                .30,
                $"{point.Label} stage column");
            canvas.AddText(
                centreX - .34,
                Math.Max(trackTop - .27, barY - .28),
                .68,
                .24,
                point.Count.ToString(CultureInfo.InvariantCulture),
                13.4,
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
            3.83,
            calloutWidth,
            1.58);
    }

    private static void RenderOngoingStageCardGrid(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        canvas.AddText(
            .55,
            2.24,
            12.23,
            .32,
            "ONGOING PROJECTS BY PRESENT STAGE",
            11.8,
            canvas.Theme.TextSecondary,
            true,
            "c",
            name: "Ongoing stage breakdown heading");

        const double gridX = .55;
        const double gridWidth = 12.23;
        const double gap = .16;
        const double cardHeight = 1.46;
        const double rowGap = .17;
        const double firstRowY = 2.72;

        var columns = points.Count <= 8 ? 4 : 5;
        var cardWidth = (gridWidth - (gap * (columns - 1))) / columns;
        var maximum = Math.Max(1, points.Max(point => point.Count));
        var leaders = points.Where(point => point.Count == maximum).ToArray();
        var uniqueLeader = leaders.Length == 1 ? leaders[0] : null;
        var rowCounts = BuildBalancedStageCardRows(points.Count, columns);

        var pointIndex = 0;
        for (var row = 0; row < rowCounts.Count; row++)
        {
            var rowCount = rowCounts[row];
            var rowWidth = (rowCount * cardWidth) + ((rowCount - 1) * gap);
            var rowX = gridX + ((gridWidth - rowWidth) / 2d);
            var y = firstRowY + (row * (cardHeight + rowGap));

            for (var column = 0; column < rowCount; column++)
            {
                var point = points[pointIndex++];
                var x = rowX + (column * (cardWidth + gap));
                var isLeader = uniqueLeader is not null && point.Equals(uniqueLeader);
                var accent = ResolveOngoingStageAccent(canvas, isLeader);
                var cardFill = ResolveStageSummaryCardFill(canvas, isLeader);
                var cardBorder = ResolveStageSummaryCardBorder(canvas, isLeader);
                var badgeFill = ResolveStageSummaryBadgeFill(canvas, accent, isLeader);
                var badgeText = canvas.Theme.IsDark || isLeader
                    ? canvas.Theme.TextOnAccent
                    : accent;
                var trackFill = ResolveStageSummaryCardTrack(canvas, cardFill);
                var stageNameFont = columns == 4 ? 11.4 : 10.8;
                var countFont = columns == 4 ? 20.2 : 19.0;

                canvas.AddSubtleRoundedRect(
                    x,
                    y,
                    cardWidth,
                    cardHeight,
                    cardFill,
                    cardBorder,
                    $"{point.Label} compact stage card");

                if (isLeader)
                {
                    canvas.AddRoundedRect(
                        x + .02,
                        y + .08,
                        .04,
                        cardHeight - .16,
                        canvas.Theme.Accent,
                        null,
                        .02,
                        $"{point.Label} leading stage marker");
                }

                canvas.AddEllipse(
                    x + .12,
                    y + .12,
                    .40,
                    .40,
                    badgeFill,
                    null,
                    name: $"{point.Label} compact stage badge");
                canvas.AddText(
                    x + .12,
                    y + .12,
                    .40,
                    .40,
                    ResolveStageBadge(point),
                    9.6,
                    badgeText,
                    true,
                    "c",
                    name: $"{point.Label} compact stage badge text");

                canvas.AddText(
                    x + .60,
                    y + .08,
                    cardWidth - .72,
                    .47,
                    point.Label,
                    stageNameFont,
                    canvas.Theme.TextPrimary,
                    true,
                    "l",
                    verticalAnchor: "t",
                    name: $"{point.Label} compact stage label");

                canvas.AddText(
                    x + .14,
                    y + .57,
                    .50,
                    .36,
                    point.Count.ToString(CultureInfo.InvariantCulture),
                    countFont,
                    accent,
                    true,
                    "c",
                    name: $"{point.Label} compact stage count");

                var barX = x + .72;
                var barY = y + .68;
                var barWidth = cardWidth - .88;
                canvas.AddRoundedRect(
                    barX,
                    barY,
                    barWidth,
                    .18,
                    trackFill,
                    null,
                    .03,
                    $"{point.Label} compact stage track");
                canvas.AddRoundedRect(
                    barX,
                    barY,
                    Math.Max(.12, barWidth * point.Count / maximum),
                    .18,
                    accent,
                    accent,
                    .03,
                    $"{point.Label} compact stage bar");

                canvas.AddText(
                    x + .14,
                    y + 1.05,
                    cardWidth - .28,
                    .24,
                    $"{FormatPercentage(point.Count, Math.Max(1, summary.OngoingCount))} of ongoing",
                    9.0,
                    canvas.Theme.TextMuted,
                    false,
                    "l",
                    name: $"{point.Label} compact stage share");
            }
        }

        RenderInsightStrip(canvas, BuildDenseStageTakeaway(summary, points));
    }

    private static IReadOnlyList<int> BuildBalancedStageCardRows(
        int pointCount,
        int maximumColumns)
    {
        if (pointCount <= maximumColumns)
        {
            return new[] { pointCount };
        }

        return new[]
        {
            maximumColumns,
            pointCount - maximumColumns
        };
    }

    private static string ResolveStageSummaryCardFill(
        SlideCanvas canvas,
        bool isLeader)
    {
        if (isLeader)
        {
            return canvas.Theme.IsDark
                ? BlendStageSummaryColor(canvas.Theme.AccentSoft, canvas.Theme.Surface, .26)
                : BlendStageSummaryColor(canvas.Theme.AccentSoft, canvas.Theme.Surface, .18);
        }

        return canvas.Theme.IsDark
            ? BlendStageSummaryColor(canvas.Theme.Surface, canvas.Theme.Canvas, .18)
            : BlendStageSummaryColor(canvas.Theme.Surface, canvas.Theme.Canvas, .08);
    }

    private static string ResolveStageSummaryCardBorder(
        SlideCanvas canvas,
        bool isLeader)
        => isLeader
            ? canvas.Theme.Accent
            : BlendStageSummaryColor(
                canvas.Theme.Border,
                canvas.Theme.Canvas,
                canvas.Theme.IsDark ? .48 : .62);

    private static string ResolveStageSummaryBadgeFill(
        SlideCanvas canvas,
        string accent,
        bool isLeader)
        => canvas.Theme.IsDark || isLeader
            ? accent
            : canvas.Theme.AccentSoft;

    private static string ResolveStageSummaryCardTrack(
        SlideCanvas canvas,
        string cardFill)
        => BlendStageSummaryColor(
            canvas.Theme.SurfaceMuted,
            cardFill,
            canvas.Theme.IsDark ? .42 : .34);

    private static void RenderOngoingStageCompactBars(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        canvas.AddText(
            .55,
            2.23,
            12.23,
            .32,
            "ONGOING PROJECTS BY PRESENT STAGE",
            11.8,
            canvas.Theme.TextSecondary,
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
                var y = 2.74 + (index * rowHeight);
                var accent = ResolveOngoingStageAccent(canvas, point.Count == maximum);
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

        RenderInsightStrip(canvas, BuildDenseStageTakeaway(summary, points));
    }

    private static void RenderNoOngoingProjects(
        SlideCanvas canvas,
        ProjectBriefingPresentationSummary summary)
    {
        canvas.AddText(
            .65,
            2.45,
            12.03,
            .38,
            "ONGOING PROJECTS BY PRESENT STAGE",
            11.8,
            canvas.Theme.TextSecondary,
            true,
            "c");
        canvas.AddSubtleRoundedRect(2.12, 3.12, 9.10, 2.10, canvas.Theme.PositiveSoft, canvas.Theme.Positive, "All projects completed message panel");
        canvas.AddEllipse(3.00, 3.70, .72, .72, canvas.Theme.Positive, null, name: "All projects completed icon");
        canvas.AddText(3.00, 3.69, .72, .72, "✓", 28, canvas.Theme.TextOnAccent, true, "c");
        canvas.AddText(
            4.02,
            3.48,
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
            4.10,
            6.30,
            .42,
            BuildStageInsight(summary, Array.Empty<ProjectBriefingSummaryPoint>()).Body,
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
        var insight = BuildStageInsight(summary, points);
        canvas.AddSubtleRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.AccentSoft,
            canvas.Theme.Accent,
            "Stage summary key takeaway panel");
        canvas.AddText(
            x + .30,
            y + .18,
            width - .60,
            .18,
            "KEY TAKEAWAY",
            8.9,
            canvas.Theme.Accent,
            true,
            "l",
            name: "Stage summary key takeaway eyebrow");
        canvas.AddText(
            x + .28,
            y + .47,
            width - .56,
            .42,
            insight.Headline,
            22.6,
            canvas.Theme.Accent,
            true,
            "l",
            name: "Stage summary key takeaway metric");

        if (!string.IsNullOrWhiteSpace(insight.Emphasis))
        {
            canvas.AddRichTextBox(
                x + .28,
                y + 1.00,
                width - .56,
                Math.Max(.34, height - 1.12),
                new[]
                {
                    new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                insight.Body,
                                10.8,
                                canvas.Theme.TextPrimary)
                        },
                        Align: "l",
                        SpaceAfterPoints: 1.5,
                        LineSpacingPoints: 12.6),
                    new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(
                                insight.Emphasis!,
                                11.5,
                                canvas.Theme.Accent,
                                Bold: true)
                        },
                        Align: "l",
                        LineSpacingPoints: 13.2)
                },
                "Stage summary key takeaway body",
                verticalAnchor: "t",
                allowAutoFit: false,
                leftInset: 0,
                rightInset: 0,
                topInset: 0,
                bottomInset: 0);
        }
        else
        {
            canvas.AddText(
                x + .28,
                y + 1.00,
                width - .56,
                Math.Max(.34, height - 1.12),
                insight.Body,
                10.8,
                canvas.Theme.TextPrimary,
                false,
                "l",
                verticalAnchor: "t",
                name: "Stage summary key takeaway body");
        }
    }

    private static void RenderInsightStrip(SlideCanvas canvas, string message)
    {
        canvas.AddSubtleRoundedRect(
            2.05,
            6.36,
            9.23,
            .42,
            canvas.Theme.AccentSoft,
            canvas.Theme.Accent,
            "Stage summary insight strip");
        canvas.AddEllipse(
            2.25,
            6.43,
            .27,
            .27,
            canvas.Theme.Accent,
            null,
            name: "Stage summary insight strip icon");
        canvas.AddText(
            2.25,
            6.425,
            .27,
            .27,
            "i",
            9.4,
            canvas.Theme.TextOnAccent,
            true,
            "c",
            name: "Stage summary insight strip icon text");
        canvas.AddText(
            2.64,
            6.38,
            8.33,
            .36,
            message,
            10.6,
            canvas.Theme.TextPrimary,
            true,
            "l",
            name: "Stage summary insight strip message");
    }

    private static StageSummaryInsight BuildStageInsight(
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        if (summary.ProjectCount <= 0)
        {
            return new StageSummaryInsight(
                "No data",
                "No selected projects are available for stage analysis.");
        }

        if (summary.OngoingCount <= 0)
        {
            return new StageSummaryInsight(
                summary.CompletedCount == summary.ProjectCount ? "All completed" : "No ongoing cases",
                summary.CompletedCount == summary.ProjectCount
                    ? "All selected projects are completed."
                    : "No selected project is currently ongoing.");
        }

        if (points.Count == 0)
        {
            return new StageSummaryInsight(
                "Position unresolved",
                "The ongoing stage position has not yet been resolved.");
        }

        var maximum = points.Max(point => point.Count);
        var leaders = points.Where(point => point.Count == maximum).ToArray();
        if (leaders.Length != 1)
        {
            return new StageSummaryInsight(
                "No single leader",
                "The ongoing portfolio is distributed across multiple stages.");
        }

        var leader = leaders[0];
        var noun = summary.OngoingCount == 1 ? "project is" : "projects are";
        return new StageSummaryInsight(
            $"{leader.Count.ToString(CultureInfo.InvariantCulture)} of {summary.OngoingCount.ToString(CultureInfo.InvariantCulture)}",
            $"ongoing {noun} at the",
            $"{ResolveTakeawayStageName(leader)} stage");
    }

    private static string BuildDenseStageTakeaway(
        ProjectBriefingPresentationSummary summary,
        IReadOnlyList<ProjectBriefingSummaryPoint> points)
    {
        if (summary.OngoingCount <= 0)
        {
            return summary.CompletedCount == summary.ProjectCount
                ? "All selected projects are completed"
                : "No selected project is currently ongoing";
        }

        if (points.Count == 0)
        {
            return "The ongoing stage position has not yet been resolved";
        }

        var maximum = points.Max(point => point.Count);
        var leaders = points.Where(point => point.Count == maximum).ToArray();
        if (leaders.Length != 1)
        {
            return "No single ongoing stage dominates · portfolio distributed across multiple stages";
        }

        var leader = leaders[0];
        var stageName = CapitaliseStageSummaryLabel(ResolveTakeawayStageName(leader));
        var projectWord = summary.OngoingCount == 1 ? "project" : "projects";
        return $"{stageName} is the leading stage · " +
               $"{leader.Count.ToString(CultureInfo.InvariantCulture)} of " +
               $"{summary.OngoingCount.ToString(CultureInfo.InvariantCulture)} ongoing {projectWord} " +
               $"({FormatPercentage(leader.Count, summary.OngoingCount)})";
    }

    private static string CapitaliseStageSummaryLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string ResolveOngoingStageAccent(SlideCanvas canvas, bool isLeader)
        => isLeader
            ? canvas.Theme.Accent
            : BlendStageSummaryColor(canvas.Theme.Accent, canvas.Theme.Canvas, .18);

    private static string BlendStageSummaryColor(
        string foreground,
        string background,
        double backgroundWeight)
    {
        var foregroundHex = foreground.Trim().TrimStart('#');
        var backgroundHex = background.Trim().TrimStart('#');
        if (foregroundHex.Length != 6 || backgroundHex.Length != 6)
        {
            return foreground;
        }

        backgroundWeight = Math.Clamp(backgroundWeight, 0d, 1d);

        static int Channel(string value, int start)
            => int.Parse(value.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static int Mix(int foregroundValue, int backgroundValue, double backgroundRatio)
            => (int)Math.Round((foregroundValue * (1d - backgroundRatio)) + (backgroundValue * backgroundRatio));

        var red = Mix(Channel(foregroundHex, 0), Channel(backgroundHex, 0), backgroundWeight);
        var green = Mix(Channel(foregroundHex, 2), Channel(backgroundHex, 2), backgroundWeight);
        var blue = Mix(Channel(foregroundHex, 4), Channel(backgroundHex, 4), backgroundWeight);
        return $"{red:X2}{green:X2}{blue:X2}";
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

    private sealed record StageSummaryInsight(
        string Headline,
        string Body,
        string? Emphasis = null);
}
