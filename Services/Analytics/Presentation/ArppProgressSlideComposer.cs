using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Presentation;
using static ProjectManagement.Services.Analytics.Presentation.AnalyticsSlideFrame;

namespace ProjectManagement.Services.Analytics.Presentation;

public interface IArppProgressSlideComposer
{
    /// <summary>The ARPP progress slides for what the Analytics page shows (year and view), as a .pptx.</summary>
    (byte[] Content, int SlideCount) Compose(ArppAnalyticsVm arpp, DateOnly today);
}

/// <summary>
/// Analytics › ARPP as two or three slides (owner, 6 Nov 2026). Slide 1: the five headline cards and the
/// stage chart stacked by parent category, its stages banded as the cards group them (Pre-AoN, AoN to SO,
/// Under development). Slide 2: every listed project in four columns (Pre-AoN, AoN to
/// SO, Under development, Completed), full names wrapped, a stage tag before the supply order, the PDC for
/// projects at Development (red when overdue) and the completion month and year; a column that does not fit
/// continues on a further slide. Not-known projects, when there are any, get a slide of their own; cancelled
/// projects are counted nowhere and are named only in the presenter's notes (owner, 7 Nov 2026). Built to be
/// edited by hand: each card, column heading and column of projects is one shape, and each project one
/// paragraph. Drawn on the shared canvas in the Analytics slide frame (<see cref="AnalyticsSlideFrame"/>), so
/// it looks like the CoE slides from the same page. Every figure comes from <see cref="ArppAnalyticsVm"/> and
/// <see cref="ArppHeadlines"/>; nothing is counted here.
/// </summary>
public sealed class ArppProgressSlideComposer : IArppProgressSlideComposer
{
    private const string Faint = "7A7F8C";
    private const string Grid = "E4E7EC";

    // The light-theme category colours of the Analytics charts (--pm-chart-cat-*).
    private const string DcdColor = "2563EB";
    private const string CoeColor = "16A34A";
    private const string OtherColor = "D97706";
    private const string NeutralColor = "9AA3B2";

    private readonly string _templatePath;
    private readonly LogoPaths _logoPaths;

    public ArppProgressSlideComposer(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _templatePath = Path.Combine(environment.ContentRootPath, "Resources", "ProjectBriefing", "ProjectBriefingTemplate.pptx");
        _logoPaths = LogoPaths.Find(environment);
    }

