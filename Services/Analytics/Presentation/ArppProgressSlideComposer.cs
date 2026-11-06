using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Analytics.Presentation;

public interface IArppProgressSlideComposer
{
    /// <summary>The ARPP progress slides for what the Analytics page shows (year and view), as a .pptx.</summary>
    (byte[] Content, int SlideCount) Compose(ArppAnalyticsVm arpp, DateOnly today);
}

/// <summary>
/// Analytics › ARPP as two or three slides (owner, 6 Nov 2026). Slide 1: the five headline cards and the
/// stage chart stacked by parent category. Slide 2: every listed project in four columns (Pre-AoN, AoN to
/// SO, Under development, Completed), full names wrapped, a stage tag before the supply order, the PDC for
/// projects at Development (red when overdue) and the completion month and year; a column that does not fit
/// continues on a further slide. ARTRAC insignia left, SDD insignia right, as the Briefing Studio header; no
/// handling marking. Every figure comes from <see cref="ArppAnalyticsVm"/> and <see cref="ArppHeadlines"/>;
/// nothing is counted here.
/// </summary>
public sealed class ArppProgressSlideComposer : IArppProgressSlideComposer
{
    private const double SlideWidth = 13.333333;
    private const double SlideHeight = 7.5;
    private const double Margin = .4;

    private const string Navy = "1F3A68";
    private const string Ink = "1F2430";
    private const string Muted = "5B6170";
    private const string Faint = "7A7F8C";
    private const string Rule = "D8DBE2";
    private const string CardFill = "F3F5F9";
    private const string Grid = "E4E7EC";
    private const string OverdueRed = "B3261E";

    // The light-theme category colours of the Analytics charts (--pm-chart-cat-*).
    private const string DcdColor = "2563EB";
    private const string CoeColor = "16A34A";
    private const string OtherColor = "D97706";
    private const string NeutralColor = "9AA3B2";

