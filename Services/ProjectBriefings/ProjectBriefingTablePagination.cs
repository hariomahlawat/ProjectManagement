using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

internal sealed record ProjectBriefingTableRowMeasure(
    string ProjectName,
    string PresentStage,
    string ExternalStatus,
    string CostRdText);

internal sealed record ProjectBriefingTablePage<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<double> RowHeights);

// The status table's columns for a deck: headers and widths (inches, 12.15 in across), shared by the
// pagination and the slide so both measure the same columns.
internal sealed record ProjectBriefingTableLayout(
    IReadOnlyList<string> Headers,
    IReadOnlyList<double> Widths,
    bool ShowsCostRd,
    bool ShowsProliferation)
{
    public double ProjectWidth => Widths[0];
    public double StageWidth => Widths[^2];
    public double StatusWidth => Widths[^1];
}

// The status table (docs/ux/briefing-slides-review.md, PR 3; owner, 17 Nov 2026): 12 pt text and
// up to 12 rows a slide, rows as tall as their text, pages of even length.
internal static class ProjectBriefingTablePagination
{
    public const double FontSize = 12.0;
    public const double HeaderFontSize = 11.0;
    public const double HeaderHeight = .40;
    // From the header row (1.06 in + .40 in) to above the footer rule (7.00 in), less the few
    // per cent PowerPoint adds to row heights over this estimate (measured on exported slides).
    public const double AvailableBodyHeight = 5.30;
    public const int MaximumRowsPerSlide = 12;
    public const int MaximumProjectLines = 2;
    public const int MaximumStatusLines = 3;
    private const double LineHeight = FontSize * 1.2 / 72d;
    private const double RowPadding = .10;

    // A column is shown only when it has something to show: the proliferation column is left out
    // of a deck where no project has a proliferation cost.
    public static ProjectBriefingTableLayout Layout(ProjectBriefingCostMode costMode, bool anyProliferationCost)
    {
        var showsCostRd = costMode is ProjectBriefingCostMode.CostRdOnly or ProjectBriefingCostMode.Both;
        var showsProliferation = anyProliferationCost
            && costMode is ProjectBriefingCostMode.ProliferationOnly or ProjectBriefingCostMode.Both;
        return (showsCostRd, showsProliferation) switch
        {
            (true, true) => new(new[] { "PROJECT", "COST (R&D)", "PROLIFERATION COST", "PRESENT STAGE", "STATUS" },
                new[] { 2.45, 1.55, 1.40, 1.70, 5.05 }, true, true),
            (true, false) => new(new[] { "PROJECT", "COST (R&D)", "PRESENT STAGE", "STATUS" },
                new[] { 2.75, 1.55, 1.75, 6.10 }, true, false),
            (false, true) => new(new[] { "PROJECT", "PROLIFERATION COST", "PRESENT STAGE", "STATUS" },
                new[] { 2.85, 1.40, 1.80, 6.10 }, false, true),
            _ => new(new[] { "PROJECT", "PRESENT STAGE", "STATUS" },
                new[] { 3.05, 1.95, 7.15 }, false, false)
        };
    }

    // Characters a line at 12 pt in a column of this width (cell insets .10 in each side).
    public static int CharactersPerLine(double width)
        => Math.Max(8, (int)Math.Floor((width - .20) * 72d / (FontSize * .50)));

    // The project name is bold, and bold letters are wider (measured on exported slides).
    public static int ProjectCharactersPerLine(double width)
        => Math.Max(8, (int)Math.Floor((width - .20) * 72d / (FontSize * .58)));

    public static IReadOnlyList<ProjectBriefingTablePage<T>> Paginate<T>(
        IReadOnlyList<T> items,
        ProjectBriefingTableLayout layout,
        Func<T, ProjectBriefingTableRowMeasure> measure)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(measure);

        if (items.Count == 0)
        {
            return Array.Empty<ProjectBriefingTablePage<T>>();
        }

        var measured = items
            .Select(item => new MeasuredItem<T>(item, EstimateRowHeight(measure(item), layout)))
            .ToList();

        // As few slides as the rows allow, then the rows spread evenly across them, so no slide
        // ends with two or three rows: the smallest page height (and row count) that still needs
        // no more slides.
        var pages = Fill(measured, MaximumRowsPerSlide, AvailableBodyHeight);
        var low = measured.Sum(row => row.Height) / pages.Count;
        var high = AvailableBodyHeight;
        for (var step = 0; step < 24; step++)
        {
            var middle = (low + high) / 2;
            if (Fill(measured, MaximumRowsPerSlide, middle).Count <= pages.Count)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        var balanced = Fill(measured, MaximumRowsPerSlide, high);
        if (balanced.Count == pages.Count)
        {
            pages = balanced;
        }

        return pages
            .Select(page => new ProjectBriefingTablePage<T>(
                page.Select(row => row.Item).ToArray(),
                page.Select(row => row.Height).ToArray()))
            .ToArray();
    }

