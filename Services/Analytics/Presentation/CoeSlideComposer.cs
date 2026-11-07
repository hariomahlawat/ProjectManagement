using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using ProjectManagement.Services.Presentation;

namespace ProjectManagement.Services.Analytics.Presentation;

public interface ICoeSlideComposer
{
    byte[] Compose(CoeSlidesData data);
}

/// <summary>
/// Draws the two CoE (AR/VR) slides on the shared PowerPoint canvas, in the ARPP slide style: a
/// white page, the formation crest and SDD insignia either side of the title over a navy rule, then
/// (1) the projects in each category as side-by-side columns, every name in full, and (2) the AR/VR
/// summary as stat cards over every completed project and its year. A list too long for one slide
/// carries on to continuation slides. Built from a bundled template, so no network access.
/// </summary>
public sealed class CoeSlideComposer : ICoeSlideComposer
{
    // SECTION: Slide geometry and palette
    private const double Left = .45;
    private const double Right = 12.88;
    private const double ContentWidth = Right - Left;
    private const double SlideWidth = 13.333333;
    private const double SlideHeight = 7.5;
    private const double BodyTop = 1.32;
    private const double BodyBottom = 6.88;

    private const string Navy = "1F2A44";
    private const string Blue = "3B6FD8";
    private const string Green = "2E9152";
    private const string Orange = "D07A24";
    private const string Red = "C83B3B";
    private const string Text = "1F2937";
    private const string Muted = "6B7280";
    private const string HeaderTint = "E9EEF6";
    private const string CardTint = "F1F4F8";
    private const string Rule = "E3E8EF";

    // Average Aptos character width as a share of the font size (a little generous, so text never overflows its box).
    private const double CharacterWidthEm = .47;
    // Text boxes keep a 0.05 in inset each side.
    private const double TextInset = .1;
    // END SECTION

    private readonly string _templatePath;
    private readonly string? _leftLogoPath;
    private readonly string? _rightLogoPath;

    public CoeSlideComposer(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        // The FFC deck's widescreen template: a blank master, shipped with the app.
        _templatePath = Path.Combine(environment.ContentRootPath, "Resources", "FfcPresentation", "FfcPortfolioTemplate.pptx");

        // The briefing decks' two insignia: the formation crest on the left, the SDD insignia on the right.
        var webRootPath = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;
        var left = Path.Combine(webRootPath, "img", "logos", "artrac.png");
        var right = Path.Combine(webRootPath, "img", "logos", "sdd.png");
        _leftLogoPath = File.Exists(left) ? left : null;
        _rightLogoPath = File.Exists(right) ? right : null;
    }

    public byte[] Compose(CoeSlidesData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!File.Exists(_templatePath))
        {
            throw new FileNotFoundException("The PowerPoint template was not found.", _templatePath);
        }

        var logos = new Logos(ReadAsset(_leftLogoPath), ReadAsset(_rightLogoPath));
        var renderers = ProjectSlides(data, logos).Concat(SummarySlides(data, logos)).ToList();

        var templateBytes = File.ReadAllBytes(_templatePath);
        using var stream = new MemoryStream(templateBytes.Length + 200_000);
        stream.Write(templateBytes, 0, templateBytes.Length);
        stream.Position = 0;

        using (var document = PresentationDocument.Open(stream, true))
        {
            var presentationPart = document.PresentationPart
                ?? throw new InvalidOperationException("The PowerPoint template has no presentation part.");
            var layoutPart = PptxTemplate.FindBlankLayout(presentationPart)
                ?? throw new InvalidOperationException("The PowerPoint template has no slide layout.");

            PptxTemplate.RemoveTemplateSlides(presentationPart);
            var slideIdList = presentationPart.Presentation.SlideIdList;
            if (slideIdList is null)
            {
                slideIdList = new SlideIdList();
                presentationPart.Presentation.Append(slideIdList);
            }

            uint nextSlideId = 256;
            for (var index = 0; index < renderers.Count; index++)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.AddPart(layoutPart);
                var canvas = new PptxSlideCanvas(slidePart);
                renderers[index](canvas, index + 1);
                canvas.Commit();
                slideIdList.Append(new SlideId { Id = nextSlideId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }

            presentationPart.Presentation.Save();
            document.PackageProperties.Title = "CoE (AR/VR) projects";
            document.PackageProperties.Subject = "CoE (AR/VR) projects and AR/VR summary generated by PRISM ERP";
            document.PackageProperties.Creator = "Simulator Development Division";
            document.PackageProperties.LastModifiedBy = "PRISM ERP";
            document.PackageProperties.Modified = DateTime.UtcNow;
        }

        return stream.ToArray();
    }

