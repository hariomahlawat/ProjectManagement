using System.Globalization;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics.Presentation;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

/// <summary>
/// The stage-wise distribution block (PR 7 of the blocks plan in docs/ux/briefing-decks.md), drawn like
/// Analytics › ARPP for the deck's own projects (table row only projects count). Slide 1: the stages grouped
/// as Pre-AoN, AoN to SO, Under development and Completed, each bar split by parent category. Slide 2 (and
/// continuations): every project in those four columns, the stage before the supply order, the PDC at
/// Development (red when overdue) and the completion month and year. Bands, PDCs and completion come from
/// <see cref="ProjectBriefingPresentationData.StageDistribution"/>; nothing is worked out here. The ARPP headline
/// cards (IPA, listed, delisted) are left out. Cancelled projects are counted nowhere and named in the subtitle
/// count only, as on the ARPP slides.
/// </summary>
public sealed partial class ProjectBriefingSlideComposer
{
    private const string StageDistributionTitle = "Where the projects stand";
    private const string StageDistributionListTitle = "The projects by where they stand";

    private static readonly (ArppProgressBucket Band, string Title)[] StageDistributionBands =
    {
        (ArppProgressBucket.PreAon, "Pre-AoN"),
        (ArppProgressBucket.AonToSupplyOrder, "AoN to SO"),
        (ArppProgressBucket.UnderDevelopment, "Under development"),
        (ArppProgressBucket.Completed, "Completed")
    };

    /// <summary>Adds the chart slide and the project list slides (as many as the longest column needs).</summary>
    private static void AddStageDistributionSlides(List<SlidePlan> plans, ProjectBriefingPresentationData data)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(data);

        var distribution = data.StageDistribution;
        var shown = distribution.Lines.Where(line => StageDistributionBands.Any(band => band.Band == line.Bucket)).ToList();
        var categories = StageDistributionCategories(distribution, shown);
        var subtitle = StageDistributionSubtitle(distribution, shown);

        plans.Add(new SlidePlan(
            SlidePlanKind.Summary,
            canvas => RenderStageDistributionChart(canvas, shown, categories, subtitle),
            ProjectBriefingSlideRole.StageDistribution,
            ListedProjectIds: shown.Select(line => line.ProjectId).ToArray()));