    private static readonly IReadOnlyDictionary<string, string> StageShort = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["FS"] = "FS", ["IPA"] = "IPA", ["SOW"] = "SoW", ["AON"] = "AoN", ["BID"] = "Bid", ["TEC"] = "TEC", ["BM"] = "BM",
        ["COB"] = "COB", ["PNC"] = "PNC", ["EAS"] = "EAS", ["SO"] = "SO", ["DEVP"] = "Devp", ["ATP"] = "ATP",
        ["PAYMENT"] = "Pay", ["TOT"] = "ToT", ["UNASSIGNED"] = "Unassigned", ["NOT_KNOWN"] = "Not known"
    };

    private readonly string _templatePath;
    private readonly string? _leftLogoPath;
    private readonly string? _rightLogoPath;

    public ArppProgressSlideComposer(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _templatePath = Path.Combine(environment.ContentRootPath, "Resources", "ProjectBriefing", "ProjectBriefingTemplate.pptx");
        var left = Path.Combine(environment.WebRootPath ?? string.Empty, "img", "logos", "artrac.png");
        var right = Path.Combine(environment.WebRootPath ?? string.Empty, "img", "logos", "sdd.png");
        _leftLogoPath = File.Exists(left) ? left : null;
        _rightLogoPath = File.Exists(right) ? right : null;
    }

    public (byte[] Content, int SlideCount) Compose(ArppAnalyticsVm arpp, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(arpp);
        if (!File.Exists(_templatePath))
        {
            throw new FileNotFoundException("The presentation template was not found.", _templatePath);
        }

        var leftLogo = _leftLogoPath is null ? null : File.ReadAllBytes(_leftLogoPath);
        var rightLogo = _rightLogoPath is null ? null : File.ReadAllBytes(_rightLogoPath);
        var shownOn = arpp.AsOfDate ?? today;
        var when = arpp.AsOfDate is { } asOf ? $"on {asOf:d MMM yyyy}" : $"as of {today:d MMM yyyy}";
        var year = $"FY {arpp.FinancialYearDisplay}";
        var categories = CategoryOrder(arpp);

        var renders = new List<Action<SlideCanvas, int>>
        {
            (canvas, page) => RenderOverview(canvas, arpp, year, when, categories, leftLogo, rightLogo, page, today)
        };
        var columnPages = PaginateColumns(arpp, Columns);
        // Cancelled and not-known projects: named in the footer when that fits on two lines, otherwise
        // listed on their own slide (or slides) after the columns, never cut off.
        var others = OtherProjectsText(arpp, when);
        var othersFitFooter = others is null || WrapCount(others, FooterTextWidth, FooterSize) <= 2;
        var otherPages = othersFitFooter ? new List<IReadOnlyList<ColumnPage>>() : PaginateColumns(arpp, OtherColumns);
        for (var index = 0; index < columnPages.Count; index++)
        {
            var pageColumns = columnPages[index];
            var isFirst = index == 0;
            var footerOthers = index == columnPages.Count - 1
                ? othersFitFooter ? others : "Cancelled and not-known projects: on the next slide"
                : null;
            renders.Add((canvas, page) => RenderProjects(canvas, arpp, year, when, shownOn, categories, pageColumns, isFirst ? ProjectsTitle(year) : $"{ProjectsTitle(year)} (continued)", isFirst, footerOthers, leftLogo, rightLogo, page, today));
        }
        for (var index = 0; index < otherPages.Count; index++)
        {
            var pageColumns = otherPages[index];
            var isFirst = index == 0;
            renders.Add((canvas, page) => RenderProjects(canvas, arpp, year, when, shownOn, categories, pageColumns, isFirst ? $"ARPP {year}: cancelled and not known" : $"ARPP {year}: cancelled and not known (continued)", isFirst, null, leftLogo, rightLogo, page, today));
        }

        var templateBytes = File.ReadAllBytes(_templatePath);
        using var stream = new MemoryStream(templateBytes.Length + 500_000);
        stream.Write(templateBytes, 0, templateBytes.Length);
        stream.Position = 0;
        using (var document = PresentationDocument.Open(stream, true))
        {
            var presentationPart = document.PresentationPart
                ?? throw new InvalidOperationException("The PowerPoint template has no presentation part.");
            var layout = presentationPart.SlideMasterParts
                    .SelectMany(master => master.SlideLayoutParts)
                    .FirstOrDefault(part => string.Equals(part.SlideLayout?.CommonSlideData?.Name?.Value, "Blank", StringComparison.OrdinalIgnoreCase))
                ?? presentationPart.SlideMasterParts.SelectMany(master => master.SlideLayoutParts).First();

            var slideIds = presentationPart.Presentation.SlideIdList ?? presentationPart.Presentation.AppendChild(new SlideIdList());
            foreach (var slideId in slideIds.Elements<SlideId>().ToList())
            {
                if (slideId.RelationshipId?.Value is { } relationship && presentationPart.GetPartById(relationship) is SlidePart old)
                {
                    presentationPart.DeletePart(old);
                }
                slideId.Remove();
            }

            uint nextId = 256;
            for (var index = 0; index < renders.Count; index++)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.AddPart(layout);
                var canvas = new SlideCanvas(slidePart);
                renders[index](canvas, index + 1);
                canvas.Commit();
                slideIds.Append(new SlideId { Id = nextId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }

            presentationPart.Presentation.Save();
            document.PackageProperties.Title = $"ARPP {year} progress";
            document.PackageProperties.Subject = "ARPP progress generated by PRISM ERP";
            document.PackageProperties.Creator = "Simulator Development Division";
            document.PackageProperties.LastModifiedBy = "PRISM ERP";
            document.PackageProperties.Modified = DateTime.UtcNow;
        }

        return (stream.ToArray(), renders.Count);
    }

    // SECTION: Slide 1 — cards and the stage chart
    private static void RenderOverview(
        SlideCanvas canvas,
        ArppAnalyticsVm arpp,
        string year,
        string when,
        IReadOnlyList<string> categories,
        byte[]? leftLogo,
        byte[]? rightLogo,
        int page,
        DateOnly today)
    {
        RenderHeader(canvas, $"ARPP {year}: progress {when}", $"{arpp.ListedCount} listed projects · IPA {Utilities.IndianCurrencyFormatter.FormatCompact(arpp.ListedIpaValue)}", leftLogo, rightLogo, null);

        // The page's five cards; the supply-order card reads as the column it feeds on slide 2.
        var cards = ArppHeadlines.Build(arpp);
        var gap = .15;
        var cardWidth = (SlideWidth - 2 * Margin - gap * (cards.Count - 1)) / cards.Count;
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var x = Margin + index * (cardWidth + gap);
            var label = card.Key switch
            {
                ArppHeadlines.SupplyOrder => "Under development",
                ArppHeadlines.AonToSupplyOrder => "AoN to SO",
                ArppHeadlines.PreAon => "Pre-AoN",
                _ => card.Label
            };
            canvas.AddRect(x, 1.2, cardWidth, 1.25, CardFill, radius: true);
            canvas.AddText(x + .14, 1.26, cardWidth - .28, .48, card.Value, 26, Ink, true, "l", anchor: "t");
            canvas.AddText(x + .14, 1.72, cardWidth - .28, .26, label, 12, Ink, true, "l", anchor: "t");
            canvas.AddText(x + .14, 1.98, cardWidth - .28, .42, card.Hint, 9.5, Muted, false, "l", anchor: "t");
        }

        // Legend: only the categories present in the year's listed projects.
        RenderLegend(canvas, categories, arpp.ByStageByCategory.GroupBy(point => point.CategoryName).ToDictionary(group => group.Key, group => group.Sum(point => point.Count)), Margin, 2.66, alignRight: false);

        // The stage chart: every workflow stage in order, then any other bucket present, stacked by category.
        var codes = StageCodes.All
            .Where(code => !StageCodes.IsTot(code) || arpp.ByStageByCategory.Any(point => Same(point.StageCode, code)))
            .Concat(arpp.ByStageByCategory.Select(point => point.StageCode).Where(code => !StageCodes.All.Contains(code, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            .ToList();
        var totals = codes.Select(code => arpp.ByStageByCategory.Where(point => Same(point.StageCode, code)).Sum(point => point.Count)).ToList();
        var top = NiceTop(totals.DefaultIfEmpty(0).Max());
        const double plotLeft = .85, plotRight = 12.93, plotTop = 3.15, plotBottom = 6.45;
        var plotHeight = plotBottom - plotTop;
        var step = top <= 5 ? 1 : top <= 10 ? 2 : top <= 25 ? 5 : 10;
        for (var tick = 0; tick <= top; tick += step)
        {
            var y = plotBottom - plotHeight * tick / top;
            canvas.AddLine(plotLeft, y, plotRight, y, tick == 0 ? "B8BDC7" : Grid, tick == 0 ? 1 : .5);
            canvas.AddText(plotLeft - .5, y - .12, .42, .24, tick.ToString(CultureInfo.InvariantCulture), 9, Faint, false, "r");
        }

        var slot = (plotRight - plotLeft) / Math.Max(1, codes.Count);
        var barWidth = Math.Min(.62, slot * .58);
        for (var index = 0; index < codes.Count; index++)
        {
            var code = codes[index];
            var x = plotLeft + slot * index + (slot - barWidth) / 2;
            var y = plotBottom;
            foreach (var category in categories)
            {
                var count = arpp.ByStageByCategory.Where(point => Same(point.StageCode, code) && point.CategoryName == category).Sum(point => point.Count);
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

            if (totals[index] > 0)
            {
                canvas.AddText(x - .2, y - .28, barWidth + .4, .26, totals[index].ToString(CultureInfo.InvariantCulture), 11, Ink, true, "ctr");
            }
            canvas.AddText(plotLeft + slot * index, plotBottom + .05, slot, .26, ShortOf(code), 10, Muted, false, "ctr");
        }

        var source = new List<string> { $"Published ARPP {year}, latest row per project" };
        if (arpp.AddedByAddendumCount > 0)
        {
            source.Add($"{arpp.AddedByAddendumCount} added or changed by addendum");
        }
        if (arpp.CancelledCount > 0)
        {
            source.Add($"{arpp.CancelledCount} cancelled");
        }
        if (arpp.UnlinkedRowCount > 0)
        {
            source.Add($"{arpp.UnlinkedRowCount} unlinked {(arpp.UnlinkedRowCount == 1 ? "row" : "rows")} excluded");
        }
        RenderFooter(canvas, string.Join(" · ", source), page, today);
    }
    // END SECTION

    // SECTION: Slide 2 (and continuations) — projects in four columns
    private const double ColumnGap = .18;
    private const double ColumnsTop = 1.2;
    private const double RowsTop = 1.62;
    private const double RowsBottom = 6.9;
    private const double NameSize = 10;
    private const double DetailSize = 9;
    private const double LineHeight = .17;
    private const double RowGap = .07;

    private static readonly (ArppProgressBucket Bucket, string Title, string Fill)[] Columns =
    {
        (ArppProgressBucket.PreAon, "Pre-AoN", "EEF1F6"),
        (ArppProgressBucket.AonToSupplyOrder, "AoN to SO", "EEF1F6"),
        (ArppProgressBucket.UnderDevelopment, "Under development", "E8F0FB"),
        (ArppProgressBucket.Completed, "Completed", "EAF5EE")
    };

    // Their own slide when they do not fit the footer: same column widths, two columns.
    private static readonly (ArppProgressBucket Bucket, string Title, string Fill)[] OtherColumns =
    {
        (ArppProgressBucket.Cancelled, "Cancelled", "F6EEEE"),
        (ArppProgressBucket.NotKnown, "Not known", "F1F2F4")
    };

    private const double FooterSize = 9;
    private static double FooterTextWidth => SlideWidth - 2 * Margin - 3.3;

    private static string ProjectsTitle(string year) => $"ARPP {year}: where each project stands";

    private static string? OtherProjectsText(ArppAnalyticsVm arpp, string when)
    {
        var cancelled = arpp.ProjectLines.Where(line => line.Bucket == ArppProgressBucket.Cancelled).Select(line => line.ProjectName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        var notKnown = arpp.ProjectLines.Where(line => line.Bucket == ArppProgressBucket.NotKnown).Select(line => line.ProjectName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        var parts = new List<string>();
        if (cancelled.Count > 0)
        {
            parts.Add($"Cancelled: {string.Join("; ", cancelled)}");
        }
        if (notKnown.Count > 0)
        {
            parts.Add($"Not known {when}: {string.Join("; ", notKnown)}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static double ColumnWidth => (SlideWidth - 2 * Margin - ColumnGap * (Columns.Length - 1)) / Columns.Length;

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
                var taken = new List<ArppProjectLineVm>();
                var y = RowsTop;
                while (lines.Count > 0)
                {
                    var height = RowHeight(lines.Peek(), column.Bucket);
                    if (y + height > RowsBottom && taken.Count > 0)
                    {
                        break;
                    }

                    taken.Add(lines.Dequeue());
                    y += height;
                }

                page.Add(new ColumnPage(column.Bucket, column.Title, column.Fill, totals[column.Bucket], taken));
            }

            pages.Add(page);
        }
        while (queues.Any(item => item.lines.Count > 0));

        return pages;
    }

    private static double RowHeight(ArppProjectLineVm line, ArppProgressBucket bucket)
    {
        var nameLines = WrapCount(line.ProjectName, NameWidth(bucket), NameSize);
        // A 9 pt PDC line is a little shorter than a 10 pt name line.
        var detail = bucket == ArppProgressBucket.UnderDevelopment && line.DevelopmentPdc.HasValue ? .15 : 0;
        return nameLines * LineHeight + detail + RowGap;
    }

    // Development rows carry no stage tag (owner, 6 Nov 2026); the others keep a tag on the right.
    private static double TagWidth(ArppProgressBucket bucket) => bucket switch
    {
        ArppProgressBucket.UnderDevelopment or ArppProgressBucket.Cancelled or ArppProgressBucket.NotKnown => 0,
        ArppProgressBucket.Completed => .7,
        _ => .45
    };

    private static double NameWidth(ArppProgressBucket bucket) => ColumnWidth - .22 - TagWidth(bucket);

    private static void RenderProjects(
        SlideCanvas canvas,
        ArppAnalyticsVm arpp,
        string year,
        string when,
        DateOnly shownOn,
        IReadOnlyList<string> categories,
        IReadOnlyList<ColumnPage> columns,
        string title,
        bool isFirst,
        string? footerOthers,
        byte[]? leftLogo,
        byte[]? rightLogo,
        int page,
        DateOnly today)
    {
        var subtitle = $"{arpp.ListedCount} listed projects by where they stand, {when}";
        RenderHeader(canvas, title, subtitle, leftLogo, rightLogo, categories);

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var x = Margin + index * (ColumnWidth + ColumnGap);
            canvas.AddRect(x, ColumnsTop, ColumnWidth, .34, column.Fill, radius: true);
            canvas.AddText(x + .1, ColumnsTop, ColumnWidth - .7, .34, isFirst ? column.Title : $"{column.Title} (cont.)", 12, Ink, true, "l");
            canvas.AddText(x + ColumnWidth - .6, ColumnsTop, .5, .34, column.Total.ToString(CultureInfo.InvariantCulture), 12, Ink, true, "r");

            var y = RowsTop;
            foreach (var line in column.Lines)
            {
                var rowHeight = RowHeight(line, column.Bucket);
                canvas.AddEllipse(x + .05, y + .055, .1, .1, ColorOf(line.Category));

                // The name and, at Development, its PDC in one box: the PDC always sits right under the name.
                var paragraphs = new List<(string Text, double Size, string Color)> { (line.ProjectName, NameSize, Ink) };
                if (column.Bucket == ArppProgressBucket.UnderDevelopment && line.DevelopmentPdc is { } pdc)
                {
                    paragraphs.Add((line.PdcOverdue ? $"PDC {pdc:d MMM yyyy} · overdue" : $"PDC {pdc:d MMM yyyy}", DetailSize, line.PdcOverdue ? OverdueRed : Muted));
                }
                canvas.AddParagraphs(x + .22, y, NameWidth(column.Bucket), rowHeight - RowGap + .02, paragraphs);

                var tag = column.Bucket switch
                {
                    ArppProgressBucket.Completed => line.CompletedLabel,
                    ArppProgressBucket.UnderDevelopment => null,
                    _ => line.StageCode is { } code ? ShortOf(code) : null
                };
                if (tag is not null)
                {
                    canvas.AddText(x + ColumnWidth - TagWidth(column.Bucket), y, TagWidth(column.Bucket), LineHeight + .02, tag, DetailSize, Muted, false, "r", anchor: "t");
                }

                y += rowHeight;
                canvas.AddLine(x, y - RowGap / 2, x + ColumnWidth, y - RowGap / 2, "ECEEF2", .5);
            }
        }

        var footer = new List<string>();
        if (footerOthers is not null)
        {
            footer.Add(footerOthers);
        }
        if (arpp.ProjectLines.Any(line => line.PdcOverdue))
        {
            footer.Add("PDC in red: overdue");
        }
        RenderFooter(canvas, footer.Count == 0 ? $"Published ARPP {year}, latest row per project" : string.Join(" · ", footer), page, today);
    }
    // END SECTION

    // SECTION: Shared header, legend and footer
    private static void RenderHeader(SlideCanvas canvas, string title, string subtitle, byte[]? leftLogo, byte[]? rightLogo, IReadOnlyList<string>? legend)
    {
        if (leftLogo is not null)
        {
            canvas.AddImageContained(leftLogo, .34, .2, .62, .62, "ARTRAC insignia");
        }
        if (rightLogo is not null)
        {
            canvas.AddImageContained(rightLogo, SlideWidth - .34 - .62, .2, .62, .62, "SDD insignia");
        }

        canvas.AddText(1.12, .2, 8.4, .42, title, 20, Navy, true, "l");
        canvas.AddText(1.12, .6, 8.4, .28, subtitle, 11, Muted, false, "l");
        if (legend is { Count: > 0 })
        {
            RenderLegend(canvas, legend, null, SlideWidth - 1.12, .6, alignRight: true);
        }
        canvas.AddLine(Margin, .98, SlideWidth - Margin, .98, Navy, 1.5);
    }

    private static void RenderLegend(SlideCanvas canvas, IReadOnlyList<string> categories, IReadOnlyDictionary<string, int>? counts, double anchorX, double y, bool alignRight)
    {
        var items = categories.Select(name => counts is not null && counts.TryGetValue(name, out var count) ? $"{name} {count}" : name).ToList();
        var widths = items.Select(text => .16 + TextWidth(text, 10) + .2).ToList();
        var x = alignRight ? anchorX - widths.Sum() : anchorX;
        for (var index = 0; index < items.Count; index++)
        {
            canvas.AddEllipse(x, y + .085, .1, .1, ColorOf(categories[index]));
            canvas.AddText(x + .14, y, widths[index], .28, items[index], 10, Muted, false, "l");
            x += widths[index];
        }
    }

    private static void RenderFooter(SlideCanvas canvas, string text, int page, DateOnly generatedOn)
    {
        canvas.AddLine(Margin, 6.98, SlideWidth - Margin, 6.98, Rule, .5);
        canvas.AddText(Margin, 7.0, FooterTextWidth, .4, text, FooterSize, Faint, false, "l", anchor: "t", wrap: true);
        canvas.AddText(SlideWidth - Margin - 3.2, 7.0, 2.6, .25, $"Generated by PRISM ERP on {generatedOn:d MMM yyyy}", 9, Faint, false, "r", anchor: "t");
        canvas.AddText(SlideWidth - Margin - .5, 7.0, .5, .25, page.ToString(CultureInfo.InvariantCulture), 9, Faint, false, "r", anchor: "t");
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

    private static string ShortOf(string code) => StageShort.TryGetValue(code, out var label) ? label : code;

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

    // Aptos averages a little under half an em per character in these names; .5 keeps the estimate on the safe side.
    private const double CharacterEm = .5;

    private static double TextWidth(string text, double points) => text.Length * points / 72d * CharacterEm;

    /// <summary>How many lines a name takes at a width, wrapping at spaces (a long word is broken).</summary>
    public static int WrapCount(string text, double width, double points)
    {
        var perLine = Math.Max(8, (int)Math.Floor(width / (points / 72d * CharacterEm)));
        var lines = 1;
        var used = 0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var length = word.Length;
            if (used == 0)
            {
                lines += (length - 1) / perLine;
                used = length % perLine == 0 ? perLine : length % perLine;
                continue;
            }

            if (used + 1 + length <= perLine)
            {
                used += 1 + length;
            }
            else
            {
                lines++;
                lines += (length - 1) / perLine;
                used = length % perLine == 0 ? perLine : length % perLine;
            }
        }

        return lines;
    }
    // END SECTION

    // SECTION: Slide canvas (shapes as DrawingML)
    private sealed class SlideCanvas
    {
        private readonly SlidePart _slidePart;
        private readonly List<string> _elements = new();
        private int _nextId = 2;

        public SlideCanvas(SlidePart slidePart) => _slidePart = slidePart;

        public void AddRect(double x, double y, double width, double height, string fill, bool radius = false) =>
            AddShape(x, y, width, height, fill, radius ? "roundRect" : "rect", null);

        public void AddEllipse(double x, double y, double width, double height, string fill) =>
            AddShape(x, y, width, height, fill, "ellipse", null);

        public void AddText(double x, double y, double width, double height, string text, double size, string color, bool bold, string align, string anchor = "ctr", bool wrap = true)
        {
            var body = $"""
<p:txBody><a:bodyPr wrap="{(wrap ? "square" : "none")}" lIns="0" rIns="0" tIns="0" bIns="0" anchor="{anchor}"><a:noAutofit/></a:bodyPr><a:lstStyle/><a:p><a:pPr algn="{align}"/><a:r><a:rPr lang="en-IN" sz="{(int)Math.Round(size * 100)}" b="{(bold ? 1 : 0)}"><a:solidFill><a:srgbClr val="{color}"/></a:solidFill><a:latin typeface="Aptos"/></a:rPr><a:t xml:space="preserve">{Escape(text)}</a:t></a:r></a:p></p:txBody>
""";
            AddShape(x, y, width, height, null, "rect", body);
        }

        /// <summary>Several paragraphs in one top-anchored, wrapping box, each with its own size and colour.</summary>
        public void AddParagraphs(double x, double y, double width, double height, IReadOnlyList<(string Text, double Size, string Color)> paragraphs)
        {
            var runs = string.Join(string.Empty, paragraphs.Select(paragraph => $"<a:p><a:pPr algn=\"l\"/><a:r><a:rPr lang=\"en-IN\" sz=\"{(int)Math.Round(paragraph.Size * 100)}\" b=\"0\"><a:solidFill><a:srgbClr val=\"{paragraph.Color}\"/></a:solidFill><a:latin typeface=\"Aptos\"/></a:rPr><a:t xml:space=\"preserve\">{Escape(paragraph.Text)}</a:t></a:r></a:p>"));
            AddShape(x, y, width, height, null, "rect", $"<p:txBody><a:bodyPr wrap=\"square\" lIns=\"0\" rIns=\"0\" tIns=\"0\" bIns=\"0\" anchor=\"t\"><a:noAutofit/></a:bodyPr><a:lstStyle/>{runs}</p:txBody>");
        }

        public void AddLine(double x1, double y1, double x2, double y2, string color, double points)
        {
            var id = _nextId++;
            _elements.Add($"""
<p:cxnSp><p:nvCxnSpPr><p:cNvPr id="{id}" name="Line {id}"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr><p:spPr><a:xfrm><a:off x="{Emu(x1)}" y="{Emu(y1)}"/><a:ext cx="{Emu(Math.Max(0, x2 - x1))}" cy="{Emu(Math.Max(0, y2 - y1))}"/></a:xfrm><a:prstGeom prst="line"><a:avLst/></a:prstGeom><a:ln w="{(long)Math.Round(points * 12700)}"><a:solidFill><a:srgbClr val="{color}"/></a:solidFill></a:ln></p:spPr></p:cxnSp>
""");
        }

        /// <summary>A PNG fitted inside the box, keeping its proportions and centred.</summary>
        public void AddImageContained(byte[] png, double x, double y, double width, double height, string name)
        {
            var (pixelWidth, pixelHeight) = PngSize(png);
            var scale = pixelWidth > 0 && pixelHeight > 0 ? Math.Min(width / pixelWidth, height / pixelHeight) : 0;
            var drawWidth = scale > 0 ? pixelWidth * scale : width;
            var drawHeight = scale > 0 ? pixelHeight * scale : height;
            var imagePart = _slidePart.AddImagePart(ImagePartType.Png);
            using (var data = new MemoryStream(png, writable: false))
            {
                imagePart.FeedData(data);
            }

            var id = _nextId++;
            _elements.Add($"""
<p:pic><p:nvPicPr><p:cNvPr id="{id}" name="{Escape(name)}" descr="{Escape(name)}"/><p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="{_slidePart.GetIdOfPart(imagePart)}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x="{Emu(x + (width - drawWidth) / 2)}" y="{Emu(y + (height - drawHeight) / 2)}"/><a:ext cx="{Emu(drawWidth)}" cy="{Emu(drawHeight)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>
""");
        }

        public void Commit()
        {
            var xml = $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>{string.Join(string.Empty, _elements)}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>
""";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            _slidePart.FeedData(stream);
        }

        private void AddShape(double x, double y, double width, double height, string? fill, string geometry, string? body)
        {
            var id = _nextId++;
            var fillXml = fill is null ? "<a:noFill/>" : $"<a:solidFill><a:srgbClr val=\"{fill}\"/></a:solidFill>";
            var adjust = geometry == "roundRect" ? "<a:gd name=\"adj\" fmla=\"val 6000\"/>" : string.Empty;
            _elements.Add($"""
<p:sp><p:nvSpPr><p:cNvPr id="{id}" name="Shape {id}"/><p:cNvSpPr{(body is null ? string.Empty : " txBox=\"1\"")}/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(Math.Max(0, width))}" cy="{Emu(Math.Max(0, height))}"/></a:xfrm><a:prstGeom prst="{geometry}"><a:avLst>{adjust}</a:avLst></a:prstGeom>{fillXml}<a:ln><a:noFill/></a:ln></p:spPr>{body ?? string.Empty}</p:sp>
""");
        }

        private static (int Width, int Height) PngSize(byte[] png) =>
            png.Length >= 24 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G'
                ? ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23])
                : (0, 0);

        private static long Emu(double inches) => (long)Math.Round(inches * 914400d);
        private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
    }
    // END SECTION
}