    // SECTION: Slide 1 — CoE projects by category: one column per category, every name in full
    private const double ColumnHeaderHeight = .36;
    private const double CategoryGap = .2;
    // Below the smallest size the rows no longer fit; they carry on to continuation slides at a readable size instead.
    private const double MinimumFontSize = 7.5;
    private const double ContinuationFontSize = 10;

    /// <summary>Slide 1, plus continuation slides when a category's list will not fit even at the smallest type size.</summary>
    private static IEnumerable<Action<PptxSlideCanvas, int>> ProjectSlides(CoeSlidesData data, Logos logos)
    {
        if (data.Columns.Count == 0)
        {
            yield return (canvas, number) => RenderProjectsPage(canvas, data, logos, number, Array.Empty<IReadOnlyList<RowLayout>>(), MinimumFontSize, continued: false);
            yield break;
        }

        var width = CategoryColumnWidth(data.Columns.Count);
        var available = BodyBottom - CategoryListTop;

        // One type size for the whole slide: the largest that lets the longest column fit.
        var layouts = data.Columns.Select(column => new ColumnLayout(column, width)).ToList();
        var fontSize = FittingFontSize(layouts, available, 12.5);
        if (layouts.All(layout => layout.Height(fontSize) <= available))
        {
            var rows = layouts.Select(layout => (IReadOnlyList<RowLayout>)layout.Rows(fontSize).ToList()).ToList();
            yield return (canvas, number) => RenderProjectsPage(canvas, data, logos, number, rows, fontSize, continued: false);
            yield break;
        }

        // Too many to fit: each column carries its remaining rows onto the next slide, under the same headings.
        var chunks = layouts.Select(layout => Chunk(layout.Rows(ContinuationFontSize).ToList(), available)).ToList();
        var pageCount = chunks.Max(column => column.Count);
        for (var page = 0; page < pageCount; page++)
        {
            var pageIndex = page;
            var rows = chunks
                .Select(column => pageIndex < column.Count ? column[pageIndex] : (IReadOnlyList<RowLayout>)Array.Empty<RowLayout>())
                .ToList();
            yield return (canvas, number) => RenderProjectsPage(canvas, data, logos, number, rows, ContinuationFontSize, continued: pageIndex > 0);
        }
    }

    private static double CategoryListTop => BodyTop + ColumnHeaderHeight + .08;

    private static double CategoryColumnWidth(int columnCount) =>
        (ContentWidth - (CategoryGap * (columnCount - 1))) / columnCount;