    public (byte[] Content, int SlideCount) Compose(ArppAnalyticsVm arpp, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(arpp);
        if (!File.Exists(_templatePath))
        {
            throw new FileNotFoundException("The presentation template was not found.", _templatePath);
        }

        var logos = Logos.From(_logoPaths);
        var when = arpp.AsOfDate is { } asOf ? $"on {Date(asOf)}" : $"as of {Date(today)}";
        var year = $"FY {arpp.FinancialYearDisplay}";
        var categories = CategoryOrder(arpp);
        var listing = arpp.ListedExcludingCancelled;

        // Cancelled projects are counted nowhere on the slides (owner, 7 Nov 2026): the presenter's notes of the
        // first projects slide name them, for information. Not-known projects are counted and get their own slide.
        var cancelled = arpp.ProjectLines
            .Where(line => line.Bucket == ArppProgressBucket.Cancelled)
            .Select(line => line.ProjectName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var notKnown = arpp.ProjectLines
            .Where(line => line.Bucket == ArppProgressBucket.NotKnown)
            .OrderBy(line => line.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var slides = new List<(Action<PptxSlideCanvas, int> Render, string? Notes)>
        {
            ((canvas, page) => RenderOverview(canvas, logos, arpp, listing, year, when, categories, page, today), null)
        };
        var columnPages = PaginateColumns(arpp, Columns);
        var notKnownPages = PaginateFlow(notKnown);
        var notKnownSlide = 2 + columnPages.Count;
        var subtitle = $"{listing.Count} listed projects by where they stand, {when}"
            + (notKnown.Count > 0 ? $" · {notKnown.Count} not known: slide {notKnownSlide}" : string.Empty);
        var cancelledNotes = cancelled.Count == 0
            ? null
            : $"Cancelled, not counted on these slides ({cancelled.Count}):\n" + string.Join("\n", cancelled.Select(name => $"• {name}"));
        for (var index = 0; index < columnPages.Count; index++)
        {
            var pageColumns = columnPages[index];
            var isFirst = index == 0;
            var title = isFirst ? ProjectsTitle(year) : $"{ProjectsTitle(year)} (continued)";
            slides.Add(((canvas, page) => RenderProjects(canvas, logos, subtitle, categories, pageColumns, title, isFirst, page, today), isFirst ? cancelledNotes : null));
        }
        for (var index = 0; index < notKnownPages.Count; index++)
        {
            var pageLines = notKnownPages[index];
            var isFirst = index == 0;
            var title = isFirst ? $"ARPP {year}: not known {when}" : $"ARPP {year}: not known {when} (continued)";
            slides.Add(((canvas, page) => RenderNotKnown(canvas, logos, notKnown.Count, when, categories, pageLines, title, isFirst, page, today), null));
        }

        var templateBytes = File.ReadAllBytes(_templatePath);
        using var stream = new MemoryStream(templateBytes.Length + 500_000);
        stream.Write(templateBytes, 0, templateBytes.Length);
        stream.Position = 0;
        using (var document = PresentationDocument.Open(stream, true))
        {
            var presentationPart = document.PresentationPart
                ?? throw new InvalidOperationException("The PowerPoint template has no presentation part.");
            var layout = PptxTemplate.FindBlankLayout(presentationPart)
                ?? throw new InvalidOperationException("The PowerPoint template has no slide layout.");
            PptxTemplate.RemoveTemplateSlides(presentationPart);
            var slideIds = presentationPart.Presentation.SlideIdList ?? presentationPart.Presentation.AppendChild(new SlideIdList());

            uint nextId = 256;
            for (var index = 0; index < slides.Count; index++)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.AddPart(layout);
                var canvas = new PptxSlideCanvas(slidePart);
                slides[index].Render(canvas, index + 1);
                canvas.Commit();
                slideIds.Append(new SlideId { Id = nextId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
                if (slides[index].Notes is { } notes)
                {
                    PptxSpeakerNotes.Add(presentationPart, slidePart, notes);
                }
            }

            presentationPart.Presentation.Save();
            document.PackageProperties.Title = $"ARPP {year} progress";
            document.PackageProperties.Subject = "ARPP progress generated by PRISM ERP";
            document.PackageProperties.Creator = "Simulator Development Division";
            document.PackageProperties.LastModifiedBy = "PRISM ERP";
            document.PackageProperties.Modified = DateTime.UtcNow;
        }

        return (stream.ToArray(), slides.Count);
    }

    // SECTION: Slide 1 — cards and the stage chart
    private const double CardHeight = 1.25;

    private static void RenderOverview(
        PptxSlideCanvas canvas,
        Logos logos,
        ArppAnalyticsVm arpp,
        ArppListingVm listing,
        string year,
        string when,
        IReadOnlyList<string> categories,
        int page,
        DateOnly today)
    {
        AddHeader(canvas, logos, $"ARPP {year}: progress {when}", $"{listing.Count} listed projects · IPA {Utilities.IndianCurrencyFormatter.FormatCompact(listing.IpaValue)}");

        // The page's five cards, without cancelled projects; the supply-order card reads as the column it
        // feeds on slide 2. One accent for all five: the category colours belong to the chart.
        var cards = ArppHeadlines.Build(arpp, excludeCancelled: true);
        const double gap = .15;
        var cardWidth = (ContentWidth - gap * (cards.Count - 1)) / cards.Count;
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var label = card.Key switch
            {
                ArppHeadlines.SupplyOrder => "Under development",
                ArppHeadlines.AonToSupplyOrder => "AoN to SO",
                ArppHeadlines.PreAon => "Pre-AoN",
                _ => card.Label
            };
            AddCard(canvas, Left + index * (cardWidth + gap), BodyTop, cardWidth, CardHeight, card.Value, label, card.Hint, Navy, figureSize: 26, labelSize: 12, lineSize: 9.5);
        }

        // Legend: only the categories present among the ongoing projects, with their counts.
        var counts = arpp.ProjectLines.Where(line => line.StageCode is not null).GroupBy(line => line.Category).ToDictionary(group => group.Key, group => group.Count());
        RenderLegend(canvas, categories, counts, Left, BodyTop + CardHeight + .14, ContentWidth, "l");

        // The stage chart, grouped as the cards are (owner, 7 Nov 2026): a band each for Pre-AoN, AoN to SO and
        // Under development holding the stages its projects stand at, the group's count at its foot, then any
        // ongoing project no group holds (no stage recorded, or not known). Each bar is the projects of that
        // group at that stage, so a band's bars always add up to its card. Empty stages are left out.
        var ongoing = arpp.ProjectLines.Where(line => line.StageCode is not null).ToList();
        var slots = new List<(string Code, ArppProgressBucket? Group, List<ArppProjectLineVm> Lines)>();
        foreach (var group in ChartGroups.Select(item => (ArppProgressBucket?)item.Bucket).Append(null))
        {
            var inGroup = ongoing.Where(line => group is { } bucket ? line.Bucket == bucket : ChartGroups.All(item => item.Bucket != line.Bucket));
            slots.AddRange(inGroup
                .GroupBy(line => line.StageCode!, StringComparer.OrdinalIgnoreCase)
                .OrderBy(stage => StageOrder(stage.Key))
                .Select(stage => (stage.Key, group, stage.ToList())));
        }

        var top = NiceTop(slots.Select(item => item.Lines.Count).DefaultIfEmpty(0).Max());
        const double plotLeft = Left + .45, plotRight = Right, plotTop = 3.3, plotBottom = 6.2;
        var plotHeight = plotBottom - plotTop;
        var slot = (plotRight - plotLeft) / Math.Max(1, slots.Count);
        var barWidth = Math.Min(.62, slot * .58);

        // Bands first, behind the grid and bars: one shape each, its label anchored at the foot.
        foreach (var (bucket, title, fill) in ChartGroups)
        {
            var first = slots.FindIndex(item => item.Group == bucket);
            if (first < 0)
            {
                continue;
            }

            var last = slots.FindLastIndex(item => item.Group == bucket);
            var count = slots.Where(item => item.Group == bucket).Sum(item => item.Lines.Count);
            canvas.AddTextBox(
                plotLeft + slot * first + .03, plotTop - .1, slot * (last - first + 1) - .06, plotBottom - plotTop + .72,
                new[]
                {
                    new PptxTextParagraph(new[] { new PptxRun(title, 10.5, Ink, true), new PptxRun($" · {count}", 10.5, Muted, true) }) { Align = "ctr" }
                },
                $"{title} band",
                fill: fill,
                anchor: "b",
                insets: new PptxInsets(.05, 0, .05, .08));
        }

        var step = top <= 5 ? 1 : top <= 10 ? 2 : top <= 25 ? 5 : 10;
        for (var tick = 0; tick <= top; tick += step)
        {
            var y = plotBottom - plotHeight * tick / top;
            canvas.AddLine(plotLeft, y, plotRight, y, tick == 0 ? "B8BDC7" : Grid, tick == 0 ? 1 : .5);
            canvas.AddText(plotLeft - .5, y - .12, .44, .24, tick.ToString(CultureInfo.InvariantCulture), 9, Faint, false, "r");
        }

        for (var index = 0; index < slots.Count; index++)
        {
            var (code, _, lines) = slots[index];
            var x = plotLeft + slot * index + (slot - barWidth) / 2;
            var y = plotBottom;
            foreach (var category in categories)
            {
                var count = lines.Count(line => line.Category == category);
                if (count == 0)
                {
                    continue;
                }

                var height = plotHeight * count / top;
                y -= height;
                canvas.AddRect(x, y, barWidth, height, ColorOf(category));
                if (height >= .24)
                {
                    canvas.AddText(x, y, barWidth, height, count.ToString(CultureInfo.InvariantCulture), 10, "FFFFFF", true, "ctr");
                }
            }

            canvas.AddText(x - .2, y - .28, barWidth + .4, .26, lines.Count.ToString(CultureInfo.InvariantCulture), 11, Ink, true, "ctr");
            canvas.AddText(plotLeft + slot * index, plotBottom + .05, slot, .26, StageCodes.ShortLabelOf(code), 10, Muted, false, "ctr");
        }

        AddFooter(canvas, today, page);
    }

    // The chart's bands, in the order of the cards they match.
    private static readonly (ArppProgressBucket Bucket, string Title, string Fill)[] ChartGroups =
    {
        (ArppProgressBucket.PreAon, "Pre-AoN", "F4F5F8"),
        (ArppProgressBucket.AonToSupplyOrder, "AoN to SO", "ECEFF4"),
        (ArppProgressBucket.UnderDevelopment, "Under development", "E8F0FB")
    };

    // Workflow order; a bucket outside the workflow (no stage recorded, then not known) after it.
    private static int StageOrder(string code)
    {
        var index = Array.FindIndex(StageCodes.All, stage => Same(stage, code));
        return index >= 0 ? index : Same(code, "NOT_KNOWN") ? StageCodes.All.Length + 1 : StageCodes.All.Length;
    }
    // END SECTION

    // SECTION: Slide 2 (and continuations) — projects in four columns
    private const double ColumnGap = .18;
    private const double RowsTop = BodyTop + HeadingHeight + .08;
    private const double NameSize = 10;
    private const double DetailSize = 9;
    private const double LineHeight = .17;
    private const double RowGap = .07;
    private const double BulletIndent = .22;

    private static readonly (ArppProgressBucket Bucket, string Title, string Fill)[] Columns =
    {
        (ArppProgressBucket.PreAon, "Pre-AoN", HeaderTint),
        (ArppProgressBucket.AonToSupplyOrder, "AoN to SO", HeaderTint),
        (ArppProgressBucket.UnderDevelopment, "Under development", "E3ECFA"),
        (ArppProgressBucket.Completed, "Completed", "E6F2EA")
    };

    private const string NotKnownFill = "EEF0F3";

    private static string ProjectsTitle(string year) => $"ARPP {year}: where each project stands";

    private static double ColumnWidth => (ContentWidth - ColumnGap * (Columns.Length - 1)) / Columns.Length;

    private sealed record ColumnPage(ArppProgressBucket Bucket, string Title, string Fill, int Total, IReadOnlyList<ArppProjectLineVm> Lines);

    private static List<IReadOnlyList<ColumnPage>> PaginateColumns(ArppAnalyticsVm arpp, IReadOnlyList<(ArppProgressBucket Bucket, string Title, string Fill)> columns)
    {
        // Furthest along first (by stage, or the latest completion), then by name.
        var queues = columns.Select(column =>
        {
            var lines = arpp.ProjectLines.Where(line => line.Bucket == column.Bucket);
            lines = column.Bucket == ArppProgressBucket.Completed
                ? lines.OrderByDescending(line => line.CompletedSort ?? DateOnly.MinValue).ThenBy(line => line.ProjectName, StringComparer.OrdinalIgnoreCase)
                : lines.OrderByDescending(line => line.StageRank).ThenBy(line => line.ProjectName, StringComparer.OrdinalIgnoreCase);
            return (column, lines: new Queue<ArppProjectLineVm>(lines));
        }).ToList();
        var totals = queues.ToDictionary(item => item.column.Bucket, item => item.lines.Count);

        var pages = new List<IReadOnlyList<ColumnPage>>();
        do
        {
            var page = new List<ColumnPage>();
            foreach (var (column, lines) in queues)
            {
                page.Add(new ColumnPage(column.Bucket, column.Title, column.Fill, totals[column.Bucket], TakeColumn(lines, column.Bucket)));
            }

            pages.Add(page);
        }
        while (queues.Any(item => item.lines.Count > 0));

        return pages;
    }

    /// <summary>Not-known projects flow through four text columns of one box, as many slides as they need.</summary>
    private static List<IReadOnlyList<ArppProjectLineVm>> PaginateFlow(IReadOnlyList<ArppProjectLineVm> lines)
    {
        var queue = new Queue<ArppProjectLineVm>(lines);
        var pages = new List<IReadOnlyList<ArppProjectLineVm>>();
        while (queue.Count > 0)
        {
            var page = new List<ArppProjectLineVm>();
            for (var column = 0; column < Columns.Length && queue.Count > 0; column++)
            {
                page.AddRange(TakeColumn(queue, ArppProgressBucket.NotKnown));
            }

            pages.Add(page);
        }

        return pages;
    }

    private static List<ArppProjectLineVm> TakeColumn(Queue<ArppProjectLineVm> lines, ArppProgressBucket bucket)
    {
        var taken = new List<ArppProjectLineVm>();
        var y = RowsTop;
        while (lines.Count > 0)
        {
            var height = RowHeight(lines.Peek(), bucket);
            if (y + height > BodyBottom && taken.Count > 0)
            {
                break;
            }

            taken.Add(lines.Dequeue());
            y += height;
        }

        return taken;
    }

    private static double RowHeight(ArppProjectLineVm line, ArppProgressBucket bucket)
    {
        // The tag (stage or completion) follows the name on its last line, pushed right by a tab.
        var tag = TagOf(line, bucket);
        var nameLines = LineCount(tag is null ? line.ProjectName : $"{line.ProjectName}    {tag}", ColumnWidth - BulletIndent, NameSize);
        // A 9 pt PDC line is a little shorter than a 10 pt name line.
        var detail = PdcOf(line, bucket) is null ? 0 : .15;
        return nameLines * LineHeight + detail + RowGap;
    }

    // Development rows carry no stage tag (owner, 6 Nov 2026); completed rows show the month and year.
    private static string? TagOf(ArppProjectLineVm line, ArppProgressBucket bucket) => bucket switch
    {
        ArppProgressBucket.Completed => line.CompletedLabel is { } label ? NoBreak(label) : null,
        ArppProgressBucket.PreAon or ArppProgressBucket.AonToSupplyOrder => line.StageCode is { } code ? StageCodes.ShortLabelOf(code) : null,
        _ => null
    };

    private static (string Text, string Color)? PdcOf(ArppProjectLineVm line, ArppProgressBucket bucket) =>
        bucket == ArppProgressBucket.UnderDevelopment && line.DevelopmentPdc is { } pdc
            ? line.PdcOverdue ? ($"PDC {Date(pdc)} · overdue", Red) : ($"PDC {Date(pdc)}", Muted)
            : null;

    /// <summary>
    /// One paragraph per project: a bullet in the category colour, the name, the tag at the right tab stop and,
    /// at Development, the PDC on the next line. Typing Enter in PowerPoint adds a project the same way.
    /// </summary>
    private static PptxTextParagraph ProjectParagraph(ArppProjectLineVm line, ArppProgressBucket bucket, double width)
    {
        var runs = new List<PptxRun> { new(line.ProjectName, NameSize, Ink) };
        if (TagOf(line, bucket) is { } tag)
        {
            runs.Add(new PptxRun("\t" + tag, DetailSize, Muted));
        }
        if (PdcOf(line, bucket) is { } pdc)
        {
            runs.Add(PptxRun.LineBreak(DetailSize));
            runs.Add(new PptxRun(pdc.Text, DetailSize, pdc.Color));
        }

        return new PptxTextParagraph(runs)
        {
            BulletColor = ColorOf(line.Category),
            BulletIndent = BulletIndent,
            RightTabAt = width - .02,
            SpaceAfterPoints = RowGap * 72
        };
    }

    private static void RenderProjects(
        PptxSlideCanvas canvas,
        Logos logos,
        string subtitle,
        IReadOnlyList<string> categories,
        IReadOnlyList<ColumnPage> columns,
        string title,
        bool isFirst,
        int page,
        DateOnly today)
    {
        RenderHeader(canvas, logos, title, subtitle, categories);

        // Two shapes per column: the heading bar (title and count) and one text box holding every project.
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var x = Left + index * (ColumnWidth + ColumnGap);
            var heading = isFirst ? column.Title : $"{column.Title} (cont.)";
            AddHeading(canvas, x, BodyTop, ColumnWidth, heading, column.Total, $"{column.Title} heading", column.Fill);
            if (column.Lines.Count > 0)
            {
                canvas.AddTextBox(
                    x, RowsTop, ColumnWidth, BodyBottom - RowsTop,
                    column.Lines.Select(line => ProjectParagraph(line, column.Bucket, ColumnWidth)).ToList(),
                    $"{column.Title} projects",
                    shrinkOnOverflow: true);
            }
        }

        AddFooter(canvas, today, page);
    }

    private static void RenderNotKnown(
        PptxSlideCanvas canvas,
        Logos logos,
        int total,
        string when,
        IReadOnlyList<string> categories,
        IReadOnlyList<ArppProjectLineVm> lines,
        string title,
        bool isFirst,
        int page,
        DateOnly today)
    {
        RenderHeader(canvas, logos, title, $"{total} listed {(total == 1 ? "project" : "projects")} whose position {when} the records cannot tell", categories);
        AddHeading(canvas, Left, BodyTop, ContentWidth, isFirst ? "Not known" : "Not known (cont.)", total, "Not known heading", NotKnownFill);
        canvas.AddTextBox(
            Left, RowsTop, ContentWidth, BodyBottom - RowsTop,
            lines.Select(line => ProjectParagraph(line, ArppProgressBucket.NotKnown, ColumnWidth)).ToList(),
            "Not known projects",
            shrinkOnOverflow: true,
            columns: Columns.Length,
            columnGap: ColumnGap);
        AddFooter(canvas, today, page);
    }
    // END SECTION

    // SECTION: Header with the category legend
    private static void RenderHeader(PptxSlideCanvas canvas, Logos logos, string title, string subtitle, IReadOnlyList<string> legend)
    {
        var titleRight = AddHeader(canvas, logos, title, subtitle);
        if (legend.Count > 0)
        {
            // On the subtitle's line, at the right of the title area.
            RenderLegend(canvas, legend, null, titleRight - 5.2, .74, 5.2, "r");
        }
    }

    /// <summary>The category legend as one text box: a dot in each category colour, then its name.</summary>
    private static void RenderLegend(PptxSlideCanvas canvas, IReadOnlyList<string> categories, IReadOnlyDictionary<string, int>? counts, double x, double y, double width, string align)
    {
        var runs = new List<PptxRun>();
        foreach (var name in categories)
        {
            if (runs.Count > 0)
            {
                runs.Add(new PptxRun("      ", 10, Muted));
            }
            runs.Add(new PptxRun("● ", 10, ColorOf(name)));
            runs.Add(new PptxRun(counts is not null && counts.TryGetValue(name, out var count) ? $"{name} {count}" : name, 10, Muted));
        }

        canvas.AddTextBox(x, y, width, .28, new[] { new PptxTextParagraph(runs) { Align = align } }, "Category legend", anchor: "ctr");
    }
    // END SECTION

    // SECTION: Helpers
    private static IReadOnlyList<string> CategoryOrder(ArppAnalyticsVm arpp)
    {
        var present = arpp.ByStageByCategory.Select(point => point.CategoryName)
            .Concat(arpp.ProjectLines.Where(line => line.Bucket is not ArppProgressBucket.Cancelled and not ArppProgressBucket.NotKnown).Select(line => line.Category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ordered = arpp.StageCategoryOrder.Where(name => present.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        ordered.AddRange(present.Where(name => !ordered.Contains(name, StringComparer.OrdinalIgnoreCase)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        return ordered;
    }

    private static string ColorOf(string category)
    {
        var key = category.Trim().ToLowerInvariant();
        if (key.StartsWith("dcd", StringComparison.Ordinal)) return DcdColor;
        if (key.Contains("coe") || key.Contains("centre of excellence") || key.Contains("center of excellence")) return CoeColor;
        if (key.Contains("other") || key.Contains("r&d")) return OtherColor;
        return NeutralColor;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static int NiceTop(int max)
    {
        if (max <= 0) return 5;
        foreach (var top in new[] { 5, 10, 15, 20, 25, 30, 40, 50, 60, 80, 100 })
        {
            if (max < top) return top;
        }
        return (int)Math.Ceiling(max / 50d) * 50;
    }
    // END SECTION
}
