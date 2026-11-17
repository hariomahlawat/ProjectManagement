using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{

    // The FFC Global Footprint slide (owner, 17 Nov 2026; docs/ux/briefing-slides-review.md): one
    // slide. The figures in a line under the title, the map as a band across the full width, and
    // below it a table with a column for each country (largest first, up to 16) and rows for
    // Installed, Delivered, Planned and Total.
    internal const int FfcMapBandPixelWidth = 2426;
    internal const int FfcMapBandPixelHeight = 640;
    internal const int FfcTableMaximumColumns = 16;
    private const double FfcLeft = .60;
    private const double FfcWidth = 12.13;
    private const double FfcFiguresTop = 1.10;
    private const double FfcBarTop = 1.58;
    private const double FfcMapTop = 1.72;
    private const double FfcMapHeight = FfcWidth * FfcMapBandPixelHeight / FfcMapBandPixelWidth;
    private const double FfcTableTop = FfcMapTop + FfcMapHeight + .12;
    private const double FfcLabelColumnWidth = 1.35;
    private const double FfcTotalColumnWidth = .85;

    private static void RenderFfcGlobalFootprint(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        AddProjectSlideHeader(
            canvas,
            Truncate(data.Title, 110),
            subtitle: null,
            variant: ProjectSlideHeaderVariant.FfcGlobalFootprint);

        if (data.Countries.Count == 0)
        {
            canvas.AddSubtleRoundedRect(FfcLeft, 2.55, FfcWidth, 3.92, canvas.Theme.Surface, canvas.Theme.Border, "FFC empty-state panel");
            canvas.AddText(1.10, 3.75, 11.12, .62, "No active FFC portfolio data is available.", 18, canvas.Theme.TextSecondary, true, "ctr", name: "FFC empty-state message");
            return;
        }

        RenderFfcFigures(canvas, data);
        RenderFfcStatusBar(canvas, data);

        canvas.AddRoundedRect(FfcLeft - .02, FfcMapTop - .02, FfcWidth + .04, FfcMapHeight + .04, canvas.Theme.Surface, canvas.Theme.Border, .02, "FFC map frame");
        if (data.MapImage.Length > 0)
        {
            canvas.AddImage(data.MapImage, "image/png", FfcLeft, FfcMapTop, FfcWidth, FfcMapHeight, "FFC global footprint map");
        }

        RenderFfcCountryTable(canvas, data);
    }

    // "10 countries · 29 projects · 42 units" on the left; the status key on the right.
    private static void RenderFfcFigures(SlideCanvas canvas, ProjectBriefingFfcGlobalFootprintData data)
    {
        var theme = canvas.Theme;
        RichTextRun Figure(int value) => new(value.ToString("N0", CultureInfo.InvariantCulture), 20, theme.TextPrimary, Bold: true);
        RichTextRun Label(string text) => new(text, 13, theme.TextSecondary);
        canvas.AddRichTextBox(
            FfcLeft,
            FfcFiguresTop,
            6.80,
            .42,
            new[]
            {
                new RichTextParagraph(new[]
                {
                    Figure(data.Summary.CountryCount), Label(" countries     "),
                    Figure(data.Summary.ProjectCount), Label(" projects     "),
                    Figure(data.Summary.TotalUnits), Label(" units")
                })
            },
            "FFC figures",
            verticalAnchor: "b",
            allowAutoFit: false,
            leftInset: 0,
            rightInset: 0,
            topInset: 0,
            bottomInset: .02);

        var planned = FfcPlannedColor(theme);
        RichTextRun Key(string color) => new("■ ", 13, color);
        RichTextRun Name(string text) => new(text, 12.5, theme.TextSecondary);
        RichTextRun Value(int value) => new(value.ToString("N0", CultureInfo.InvariantCulture) + "      ", 13, theme.TextPrimary, Bold: true);
        canvas.AddRichTextBox(
            FfcLeft + 6.80,
            FfcFiguresTop,
            FfcWidth - 6.80,
            .42,
            new[]
            {
                new RichTextParagraph(new[]
                {
                    Key(theme.Positive), Name("Installed "), Value(data.Summary.InstalledUnits),
                    Key(theme.Accent), Name("Delivered, awaiting installation "), Value(data.Summary.DeliveredNotInstalledUnits),
                    Key(planned), Name("Planned "), new RichTextRun(data.Summary.PlannedUnits.ToString("N0", CultureInfo.InvariantCulture), 13, theme.TextPrimary, Bold: true)
                }, Align: "r")
            },
            "FFC status key",
            verticalAnchor: "b",
            allowAutoFit: false,
            leftInset: 0,
            rightInset: 0,
            topInset: 0,
            bottomInset: .03);
    }

    // The whole quantity as one bar, installed, delivered and planned, across the slide.
    private static void RenderFfcStatusBar(SlideCanvas canvas, ProjectBriefingFfcGlobalFootprintData data)
    {
        const double height = .07;
        var total = Math.Max(1, data.Summary.TotalUnits);
        var segments = new[]
        {
            ("installed", data.Summary.InstalledUnits, canvas.Theme.Positive),
            ("delivered", data.Summary.DeliveredNotInstalledUnits, canvas.Theme.Accent),
            ("planned", data.Summary.PlannedUnits, FfcPlannedColor(canvas.Theme))
        };
        var x = FfcLeft;
        foreach (var (name, value, color) in segments)
        {
            if (value <= 0)
            {
                continue;
            }

            var width = FfcWidth * value / total;
            canvas.AddRect(x, FfcBarTop, width, height, color, null, 0, $"FFC {name} share");
            x += width;
        }
    }

    // A column for each country, largest first; past 16 the smallest share an "Others" column.
    private static void RenderFfcCountryTable(SlideCanvas canvas, ProjectBriefingFfcGlobalFootprintData data)
    {
        var theme = canvas.Theme;
        var columns = FfcTableColumns(data.Countries);
        var countryWidth = (FfcWidth - FfcLabelColumnWidth - FfcTotalColumnWidth) / columns.Count;
        var nameSize = FfcHeaderFontSize(columns.Select(column => column.Name), countryWidth);
        var valueSize = columns.Count <= 12 ? 14.0 : 12.5;
        var planned = FfcPlannedColor(theme);

        NativeTableCell Header(string text, string align) => new(text, nameSize, theme.TextOnAccent, true, align, theme.TableHeader, LeftMargin: .04, RightMargin: .04);
        NativeTableCell Number(int value, double size, bool bold, string fill, string color)
            => new(value > 0 ? value.ToString("N0", CultureInfo.InvariantCulture) : "–", size, value > 0 ? color : theme.TextMuted, bold, "ctr", fill, LeftMargin: .03, RightMargin: .03);

        var rows = new List<IReadOnlyList<NativeTableCell>>
        {
            new[] { Header("", "l") }
                .Concat(columns.Select(column => Header(column.Name, "ctr")))
                .Append(Header("Total", "ctr"))
                .ToArray()
        };
        var lines = new (string Label, string Color, Func<FfcTableColumn, int> Value)[]
        {
            ("Installed", theme.Positive, column => column.Installed),
            ("Delivered", theme.Accent, column => column.Delivered),
            // Planned's own grey is too faint as text on a light table; its name takes the secondary text colour.
            ("Planned", theme.TextSecondary, column => column.Planned)
        };
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var fill = index % 2 == 0 ? theme.TableRow : theme.TableAlternateRow;
            rows.Add(new[] { new NativeTableCell(line.Label, 12, line.Color, true, "l", fill, LeftMargin: .10) }
                .Concat(columns.Select(column => Number(line.Value(column), valueSize, false, fill, theme.TextPrimary)))
                .Append(Number(columns.Sum(line.Value), valueSize, true, fill, theme.TextPrimary))
                .ToArray());
        }

        var totalBorders = new NativeTableBorders(TopColor: theme.Divider, TopWidth: 1.0);
        rows.Add(new[] { new NativeTableCell("Total", 12.5, theme.TextPrimary, true, "l", theme.SurfaceMuted, totalBorders, LeftMargin: .10) }
            .Concat(columns.Select(column => Number(column.Total, valueSize + 1, true, theme.SurfaceMuted, theme.TextPrimary) with { Borders = totalBorders }))
            .Append(Number(columns.Sum(column => column.Total), valueSize + 1, true, theme.SurfaceMuted, theme.TextPrimary) with { Borders = totalBorders })
            .ToArray());

        var widths = new[] { FfcLabelColumnWidth }
            .Concat(Enumerable.Repeat(countryWidth, columns.Count))
            .Append(FfcTotalColumnWidth)
            .ToArray();
        var bodyHeight = (6.90 - FfcTableTop - .50) / 4d;
        canvas.AddNativeTable(FfcLeft, FfcTableTop, widths, new[] { .50, bodyHeight, bodyHeight, bodyHeight, bodyHeight }, rows, "FFC country table");
    }

    private sealed record FfcTableColumn(string Name, int Installed, int Delivered, int Planned)
    {
        public int Total => Installed + Delivered + Planned;
    }

    private static IReadOnlyList<FfcTableColumn> FfcTableColumns(IReadOnlyList<ProjectBriefingFfcCountryData> countries)
    {
        var ordered = countries
            .OrderByDescending(country => country.TotalUnits)
            .ThenBy(country => country.CountryName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FfcTableColumn Column(ProjectBriefingFfcCountryData country) => new(
            string.IsNullOrWhiteSpace(country.CountryName)
                ? country.IsoCode
                : ProjectManagement.Services.Ffc.Presentation.FfcPresentationMapRenderer.ShortCountryName(country.CountryName),
            country.InstalledUnits,
            country.DeliveredNotInstalledUnits,
            country.PlannedUnits);
        if (ordered.Count <= FfcTableMaximumColumns)
        {
            return ordered.Select(Column).ToArray();
        }

        var shown = ordered.Take(FfcTableMaximumColumns - 1).Select(Column).ToList();
        var rest = ordered.Skip(FfcTableMaximumColumns - 1).ToList();
        shown.Add(new FfcTableColumn(
            $"Others ({rest.Count.ToString(CultureInfo.InvariantCulture)})",
            rest.Sum(country => country.InstalledUnits),
            rest.Sum(country => country.DeliveredNotInstalledUnits),
            rest.Sum(country => country.PlannedUnits)));
        return shown;
    }

    // The country names' size: 11 pt, or smaller (to 8.5 pt) so the longest word fits its column.
    private static double FfcHeaderFontSize(IEnumerable<string> names, double columnWidth)
    {
        var longestWord = names
            .SelectMany(name => name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .DefaultIfEmpty(string.Empty)
            .Max(word => word.Length);
        var usable = (columnWidth - .10) * 72d;
        var fits = longestWord == 0 ? 11d : usable / (longestWord * .56);
        return Math.Round(Math.Clamp(fits, 8.5, 11d) * 2, MidpointRounding.ToZero) / 2;
    }

    private static void RenderFfcCountryWiseBreakdownSlide(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data,
        IReadOnlyList<ProjectBriefingFfcCountryData> countries,
        int pageNumber,
        int pageCount)
    {
        var isContinuation = pageNumber > 1;
        var title = isContinuation
            ? $"{ProjectBriefingFfcGlobalFootprintOptions.BreakdownTitle} — Continued"
            : ProjectBriefingFfcGlobalFootprintOptions.BreakdownTitle;
        var subtitle = pageCount > 1
            ? $"Complete authoritative country-wise quantity position · Page {pageNumber} of {pageCount}"
            : "Complete authoritative country-wise quantity position";
        AddProjectSlideHeader(
            canvas,
            title,
            subtitle,
            ProjectSlideHeaderVariant.FfcGlobalFootprint);

        var rows = new List<IReadOnlyList<NativeTableCell>>
        {
            new[]
            {
                Cell("COUNTRY", 10.0, canvas.Theme.TextOnAccent, true, "l", canvas.Theme.TableHeader),
                Cell("PROJECTS", 9.6, canvas.Theme.TextOnAccent, true, "r", canvas.Theme.TableHeader),
                Cell("INSTALLED", 9.6, canvas.Theme.TextOnAccent, true, "r", canvas.Theme.TableHeader),
                Cell("DELIVERED, AWAITING\nINSTALLATION", 9.0, canvas.Theme.TextOnAccent, true, "r", canvas.Theme.TableHeader),
                Cell("PLANNED", 9.6, canvas.Theme.TextOnAccent, true, "r", canvas.Theme.TableHeader),
                Cell("TOTAL", 9.8, canvas.Theme.TextOnAccent, true, "r", canvas.Theme.TableHeader)
            }
        };

        for (var index = 0; index < countries.Count; index++)
        {
            var country = countries[index];
            var fill = index % 2 == 0
                ? canvas.Theme.TableRow
                : canvas.Theme.TableAlternateRow;
            rows.Add(new[]
            {
                Cell($"{country.IsoCode}   {country.CountryName}", 10.4, canvas.Theme.TextPrimary, true, "l", fill),
                Cell(country.ProjectCount.ToString("N0", CultureInfo.InvariantCulture), 10.4, canvas.Theme.TextPrimary, false, "r", fill),
                Cell(country.InstalledUnits.ToString("N0", CultureInfo.InvariantCulture), 10.4, canvas.Theme.TextPrimary, false, "r", fill),
                Cell(country.DeliveredNotInstalledUnits.ToString("N0", CultureInfo.InvariantCulture), 10.4, canvas.Theme.TextPrimary, false, "r", fill),
                Cell(country.PlannedUnits.ToString("N0", CultureInfo.InvariantCulture), 10.4, canvas.Theme.TextPrimary, false, "r", fill),
                Cell(country.TotalUnits.ToString("N0", CultureInfo.InvariantCulture), 10.8, canvas.Theme.TextPrimary, true, "r", canvas.Theme.AccentSoft)
            });
        }

        const double headerHeight = .52;
        var bodyHeight = countries.Count == 0
            ? .40
            : Math.Min(.40, 4.78 / countries.Count);
        var rowHeights = new List<double> { headerHeight };
        rowHeights.AddRange(Enumerable.Repeat(bodyHeight, countries.Count));
        canvas.AddNativeTable(
            .72,
            1.43,
            new[] { 3.55, 1.25, 1.35, 3.13, 1.35, 1.25 },
            rowHeights,
            rows,
            $"FFC country-wise breakdown table page {pageNumber}");

        var pageStart = ((pageNumber - 1) * ProjectBriefingFfcGlobalFootprintOptions.CountriesPerBreakdownSlide) + 1;
        var pageEnd = pageStart + countries.Count - 1;
        canvas.AddText(
            .76,
            6.75,
            8.00,
            .18,
            $"Countries {pageStart}–{pageEnd} of {data.Countries.Count}",
            8.5,
            canvas.Theme.TextMuted,
            false,
            "l",
            name: "FFC country-wise breakdown range");
    }

    private static string FfcPlannedColor(ProjectBriefingThemeDefinition theme)
        => theme.IsDark ? "737D8E" : "AEB6C2";

    private static string FfcTrackColor(ProjectBriefingThemeDefinition theme)
        => theme.IsDark ? "2A303A" : "E9ECEF";
}