    private static void RenderProjectsPage(
        PptxSlideCanvas canvas,
        CoeSlidesData data,
        Logos logos,
        int number,
        IReadOnlyList<IReadOnlyList<RowLayout>> columnRows,
        double fontSize,
        bool continued)
    {
        AddHeader(canvas, logos,
            continued ? "CoE (AR/VR) projects by category (continued)" : "CoE (AR/VR) projects by category",
            $"as of {Date(data.AsOf)} · {data.OngoingCount} ongoing, {data.CompletedCount} completed · stage today; PDC for projects in development");

        var notes = new List<string>();
        if (data.CancelledLeftOut.Count > 0)
        {
            notes.Add($"Cancelled: {string.Join(", ", data.CancelledLeftOut)}");
        }
        if (data.Columns.Any(column => column.Projects.Any(project => project.PdcOverdue)))
        {
            notes.Add("PDC in red: overdue");
        }
        AddFooter(canvas, string.Join(" · ", notes), number);

        if (data.Columns.Count == 0)
        {
            canvas.AddText(Left, 3.4, ContentWidth, .5, "No ongoing or completed CoE projects yet.", 16, Muted, false, "ctr");
            return;
        }

        var width = CategoryColumnWidth(data.Columns.Count);
        for (var index = 0; index < data.Columns.Count; index++)
        {
            var column = data.Columns[index];
            var x = Left + (index * (width + CategoryGap));
            canvas.AddRect(x, BodyTop, width, ColumnHeaderHeight, HeaderTint);
            canvas.AddText(x + .06, BodyTop, width - .7, ColumnHeaderHeight, column.Name, 12, Navy, true, "l");
            canvas.AddText(x + width - .62, BodyTop, .56, ColumnHeaderHeight, column.Projects.Count.ToString(CultureInfo.InvariantCulture), 12, Navy, true, "r");

            DrawRows(canvas, columnRows[index], x, CategoryListTop, width, fontSize);
        }
    }

    /// <summary>One list column: a dot, the name (wrapping, its PDC under it) and the tag on the first line's right.</summary>
    private static void DrawRows(PptxSlideCanvas canvas, IEnumerable<RowLayout> rows, double x, double top, double width, double fontSize)
    {
        var y = top;
        foreach (var row in rows)
        {
            var project = row.Project;
            var dot = project.IsCompleted ? Green : Blue;
            canvas.AddEllipse(x + .07, y + row.Padding + (row.LineHeight / 2) - .045, .09, .09, dot);
            // The name and its PDC share one top-anchored box, so the PDC follows the name however it wraps.
            var paragraphs = new List<PptxParagraph> { new(project.Name, fontSize, Text) };
            if (project.Pdc is not null)
            {
                paragraphs.Add(new PptxParagraph(project.Pdc, PdcFontSize(fontSize), project.PdcOverdue ? Red : Muted));
            }
            canvas.AddParagraphs(x + .2, y + row.Padding, row.NameWidth, row.Height - (row.Padding * 2), paragraphs);
            canvas.AddText(x + width - row.TagWidth, y + row.Padding - .02, row.TagWidth, row.LineHeight, project.Tag, fontSize - 1, TagColor(project), false, "r");

            y += row.Height;
            canvas.AddLine(x + .2, y - .01, x + width, y - .01, Rule, .5);
        }
    }

    /// <summary>The largest type size (from <paramref name="start"/> down to the minimum) at which every column fits the height.</summary>
    private static double FittingFontSize(IReadOnlyList<ColumnLayout> layouts, double available, double start)
    {
        var fontSize = start;
        while (fontSize > MinimumFontSize && layouts.Any(layout => layout.Height(fontSize) > available))
        {
            fontSize -= .5;
        }

        return fontSize;
    }