        var pages = PaginateStageDistributionColumns(shown);
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var isFirst = index == 0;
            var pageNumber = index + 1;
            var pageCount = pages.Count;
            plans.Add(new SlidePlan(
                SlidePlanKind.Summary,
                canvas => RenderStageDistributionList(canvas, page, categories, isFirst, pageNumber, pageCount),
                ProjectBriefingSlideRole.StageDistributionList,
                ListedProjectIds: page.SelectMany(column => column.Parts.SelectMany(part => part)).Select(line => line.ProjectId).ToArray()));
        }
    }

    /// <summary>Test entry point: the stage-wise distribution slides alone, composed and validated like a deck.</summary>
    internal ProjectBriefingComposition ComposeStageDistributionSlidesForTesting(ProjectBriefingPresentationData data)
    {
        var plans = new List<SlidePlan>();
        AddStageDistributionSlides(plans, data);
        return ComposePlans(data, plans);
    }

    private static IReadOnlyList<string> StageDistributionCategories(
        ProjectBriefingStageDistributionData distribution,
        IReadOnlyList<ArppProjectLineVm> shown)
    {
        var present = shown.Select(line => line.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ordered = distribution.CategoryOrder.Where(name => present.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        ordered.AddRange(present
            .Where(name => !ordered.Contains(name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        return ordered;
    }

    private static string StageDistributionSubtitle(
        ProjectBriefingStageDistributionData distribution,
        IReadOnlyList<ArppProjectLineVm> shown)
    {
        var subtitle = $"{Projects(shown.Count)} in this deck by stage and parent category";
        var cancelled = distribution.Lines.Count(line => line.Bucket == ArppProgressBucket.Cancelled);
        var notKnown = distribution.Lines.Count(line => line.Bucket == ArppProgressBucket.NotKnown);
        if (cancelled > 0)
        {
            subtitle += $" · {cancelled.ToString(CultureInfo.InvariantCulture)} cancelled, not shown";
        }
        if (notKnown > 0)
        {
            subtitle += $" · {notKnown.ToString(CultureInfo.InvariantCulture)} not known, not shown";
        }

        return subtitle + $" · as of {distribution.AsOf.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
    }

    private static string Projects(int count) =>
        count == 1 ? "1 project" : $"{count.ToString(CultureInfo.InvariantCulture)} projects";

    private static string StageDistributionCategoryColor(SlideCanvas canvas, string category) =>
        AnalyticsSlideFrame.CategoryColor(category, canvas.Theme.IsDark);

    private static string StageDistributionBandFill(SlideCanvas canvas, ArppProgressBucket band) => band switch
    {
        ArppProgressBucket.PreAon => canvas.Theme.SurfaceMuted,
        ArppProgressBucket.AonToSupplyOrder => canvas.Theme.Placeholder,
        ArppProgressBucket.UnderDevelopment => canvas.Theme.AccentSoft,
        _ => canvas.Theme.PositiveSoft
    };

    // SECTION: Slide 1, the chart
    private const double DistributionPlotLeft = 1.08;
    private const double DistributionPlotRight = 12.72;
    private const double DistributionPlotTop = 1.62;
    private const double DistributionPlotBottom = 5.62;

    private static void RenderStageDistributionChart(
        SlideCanvas canvas,
        IReadOnlyList<ArppProjectLineVm> lines,
        IReadOnlyList<string> categories,
        string subtitle)
    {
        AddSlideTitle(canvas, StageDistributionTitle, subtitle);
        if (lines.Count == 0)
        {
            AddEmptyMessage(canvas, "No project in this deck is ongoing or completed.");
            return;
        }

        // A slot per stage its projects stand at, band by band; Completed is one slot. Empty bands and stages
        // are left out, so every bar has projects and a band's bars add up to its count.
        var slots = new List<(ArppProgressBucket Band, string Label, List<ArppProjectLineVm> Lines)>();
        foreach (var (band, title) in StageDistributionBands)
        {
            var inBand = lines.Where(line => line.Bucket == band).ToList();
            if (inBand.Count == 0)
            {
                continue;
            }

            if (band == ArppProgressBucket.Completed)
            {
                slots.Add((band, title, inBand));
                continue;
            }

            slots.AddRange(inBand
                .GroupBy(line => line.StageCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(stage => stage.Min(line => line.StageRank < 0 ? int.MaxValue : line.StageRank))
                .ThenBy(stage => stage.Key, StringComparer.OrdinalIgnoreCase)
                .Select(stage => (band, StageDistributionStageLabel(stage.Key), stage.ToList())));
        }

        var top = AnalyticsSlideFrame.NiceAxisTop(slots.Max(slot => slot.Lines.Count));
        const double plotHeight = DistributionPlotBottom - DistributionPlotTop;
        var slotWidths = StageDistributionSlotWidths(slots.Select(slot => slot.Band).ToList());
        var slotLefts = new double[slots.Count];
        for (var index = 1; index < slots.Count; index++)
        {
            slotLefts[index] = slotLefts[index - 1] + slotWidths[index - 1];
        }
        var barWidth = Math.Min(.62, slotWidths.Min() * .58);

        // Bands first, behind the grid and bars: one shape each, its title and count at the foot.
        foreach (var (band, title) in StageDistributionBands)
        {
            var first = slots.FindIndex(item => item.Band == band);
            if (first < 0)
            {
                continue;
            }

            var last = slots.FindLastIndex(item => item.Band == band);
            var count = slots.Where(item => item.Band == band).Sum(item => item.Lines.Count);
            var width = slotLefts[last] + slotWidths[last] - slotLefts[first] - .06;
            canvas.AddTextShape(
                DistributionPlotLeft + slotLefts[first] + .03,
                DistributionPlotTop - .12,
                width,
                DistributionPlotBottom - DistributionPlotTop + .78,
                StageDistributionBandFill(canvas, band),
                null,
                0,
                "rect",
                new[]
                {
                    new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(title, BandTitleSize, canvas.Theme.TextPrimary, Bold: true),
                            new RichTextRun($" · {count.ToString(CultureInfo.InvariantCulture)}", BandTitleSize, canvas.Theme.TextSecondary, Bold: true)
                        },
                        Align: "ctr")
                },
                $"{title} band",
                verticalAnchor: "b",
                leftInset: .04,
                rightInset: .04,
                topInset: 0,
                bottomInset: .08);
        }

        var step = AnalyticsSlideFrame.AxisStep(top);
        for (var tick = 0; tick <= top; tick += step)
        {
            var y = DistributionPlotBottom - plotHeight * tick / top;
            canvas.AddLine(DistributionPlotLeft, y, DistributionPlotRight, y, tick == 0 ? canvas.Theme.Divider : canvas.Theme.Border, tick == 0 ? 1 : .5);
            canvas.AddText(DistributionPlotLeft - .5, y - .12, .42, .24, tick.ToString(CultureInfo.InvariantCulture), 9, canvas.Theme.TextMuted, false, "r", name: "Axis value");
        }

        // Counts inside the segments read on the category colour: white on the light theme's strong colours,
        // the canvas ink on the dark theme's lighter ones.
        var segmentText = canvas.Theme.IsDark ? canvas.Theme.Canvas : canvas.Theme.TextOnAccent;
        for (var index = 0; index < slots.Count; index++)
        {
            var (_, label, stageLines) = slots[index];
            var slot = slotWidths[index];
            var slotLeft = DistributionPlotLeft + slotLefts[index];
            var x = slotLeft + (slot - barWidth) / 2;
            var y = DistributionPlotBottom;
            foreach (var category in categories)
            {
                var count = stageLines.Count(line => string.Equals(line.Category, category, StringComparison.OrdinalIgnoreCase));
                if (count == 0)
                {
                    continue;
                }

                var height = plotHeight * count / top;
                y -= height;
                canvas.AddRect(x, y, barWidth, height, StageDistributionCategoryColor(canvas, category), name: $"{label} {category}");
                if (height >= .24)
                {
                    canvas.AddText(x, y, barWidth, height, count.ToString(CultureInfo.InvariantCulture), 10, segmentText, true, "ctr", name: $"{label} {category} count");
                }
            }

            canvas.AddText(x - .2, y - .28, barWidth + .4, .26, stageLines.Count.ToString(CultureInfo.InvariantCulture), 11, canvas.Theme.TextPrimary, true, "ctr", name: $"{label} total");
            canvas.AddText(slotLeft, DistributionPlotBottom + .05, slot, .26, label, slot < .75 ? 8.5 : 10, canvas.Theme.TextSecondary, false, "ctr", name: $"{label} stage");
        }

        AddStageDistributionLegend(canvas, categories, lines, 6.55);
    }

    private const double BandTitleSize = 11;

    /// <summary>
    /// Each stage gets an equal slot, except that a band too narrow for its title and count on one line (a
    /// one-stage Completed or Under development band) is widened to fit, and the other slots share the rest.
    /// </summary>
    private static double[] StageDistributionSlotWidths(IReadOnlyList<ArppProgressBucket> slotBands)
    {
        const double plotWidth = DistributionPlotRight - DistributionPlotLeft;
        var bands = slotBands.Distinct().ToList();
        var minimum = bands.ToDictionary(
            band => band,
            band => AnalyticsSlideFrame.TextWidth($"{StageDistributionBands.First(item => item.Band == band).Title} · 000", BandTitleSize) + .2);
        var fixedWidth = new Dictionary<ArppProgressBucket, double>();
        for (var pass = 0; pass < bands.Count; pass++)
        {
            var freeSlots = slotBands.Count(band => !fixedWidth.ContainsKey(band));
            if (freeSlots == 0)
            {
                break;
            }

            var unit = (plotWidth - fixedWidth.Values.Sum()) / freeSlots;
            var widened = bands
                .Where(band => !fixedWidth.ContainsKey(band) && slotBands.Count(item => item == band) * unit < minimum[band])
                .ToList();
            if (widened.Count == 0)
            {
                break;
            }

            foreach (var band in widened)
            {
                fixedWidth[band] = minimum[band];
            }
        }

        var free = slotBands.Count(band => !fixedWidth.ContainsKey(band));
        var shared = free == 0 ? 0 : (plotWidth - fixedWidth.Values.Sum()) / free;
        var widths = slotBands
            .Select(band => fixedWidth.TryGetValue(band, out var width) ? width / slotBands.Count(item => item == band) : shared)
            .ToArray();
        // Never wider than the plot: when the widened bands alone overfill it, scale every slot back to fit.
        var total = widths.Sum();
        return total > plotWidth ? widths.Select(width => width * plotWidth / total).ToArray() : widths;
    }

    private static string StageDistributionStageLabel(string code) =>
        string.IsNullOrWhiteSpace(code) ? "Stage not set" : StageCodes.ShortLabelOf(code);

    /// <summary>The category legend as one text box: a square in each category colour, its name and count.</summary>
    private static void AddStageDistributionLegend(
        SlideCanvas canvas,
        IReadOnlyList<string> categories,
        IReadOnlyList<ArppProjectLineVm>? lines,
        double y)
    {
        if (categories.Count == 0)
        {
            return;
        }

        var runs = new List<RichTextRun>();
        foreach (var category in categories)
        {
            if (runs.Count > 0)
            {
                runs.Add(new RichTextRun("      ", 10, canvas.Theme.TextMuted));
            }

            runs.Add(new RichTextRun("■ ", 10, StageDistributionCategoryColor(canvas, category)));
            var count = lines?.Count(line => string.Equals(line.Category, category, StringComparison.OrdinalIgnoreCase));
            runs.Add(new RichTextRun(count is { } value ? $"{category} {value.ToString(CultureInfo.InvariantCulture)}" : category, 10, canvas.Theme.TextSecondary));
        }

        canvas.AddRichTextBox(.62, y, 12.10, .3, new[] { new RichTextParagraph(runs, Align: "ctr") }, "Category legend", verticalAnchor: "ctr");
    }
    // END SECTION

    // SECTION: Slide 2 (and continuations), the projects in four columns
    private const double DistributionColumnLeft = .62;
    private const double DistributionColumnGap = .22;
    private const double DistributionHeadingTop = 1.2;
    private const double DistributionHeadingHeight = .36;
    private const double DistributionRowsTop = DistributionHeadingTop + DistributionHeadingHeight + .1;
    private const double DistributionRowsBottom = 6.62;
    private const double DistributionNameSize = 10;
    private const double DistributionTagSize = 9;
    private const double DistributionMeasureSize = 9.2;
    // Measured on the list exported through PowerPoint (the column box shrinks its text slightly):
    // .196 in for a one-line project and .1375 in for each further line. Kept a little above that.
    private const double DistributionLineHeight = .15;
    private const double DistributionRowGap = .05;
    private const double DistributionBulletIndent = .18;

    private static double DistributionColumnWidth =>
        (12.10 - DistributionColumnGap * (StageDistributionBands.Length - 1)) / StageDistributionBands.Length;

    // One slide (docs/ux/briefing-slides-review.md, PR 3): a band with no projects takes a narrow
    // column, and the rest of the width is shared in parts, a long band taking two or more parts
    // side by side, up to six across. Only a list that does not fit even then pages as before.
    private const double DistributionTotalWidth = 12.10;
    private const double DistributionEmptyWidth = 1.45;
    private const double DistributionPartGap = .16;
    private const int DistributionMaximumParts = 6;

    // A band's projects in parts (side-by-side columns), and the band's width across all of them.
    private sealed record StageDistributionColumn(
        ArppProgressBucket Band,
        string Title,
        int Total,
        IReadOnlyList<IReadOnlyList<ArppProjectLineVm>> Parts,
        double Width,
        double PartWidth);

    private static List<IReadOnlyList<StageDistributionColumn>> PaginateStageDistributionColumns(IReadOnlyList<ArppProjectLineVm> lines)
    {
        // Furthest along first (by stage, or the latest completion), then by name; as on the ARPP slides.
        var bands = StageDistributionBands.Select(band =>
        {
            var inBand = lines.Where(line => line.Bucket == band.Band);
            inBand = band.Band == ArppProgressBucket.Completed
                ? inBand.OrderByDescending(line => line.CompletedSort ?? DateOnly.MinValue).ThenBy(line => line.ProjectName, StringComparer.OrdinalIgnoreCase)
                : inBand.OrderByDescending(line => line.StageRank).ThenBy(line => line.ProjectName, StringComparer.OrdinalIgnoreCase);
            return (band.Band, band.Title, Lines: inBand.ToList());
        }).ToList();

        // Every way of giving each band with projects one to four parts, six parts at most across;
        // of those that fit, the one with the widest parts (the largest text per line).
        var counts = bands.Select(band => band.Lines.Count).ToArray();
        StageDistributionColumn[]? best = null;
        var bestWidth = 0d;
        foreach (var parts in PartAllocations(counts))
        {
            var partWidth = StageDistributionPartWidth(counts, parts);
            if (partWidth <= bestWidth)
            {
                continue;
            }

            var columns = new StageDistributionColumn[bands.Count];
            var fits = true;
            for (var index = 0; index < bands.Count && fits; index++)
            {
                var band = bands[index];
                if (band.Lines.Count == 0)
                {
                    columns[index] = new StageDistributionColumn(band.Band, band.Title, 0, Array.Empty<IReadOnlyList<ArppProjectLineVm>>(), DistributionEmptyWidth, DistributionEmptyWidth);
                    continue;
                }

                var split = SplitIntoParts(band.Lines, band.Band, parts[index], partWidth);
                fits = split.Remainder == 0;
                columns[index] = new StageDistributionColumn(
                    band.Band,
                    band.Title,
                    band.Lines.Count,
                    split.Parts,
                    (parts[index] * partWidth) + ((parts[index] - 1) * DistributionPartGap),
                    partWidth);
            }

            if (fits)
            {
                best = columns;
                bestWidth = partWidth;
            }
        }

        if (best is not null)
        {
            return new List<IReadOnlyList<StageDistributionColumn>> { best };
        }

        // A very long list: equal columns, continued on further slides.
        var queues = bands.Select(band => (band.Band, band.Title, Total: band.Lines.Count, Queue: new Queue<ArppProjectLineVm>(band.Lines))).ToList();
        var pages = new List<IReadOnlyList<StageDistributionColumn>>();
        do
        {
            pages.Add(queues
                .Select(column => new StageDistributionColumn(
                    column.Band,
                    column.Title,
                    column.Total,
                    new[] { (IReadOnlyList<ArppProjectLineVm>)TakeStageDistributionColumn(column.Queue, column.Band, DistributionColumnWidth) },
                    DistributionColumnWidth,
                    DistributionColumnWidth))
                .ToList());
        }
        while (queues.Any(column => column.Queue.Count > 0));

        return pages;
    }

    private static IEnumerable<int[]> PartAllocations(IReadOnlyList<int> counts)
    {
        var current = new int[counts.Count];
        IEnumerable<int[]> Walk(int index, int used)
        {
            if (index == counts.Count)
            {
                if (used > 0 && used <= DistributionMaximumParts)
                {
                    yield return (int[])current.Clone();
                }

                yield break;
            }

            if (counts[index] == 0)
            {
                current[index] = 0;
                foreach (var allocation in Walk(index + 1, used))
                {
                    yield return allocation;
                }

                yield break;
            }

            for (var parts = 1; parts <= 4 && used + parts <= DistributionMaximumParts; parts++)
            {
                current[index] = parts;
                foreach (var allocation in Walk(index + 1, used + parts))
                {
                    yield return allocation;
                }
            }
        }

        return Walk(0, 0);
    }

    // The width of one part when the bands take these many parts (an empty band its narrow column).
    private static double StageDistributionPartWidth(IReadOnlyList<int> counts, IReadOnlyList<int> parts)
    {
        var empty = counts.Count(count => count == 0);
        var totalParts = Math.Max(1, parts.Sum());
        var gaps = (DistributionColumnGap * (counts.Count - 1))
            + parts.Where(count => count > 1).Sum(count => (count - 1) * DistributionPartGap);
        return (DistributionTotalWidth - (empty * DistributionEmptyWidth) - gaps) / totalParts;
    }

    // Fills the parts top to bottom, in order; Remainder is how many projects did not fit.
    private static (IReadOnlyList<IReadOnlyList<ArppProjectLineVm>> Parts, int Remainder) SplitIntoParts(
        IReadOnlyList<ArppProjectLineVm> lines,
        ArppProgressBucket band,
        int partCount,
        double partWidth)
    {
        var queue = new Queue<ArppProjectLineVm>(lines);
        var parts = new List<IReadOnlyList<ArppProjectLineVm>>();
        for (var part = 0; part < partCount && queue.Count > 0; part++)
        {
            parts.Add(TakeStageDistributionColumn(queue, band, partWidth));
        }

        return (parts, queue.Count);
    }

    private static List<ArppProjectLineVm> TakeStageDistributionColumn(Queue<ArppProjectLineVm> lines, ArppProgressBucket band, double width)
    {
        var taken = new List<ArppProjectLineVm>();
        var y = DistributionRowsTop;
        while (lines.Count > 0)
        {
            var height = StageDistributionRowHeight(lines.Peek(), band, width);
            if (y + height > DistributionRowsBottom && taken.Count > 0)
            {
                break;
            }

            taken.Add(lines.Dequeue());
            y += height;
        }

        return taken;
    }

    internal static double StageDistributionRowHeight(ArppProjectLineVm line, ArppProgressBucket band)
        => StageDistributionRowHeight(line, band, DistributionColumnWidth);

    internal static double StageDistributionRowHeight(ArppProjectLineVm line, ArppProgressBucket band, double width)
    {
        // The tag never breaks inside itself, so it is measured as one word. The column's text box shrinks its
        // text to 94% (allowAutoFit) and the tag is a point smaller than the name, so the measure is at 9.2 pt.
        var text = StageDistributionTag(line, band) is { } tag ? $"{line.ProjectName}  {tag.Text.Replace(' ', '\u00a0')}" : line.ProjectName;
        var lines = AnalyticsSlideFrame.LineCount(text, width - DistributionBulletIndent - .06, DistributionMeasureSize);
        return lines * DistributionLineHeight + DistributionRowGap;
    }

    /// <summary>
    /// What follows a project's name: its stage before the supply order, the PDC at Development (overdue when
    /// past) and the completion month and year. Development rows without a PDC carry nothing.
    /// </summary>
    private static (string Text, bool Overdue)? StageDistributionTag(ArppProjectLineVm line, ArppProgressBucket band) => band switch
    {
        ArppProgressBucket.Completed when line.CompletedLabel is { } label => (label, false),
        ArppProgressBucket.PreAon or ArppProgressBucket.AonToSupplyOrder when line.StageCode is { } code => (StageCodes.ShortLabelOf(code), false),
        ArppProgressBucket.UnderDevelopment when line.DevelopmentPdc is { } pdc => line.PdcOverdue
            ? ($"PDC {AnalyticsSlideFrame.Date(pdc)}, overdue", true)
            : ($"PDC {AnalyticsSlideFrame.Date(pdc)}", false),
        _ => null
    };

    private static void RenderStageDistributionList(
        SlideCanvas canvas,
        IReadOnlyList<StageDistributionColumn> columns,
        IReadOnlyList<string> categories,
        bool isFirst,
        int page,
        int pages)
    {
        var title = isFirst ? StageDistributionListTitle : $"{StageDistributionListTitle} (continued)";
        var subtitle = pages > 1
            ? $"PDC for projects at Development, red when overdue; completion month for completed · page {page.ToString(CultureInfo.InvariantCulture)} of {pages.ToString(CultureInfo.InvariantCulture)}"
            : "PDC for projects at Development, red when overdue; completion month for completed";
        AddSlideTitle(canvas, title, subtitle);

        var x = DistributionColumnLeft;
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var width = column.Width;
            var heading = isFirst ? column.Title : $"{column.Title} (cont.)";
            canvas.AddRichTextBox(
                x,
                DistributionHeadingTop,
                width,
                DistributionHeadingHeight,
                new[]
                {
                    new RichTextParagraph(
                        new[]
                        {
                            new RichTextRun(heading, 12, canvas.Theme.TextPrimary, Bold: true),
                            new RichTextRun(column.Total.ToString(CultureInfo.InvariantCulture), 12, canvas.Theme.TextSecondary, Bold: true)
                        },
                        TabStopInches: width - .5,
                        TabAfterFirstRun: true)
                },
                $"{column.Title} heading",
                verticalAnchor: "b",
                leftInset: .02,
                rightInset: .02,
                topInset: 0,
                bottomInset: .04);
            canvas.AddRect(x, DistributionHeadingTop + DistributionHeadingHeight, width, .03, canvas.Theme.HeaderAccent, name: $"{column.Title} rule");

            if (column.Parts.Count == 0 || column.Parts.All(part => part.Count == 0))
            {
                if (isFirst)
                {
                    canvas.AddText(x, DistributionRowsTop, width, .3, "None", 10, canvas.Theme.TextMuted, false, "l", "t", $"{column.Title} none");
                }
            }
            else
            {
                // One text box per part and one paragraph per project, so typing Enter in PowerPoint adds a project.
                for (var part = 0; part < column.Parts.Count; part++)
                {
                    canvas.AddRichTextBox(
                        x + (part * (column.PartWidth + DistributionPartGap)),
                        DistributionRowsTop,
                        column.PartWidth,
                        DistributionRowsBottom - DistributionRowsTop,
                        column.Parts[part].Select(line => StageDistributionParagraph(canvas, line, column.Band)).ToList(),
                        column.Parts.Count == 1 ? $"{column.Title} projects" : $"{column.Title} projects {part + 1}",
                        allowAutoFit: true,
                        leftInset: .02,
                        rightInset: .02,
                        topInset: 0,
                        bottomInset: 0);
                }
            }

            x += width + DistributionColumnGap;
        }

        AddStageDistributionLegend(canvas, categories, null, 6.68);
    }

    private static RichTextParagraph StageDistributionParagraph(SlideCanvas canvas, ArppProjectLineVm line, ArppProgressBucket band)
    {
        var runs = new List<RichTextRun>
        {
            new("■", 9, StageDistributionCategoryColor(canvas, line.Category)),
            new(line.ProjectName, DistributionNameSize, canvas.Theme.TextPrimary)
        };
        if (StageDistributionTag(line, band) is { } tag)
        {
            runs.Add(new RichTextRun(
                $"  {tag.Text.Replace(' ', ' ')}",
                DistributionTagSize,
                tag.Overdue ? canvas.Theme.Critical : canvas.Theme.TextMuted,
                Bold: tag.Overdue));
        }

        return new RichTextParagraph(
            runs,
            LeftMarginInches: DistributionBulletIndent,
            FirstLineIndentInches: -DistributionBulletIndent,
            TabStopInches: DistributionBulletIndent,
            TabAfterFirstRun: true,
            SpaceAfterPoints: DistributionRowGap * 72);
    }
    // END SECTION
}