    private static List<List<MeasuredItem<T>>> Fill<T>(IReadOnlyList<MeasuredItem<T>> measured, int rowsPerSlide, double pageHeight)
    {
        var pages = new List<List<MeasuredItem<T>>>();
        var current = new List<MeasuredItem<T>>();
        var currentHeight = 0d;
        foreach (var item in measured)
        {
            if (current.Count >= rowsPerSlide
                || (current.Count > 0 && currentHeight + item.Height > pageHeight + .0001))
            {
                pages.Add(current);
                current = new List<MeasuredItem<T>>();
                currentHeight = 0d;
            }

            current.Add(item);
            currentHeight += item.Height;
        }

        if (current.Count > 0)
        {
            pages.Add(current);
        }

        return pages;
    }

    public static ProjectBriefingTableRowMeasure Measure(
        string? projectName,
        string? presentStage,
        string? externalStatus,
        ProjectBriefingCostValue costRd)
        => new(
            Normalize(projectName, "Project"),
            Normalize(presentStage, "Not recorded"),
            Normalize(externalStatus, "No external status recorded"),
            CostText(costRd, "Not recorded", includeBasis: true));

    // A cost cell's text: the amount and its basis on one line ("₹4.2 Cr · IPA"). The slide and
    // the measure both use this, so they agree on its length.
    public static string CostText(ProjectBriefingCostValue value, string missing, bool includeBasis)
        => value.IsAvailable
            ? includeBasis && !string.IsNullOrWhiteSpace(value.BasisDisplay)
                ? $"{value.DisplayValue} · {value.BasisDisplay}"
                : value.DisplayValue
            : missing;

    // As tall as the row's longest cell: the name up to two lines, the status up to three (the
    // slide shortens longer ones), the stage up to two, the cost and its basis one, or two when the
    // pair is longer than the column.
    public static double EstimateRowHeight(ProjectBriefingTableRowMeasure row, ProjectBriefingTableLayout layout)
    {
        var projectLines = EstimateLines(row.ProjectName, ProjectCharactersPerLine(layout.ProjectWidth), MaximumProjectLines);
        var statusLines = EstimateLines(row.ExternalStatus, CharactersPerLine(layout.StatusWidth), MaximumStatusLines);
        var stageLines = EstimateLines(row.PresentStage, CharactersPerLine(layout.StageWidth), 2);
        var costLines = layout.ShowsCostRd ? EstimateLines(row.CostRdText, CharactersPerLine(layout.Widths[1]), 2) : 1;

        var lines = Math.Max(Math.Max(projectLines, statusLines), Math.Max(stageLines, costLines));
        return RowPadding + (lines * LineHeight);
    }

    private static int EstimateLines(string value, int charactersPerLine, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = 1;
        var currentLength = 0;

        foreach (var word in words)
        {
            var proposed = currentLength == 0 ? word.Length : currentLength + 1 + word.Length;
            if (proposed <= charactersPerLine)
            {
                currentLength = proposed;
                continue;
            }

            // The word starts a new line; a word longer than a line breaks inside itself.
            if (currentLength > 0)
            {
                lines++;
            }

            lines += (word.Length - 1) / charactersPerLine;
            currentLength = ((word.Length - 1) % charactersPerLine) + 1;
            if (lines >= maximum)
            {
                return maximum;
            }
        }

        return Math.Clamp(lines, 1, maximum);
    }

    // The text as it wraps in a cell (word by word, as EstimateLines measures it), cut after the
    // last whole word that fits in these many lines, with an ellipsis: the slide never shows a
    // line more than the row was measured for.
    public static string FitToLines(string? value, int charactersPerLine, int maximumLines)
    {
        var text = Normalize(value, string.Empty);
        if (EstimateLines(text, charactersPerLine, maximumLines + 1) <= maximumLines)
        {
            return text;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        foreach (var word in words)
        {
            kept.Add(word);
            if (EstimateLines(string.Join(' ', kept) + "…", charactersPerLine, maximumLines + 1) > maximumLines)
            {
                kept.RemoveAt(kept.Count - 1);
                break;
            }
        }

        // A first word longer than the lines allow is cut inside the word.
        return kept.Count == 0
            ? text[..Math.Max(1, (charactersPerLine * maximumLines) - 1)] + "…"
            : string.Join(' ', kept).TrimEnd(',', ';', ':', '.', '-', '·', ' ') + "…";
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record MeasuredItem<T>(T Item, double Height);
}