    /// <summary>Splits rows into runs that each fit the height (a row taller than the height still gets a run of its own).</summary>
    internal static List<IReadOnlyList<RowLayout>> Chunk(IReadOnlyList<RowLayout> rows, double available)
    {
        var chunks = new List<IReadOnlyList<RowLayout>>();
        var current = new List<RowLayout>();
        var used = 0d;
        foreach (var row in rows)
        {
            if (current.Count > 0 && used + row.Height > available)
            {
                chunks.Add(current);
                current = new List<RowLayout>();
                used = 0;
            }

            current.Add(row);
            used += row.Height;
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    private static string TagColor(CoeSlideProject project) => project.IsCompleted ? Green : Muted;

    private static double PdcFontSize(double fontSize) => fontSize - 1;

    private static double LineHeightOf(double fontSize) => fontSize * 1.22 / 72;

    /// <summary>A row's height: the wrapped name, then every wrapped line of its PDC at the PDC's own size.</summary>
    internal static double RowHeight(CoeSlideProject project, double nameWidth, double fontSize, double padding = .05)
    {
        var nameLines = LineCount(project.Name, nameWidth - TextInset, fontSize);
        var height = (padding * 2) + (nameLines * LineHeightOf(fontSize));
        if (project.Pdc is not null)
        {
            var pdcSize = PdcFontSize(fontSize);
            height += LineCount(project.Pdc, nameWidth - TextInset, pdcSize) * LineHeightOf(pdcSize);
        }

        return height;
    }

    /// <summary>Row heights for one column at a given type size: names wrap, the stage tag sits on the first line.</summary>
    internal sealed class ColumnLayout
    {
        private readonly double _width;

        public ColumnLayout(CoeSlideColumn column, double width)
        {
            Column = column;
            _width = width;
        }

        public CoeSlideColumn Column { get; }

        public double Height(double fontSize) => Rows(fontSize).Sum(row => row.Height);

        public IEnumerable<RowLayout> Rows(double fontSize)
        {
            const double padding = .05;
            // The widest tag in this column sets the tag slot, so every name wraps at the same edge.
            // Tags are short words in a wide face ("Completed"): measure them generously so one never wraps.
            var tagWidth = (Column.Projects.Max(project => TextWidth(project.Tag, fontSize - 1)) * 1.25) + TextInset + .08;
            var nameWidth = _width - .2 - tagWidth;
            foreach (var project in Column.Projects)
            {
                var height = RowHeight(project, nameWidth, fontSize, padding);
                yield return new RowLayout(project, LineHeightOf(fontSize), padding, nameWidth, tagWidth, height);
            }
        }
    }

    internal sealed record RowLayout(CoeSlideProject Project, double LineHeight, double Padding, double NameWidth, double TagWidth, double Height);
    // END SECTION

    // SECTION: Slide 2 — AR/VR at a glance: stat cards, then every completed AR/VR project with its year
    private const double CardHeight = 1.42;
    private const double ListColumnGap = .3;
    private const int MaxListColumns = 4;

    private static double SummaryListHeaderTop => BodyTop + CardHeight + .26;

    private static double ListColumnWidth(int columnCount) =>
        (ContentWidth - (ListColumnGap * (columnCount - 1))) / columnCount;

    /// <summary>Slide 2, plus continuation slides for the completed list when it will not fit even at the smallest type size.</summary>
    private static IEnumerable<Action<PptxSlideCanvas, int>> SummarySlides(CoeSlidesData data, Logos logos)
    {
        var projects = data.CompletedProjects;
        var firstListTop = SummaryListHeaderTop + ColumnHeaderHeight + .08;
        if (projects.Count == 0)
        {
            yield return (canvas, number) => RenderSummaryPage(canvas, data, logos, number, Array.Empty<IReadOnlyList<RowLayout>>(), 1, MinimumFontSize, continued: false);
            yield break;
        }

        // Balanced columns, at the largest size that fits.
        var columnCount = Math.Clamp((int)Math.Ceiling(projects.Count / 7d), 1, MaxListColumns);
        columnCount = Math.Max(columnCount, Math.Min(3, projects.Count));
        var columnWidth = ListColumnWidth(columnCount);
        var perColumn = (int)Math.Ceiling(projects.Count / (double)columnCount);
        var layouts = Enumerable.Range(0, columnCount)
            .Select(index => projects.Skip(index * perColumn).Take(perColumn).ToList())
            .Where(column => column.Count > 0)
            .Select(column => new ColumnLayout(new CoeSlideColumn(string.Empty, column), columnWidth))
            .ToList();
        var available = BodyBottom - firstListTop;
        var fontSize = FittingFontSize(layouts, available, 12);
        if (layouts.All(layout => layout.Height(fontSize) <= available))
        {
            var rows = layouts.Select(layout => (IReadOnlyList<RowLayout>)layout.Rows(fontSize).ToList()).ToList();
            yield return (canvas, number) => RenderSummaryPage(canvas, data, logos, number, rows, columnCount, fontSize, continued: false);
            yield break;
        }

        // Too many to fit: fill four columns down, then carry on to continuation slides that give the list the whole page.
        var allRows = new ColumnLayout(new CoeSlideColumn(string.Empty, projects), ListColumnWidth(MaxListColumns))
            .Rows(ContinuationFontSize)
            .ToList();
        var next = 0;
        var first = true;
        while (next < allRows.Count)
        {
            var pageHeight = first ? available : BodyBottom - (BodyTop + ColumnHeaderHeight + .08);
            var columns = new List<IReadOnlyList<RowLayout>>();
            for (var column = 0; column < MaxListColumns && next < allRows.Count; column++)
            {
                var rows = new List<RowLayout>();
                var used = 0d;
                while (next < allRows.Count && (rows.Count == 0 || used + allRows[next].Height <= pageHeight))
                {
                    used += allRows[next].Height;
                    rows.Add(allRows[next++]);
                }
                columns.Add(rows);
            }

            var continued = !first;
            yield return (canvas, number) => RenderSummaryPage(canvas, data, logos, number, columns, MaxListColumns, ContinuationFontSize, continued);
            first = false;
        }
    }

    private static void RenderSummaryPage(
        PptxSlideCanvas canvas,
        CoeSlidesData data,
        Logos logos,
        int number,
        IReadOnlyList<IReadOnlyList<RowLayout>> columns,
        int columnCount,
        double fontSize,
        bool continued)
    {
        AddHeader(canvas, logos,
            continued ? "AR/VR at a glance (continued)" : "AR/VR at a glance",
            $"as of {Date(data.AsOf)} · completed projects, training and proliferation, all years");
        AddFooter(canvas, data.Training is null && data.Proliferation is null
            ? "Training and proliferation figures are shown to their trackers' viewers only."
            : string.Empty, number);

        var listHeaderTop = BodyTop;
        if (!continued)
        {
            var cards = new List<StatCard>
            {
                new(data.CompletedTotal, "AR/VR projects completed", "in the CoE and outside it", Green)
            };
            if (data.Training is { } training)
            {
                cards.Add(new(training.People, "People trained", $"in {training.Courses} AR/VR course{(training.Courses == 1 ? string.Empty : "s")}", Blue));
            }
            if (data.Proliferation is { } proliferation)
            {
                cards.Add(new(proliferation.UnitsDelivered, "Units proliferated", $"to {proliferation.ReceivingUnits} receiving unit{(proliferation.ReceivingUnits == 1 ? string.Empty : "s")}", Orange));
            }

            // A three-column grid whatever the caller may see, so a card keeps its place and size.
            const double gap = .24;
            var cardWidth = (ContentWidth - (gap * 2)) / 3;
            for (var index = 0; index < cards.Count; index++)
            {
                RenderCard(canvas, cards[index], Left + (index * (cardWidth + gap)), cardWidth, CardHeight);
            }

            listHeaderTop = SummaryListHeaderTop;
        }

        // Every completed AR/VR project, newest first, in columns under one heading.
        canvas.AddRect(Left, listHeaderTop, ContentWidth, ColumnHeaderHeight, HeaderTint);
        canvas.AddText(Left + .06, listHeaderTop, ContentWidth - .8, ColumnHeaderHeight,
            continued ? "Completed AR/VR projects, with the year of completion (continued)" : "Completed AR/VR projects, with the year of completion",
            12, Navy, true, "l");
        canvas.AddText(Right - .62, listHeaderTop, .56, ColumnHeaderHeight, data.CompletedProjects.Count.ToString(CultureInfo.InvariantCulture), 12, Navy, true, "r");

        var listTop = listHeaderTop + ColumnHeaderHeight + .08;
        if (data.CompletedProjects.Count == 0)
        {
            canvas.AddText(Left, listTop + .3, ContentWidth, .4, "No AR/VR project has been completed yet.", 13, Muted, false, "l");
            return;
        }

        var columnWidth = ListColumnWidth(columnCount);
        for (var index = 0; index < columns.Count; index++)
        {
            DrawRows(canvas, columns[index], Left + (index * (columnWidth + ListColumnGap)), listTop, columnWidth, fontSize);
        }
    }

    private static void RenderCard(PptxSlideCanvas canvas, StatCard card, double x, double width, double height)
    {
        canvas.AddRect(x, BodyTop, width, height, CardTint);
        canvas.AddRect(x, BodyTop, .06, height, card.Accent);
        canvas.AddText(x + .22, BodyTop + .1, width - .3, .66, card.Total.ToString("N0", CultureInfo.InvariantCulture), 34, Navy, true, "l");
        canvas.AddText(x + .22, BodyTop + .78, width - .3, .3, card.Label, 13, Text, true, "l");
        canvas.AddText(x + .22, BodyTop + 1.06, width - .3, .26, card.Reach, 11, Muted, false, "l");
    }

    private sealed record StatCard(int Total, string Label, string Reach, string Accent);
    // END SECTION

    // SECTION: Shared slide furniture (the Briefing Studio header: crest left, SDD insignia right, navy rule)
    private sealed record Logos(byte[]? Left, byte[]? Right);

    private static void AddHeader(PptxSlideCanvas canvas, Logos logos, string title, string subtitle)
    {
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, "FFFFFF");

        var titleX = Left;
        if (logos.Left is { Length: > 0 })
        {
            canvas.AddImageContained(logos.Left, Left - .03, .27, .66, .66, "Formation insignia");
            titleX = Left + .78;
        }

        var titleRight = Right;
        if (logos.Right is { Length: > 0 })
        {
            // The SDD insignia is slender: a taller box balances it against the denser crest.
            canvas.AddImageContained(logos.Right, Right - .56, .2, .56, .8, "Division insignia");
            titleRight = Right - .7;
        }

        canvas.AddText(titleX, .25, titleRight - titleX, .5, title, 22, Navy, true, "l");
        canvas.AddText(titleX + .01, .72, titleRight - titleX, .3, subtitle, 11, Muted, false, "l");
        canvas.AddLine(Left, 1.12, Right, 1.12, Navy, 1.25);
    }

    private static void AddFooter(PptxSlideCanvas canvas, string note, int number)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            var size = 9.0;
            canvas.AddText(Left, 7.0, ContentWidth - .6, .26, Fit(note, ContentWidth - .6 - TextInset, size), size, Muted, false, "l");
        }

        canvas.AddText(Right - .5, 7.0, .5, .26, number.ToString(CultureInfo.InvariantCulture), 9, Muted, false, "r");
    }

    private static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static byte[]? ReadAsset(string? path) =>
        string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : File.ReadAllBytes(path);
    // END SECTION

    // SECTION: Text measuring (an estimate; generous, so wrapped text never runs past its box)
    internal static double TextWidth(string text, double fontSize) =>
        (text ?? string.Empty).Length * fontSize * CharacterWidthEm / 72;

    /// <summary>Lines the text takes when wrapped at word breaks inside the width.</summary>
    internal static int LineCount(string text, double widthInches, double fontSize)
    {
        var capacity = Math.Max(1, (int)(widthInches * 72 / (fontSize * CharacterWidthEm)));
        var lines = 1;
        var used = 0;
        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var length = word.Length;
            if (used == 0)
            {
                used = length;
            }
            else if (used + 1 + length <= capacity)
            {
                used += 1 + length;
            }
            else
            {
                lines++;
                used = length;
            }

            // A single word longer than the line breaks across lines.
            while (used > capacity)
            {
                lines++;
                used -= capacity;
            }
        }

        return lines;
    }

    /// <summary>Shortens one line of text to what fits at this size.</summary>
    internal static string Fit(string text, double widthInches, double fontSize)
    {
        var normalized = string.Join(" ", (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var maximum = Math.Max(4, (int)(widthInches * 72 / (fontSize * CharacterWidthEm)));
        return normalized.Length <= maximum ? normalized : normalized[..(maximum - 1)].TrimEnd() + "…";
    }
    // END SECTION
}
