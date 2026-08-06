using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private const double FfcBodyX = .72;
    private const double FfcBodyY = 2.52;
    private const double FfcBodyHeight = 4.30;
    private const double FfcMapWidth = 8.36;
    private const double FfcBodyGap = .22;
    private const double FfcBreakdownWidth = 3.30;

    private static void RenderFfcGlobalFootprint(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        AddProjectSlideHeader(
            canvas,
            Truncate(data.Title, 110),
            subtitle: null,
            variant: ProjectSlideHeaderVariant.FfcGlobalFootprint);

        RenderFfcHeadlineMetrics(canvas, data);
        RenderFfcQuantityPosition(canvas, data);

        if (data.Countries.Count == 0)
        {
            canvas.AddSubtleRoundedRect(
                FfcBodyX,
                2.55,
                11.88,
                3.92,
                canvas.Theme.Surface,
                canvas.Theme.Border,
                "FFC empty-state panel");
            canvas.AddText(
                1.10,
                3.75,
                11.12,
                .62,
                "No active FFC portfolio data is available.",
                18,
                canvas.Theme.TextSecondary,
                true,
                "ctr",
                name: "FFC empty-state message");
            return;
        }

        if (data.Layout == ProjectBriefingFfcGlobalFootprintLayout.MapDominant)
        {
            RenderFfcMapDominant(canvas, data);
            return;
        }

        RenderFfcMap(canvas, data);
        RenderFfcCountryWiseBreakdown(canvas, data);
    }

    private static void RenderFfcHeadlineMetrics(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        var metrics = new[]
        {
            ("COUNTRIES", data.Summary.CountryCount),
            ("PROJECTS", data.Summary.ProjectCount),
            ("TOTAL QUANTITY", data.Summary.TotalUnits)
        };
        const double x = .72;
        const double y = 1.14;
        const double width = 3.78;
        const double gap = .27;

        for (var index = 0; index < metrics.Length; index++)
        {
            var cardX = x + (index * (width + gap));
            canvas.AddGroup(cardX, y, width, .72, $"FFC {metrics[index].Item1.ToLowerInvariant()} metric", () =>
            {
                canvas.AddRoundedRect(
                    cardX,
                    y,
                    width,
                    .72,
                    canvas.Theme.Surface,
                    canvas.Theme.Border,
                    .045,
                    $"FFC {metrics[index].Item1.ToLowerInvariant()} metric background");
                canvas.AddRect(cardX, y, .065, .72, index switch
                {
                    0 => canvas.Theme.SecondaryAccent,
                    1 => canvas.Theme.Accent,
                    _ => canvas.Theme.HeaderAccent
                }, null, 0, $"FFC {metrics[index].Item1.ToLowerInvariant()} metric accent");
                canvas.AddText(
                    cardX + .22,
                    y + .10,
                    1.40,
                    .42,
                    metrics[index].Item2.ToString("N0", CultureInfo.InvariantCulture),
                    23,
                    canvas.Theme.TextPrimary,
                    true,
                    "l",
                    name: $"FFC {metrics[index].Item1.ToLowerInvariant()} value");
                canvas.AddText(
                    cardX + 1.62,
                    y + .21,
                    width - 1.84,
                    .22,
                    metrics[index].Item1,
                    10.5,
                    canvas.Theme.TextMuted,
                    true,
                    "r",
                    name: $"FFC {metrics[index].Item1.ToLowerInvariant()} label");
            });
        }
    }

    private static void RenderFfcQuantityPosition(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        const double x = .72;
        const double y = 1.98;
        const double width = 11.88;
        const double height = .42;
        var total = Math.Max(1, data.Summary.TotalUnits);
        var installedWidth = width * data.Summary.InstalledUnits / total;
        var deliveredWidth = width * data.Summary.DeliveredNotInstalledUnits / total;
        var plannedWidth = Math.Max(0, width - installedWidth - deliveredWidth);
        var plannedColor = FfcPlannedColor(canvas.Theme);

        canvas.AddRoundedRect(x, y, width, height, canvas.Theme.SurfaceMuted, canvas.Theme.Border, .035, "FFC quantity-position background");
        var cursor = x;
        if (installedWidth > 0)
        {
            canvas.AddRect(cursor, y, installedWidth, .12, canvas.Theme.Positive, null, 0, "FFC installed segment");
            cursor += installedWidth;
        }
        if (deliveredWidth > 0)
        {
            canvas.AddRect(cursor, y, deliveredWidth, .12, canvas.Theme.Accent, null, 0, "FFC delivered segment");
            cursor += deliveredWidth;
        }
        if (plannedWidth > 0)
        {
            canvas.AddRect(cursor, y, plannedWidth, .12, plannedColor, null, 0, "FFC planned segment");
        }

        AddFfcLegendItem(canvas, x + .18, y + .17, "Installed", data.Summary.InstalledUnits, canvas.Theme.Positive);
        AddFfcLegendItem(canvas, x + 3.10, y + .17, "Delivered, awaiting installation", data.Summary.DeliveredNotInstalledUnits, canvas.Theme.Accent);
        AddFfcLegendItem(canvas, x + 8.70, y + .17, "Planned", data.Summary.PlannedUnits, plannedColor);
    }

    private static void AddFfcLegendItem(
        SlideCanvas canvas,
        double x,
        double y,
        string label,
        int value,
        string color)
    {
        canvas.AddRoundedRect(x, y + .03, .09, .09, color, null, .02, $"{label} legend marker");
        canvas.AddRichTextBox(
            x + .14,
            y,
            label.StartsWith("Delivered", StringComparison.Ordinal) ? 4.90 : 2.45,
            .18,
            new[]
            {
                new RichTextParagraph(new[]
                {
                    new RichTextRun(label + " ", 8.8, canvas.Theme.TextSecondary),
                    new RichTextRun(value.ToString("N0", CultureInfo.InvariantCulture), 9.0, canvas.Theme.TextPrimary, Bold: true)
                })
            },
            $"{label} legend",
            verticalAnchor: "ctr",
            allowAutoFit: false,
            leftInset: 0,
            rightInset: 0,
            topInset: 0,
            bottomInset: 0);
    }

    private static void RenderFfcMapDominant(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        const double width = 11.88;
        const double imageHeight = 3.56;
        const double stripGap = .08;
        const double stripHeight = .52;
        var stripY = FfcBodyY + imageHeight + stripGap + .06;

        canvas.AddSubtleRoundedRect(
            FfcBodyX,
            FfcBodyY,
            width,
            FfcBodyHeight,
            "FFFFFF",
            canvas.Theme.Border,
            "FFC map-dominant frame");

        if (data.MapImage.Length > 0)
        {
            canvas.AddImage(
                data.MapImage,
                "image/png",
                FfcBodyX + .06,
                FfcBodyY + .06,
                width - .12,
                imageHeight,
                "FFC map-dominant footprint map");
        }
        else
        {
            canvas.AddText(
                FfcBodyX + .3,
                FfcBodyY + 1.50,
                width - .6,
                .42,
                "Map not available",
                16,
                canvas.Theme.TextMuted,
                true,
                "ctr",
                name: "FFC map-dominant map unavailable");
        }

        RenderFfcTopCountryStrip(
            canvas,
            data.Countries.Take(5).ToArray(),
            FfcBodyX + .10,
            stripY,
            width - .20,
            stripHeight);
    }

    private static void RenderFfcTopCountryStrip(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingFfcCountryData> countries,
        double x,
        double y,
        double width,
        double height)
    {
        canvas.AddRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            .045,
            "FFC top-country strip");

        if (countries.Count == 0)
        {
            canvas.AddText(
                x + .18,
                y + .14,
                width - .36,
                .22,
                "No country position is available.",
                9.4,
                canvas.Theme.TextMuted,
                false,
                "ctr",
                name: "FFC top-country strip empty state");
            return;
        }

        const double gap = .08;
        var itemWidth = (width - ((countries.Count - 1) * gap)) / countries.Count;
        for (var index = 0; index < countries.Count; index++)
        {
            var country = countries[index];
            var itemX = x + (index * (itemWidth + gap));
            if (index > 0)
            {
                canvas.AddLine(
                    itemX - (gap / 2),
                    y + .10,
                    itemX - (gap / 2),
                    y + height - .10,
                    canvas.Theme.Divider,
                    .45);
            }

            canvas.AddRichTextBox(
                itemX + .08,
                y + .08,
                itemWidth - .16,
                height - .16,
                new[]
                {
                    new RichTextParagraph(new[]
                    {
                        new RichTextRun(country.IsoCode + "  ", 8.7, canvas.Theme.HeaderAccent, Bold: true),
                        new RichTextRun(Truncate(country.CountryName, 18) + "  ", 9.2, canvas.Theme.TextPrimary, Bold: true),
                        new RichTextRun(country.TotalUnits.ToString("N0", CultureInfo.InvariantCulture), 10.2, canvas.Theme.TextPrimary, Bold: true)
                    }, Align: "ctr")
                },
                $"FFC top country {country.CountryName}",
                verticalAnchor: "ctr",
                allowAutoFit: true,
                leftInset: 0,
                rightInset: 0,
                topInset: 0,
                bottomInset: 0);
        }
    }

    private static void RenderFfcMap(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        canvas.AddSubtleRoundedRect(
            FfcBodyX,
            FfcBodyY,
            FfcMapWidth,
            FfcBodyHeight,
            "FFFFFF",
            canvas.Theme.Border,
            "FFC footprint map frame");

        if (data.MapImage.Length > 0)
        {
            canvas.AddImage(
                data.MapImage,
                "image/png",
                FfcBodyX + .06,
                FfcBodyY + .06,
                FfcMapWidth - .12,
                FfcBodyHeight - .12,
                "FFC global footprint map");
        }
        else
        {
            canvas.AddText(
                FfcBodyX + .3,
                FfcBodyY + 1.65,
                FfcMapWidth - .6,
                .42,
                "Map not available",
                16,
                canvas.Theme.TextMuted,
                true,
                "ctr",
                name: "FFC map unavailable");
        }
    }

    private static void RenderFfcCountryWiseBreakdown(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        var x = FfcBodyX + FfcMapWidth + FfcBodyGap;
        const double y = FfcBodyY;
        const double width = FfcBreakdownWidth;
        const double height = FfcBodyHeight;
        const double panelPadding = .16;
        const double quantityColumnWidth = .42;
        const double rowsTopOffset = .50;
        const double rowTextHeight = .18;
        const double barOffset = .22;
        const double barHeight = .05;
        const double rowContentHeight = barOffset + barHeight;
        const double overflowGap = .04;
        const double overflowTextHeight = .18;
        const double panelBottomPadding = .12;

        var visible = data.Countries.Take(data.MaximumCountryRows).ToArray();
        var plannedColor = FfcPlannedColor(canvas.Theme);
        var trackColor = FfcTrackColor(canvas.Theme);
        var quantityX = x + width - panelPadding - quantityColumnWidth;
        var countryNameX = x + .58;
        var countryNameWidth = Math.Max(.70, quantityX - countryNameX - .10);
        var headingWidth = Math.Max(.90, quantityX - (x + panelPadding) - .10);
        var barWidth = width - (panelPadding * 2);

        canvas.AddSubtleRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            "FFC country-wise breakdown panel");
        canvas.AddText(
            x + panelPadding,
            y + .14,
            headingWidth,
            .22,
            "COUNTRY-WISE BREAKDOWN",
            8.8,
            canvas.Theme.TextMuted,
            true,
            "l",
            name: "FFC country-wise breakdown heading");
        canvas.AddText(
            quantityX,
            y + .14,
            quantityColumnWidth,
            .22,
            "QTY",
            9.0,
            canvas.Theme.TextMuted,
            true,
            "r",
            name: "FFC country quantity heading");
        canvas.AddLine(
            x + panelPadding,
            y + .43,
            x + width - panelPadding,
            y + .43,
            canvas.Theme.Divider,
            .55);

        var hasOverflow = data.Countries.Count > visible.Length;
        var compactRows = visible.Length >= 9;
        var preferredRowHeight = compactRows ? .35 : .45;
        var reservedAfterRows = panelBottomPadding
            + (hasOverflow ? overflowGap + overflowTextHeight : 0);
        var maximumRowHeight = visible.Length <= 1
            ? preferredRowHeight
            : (height
               - rowsTopOffset
               - rowContentHeight
               - reservedAfterRows)
              / (visible.Length - 1);
        var rowHeight = Math.Min(
            preferredRowHeight,
            Math.Max(.30, maximumRowHeight));
        var isoFontSize = compactRows ? 8.9 : 9.3;
        var countryFontSize = compactRows ? 9.2 : 9.8;
        var quantityFontSize = compactRows ? 9.8 : 10.2;
        var maximumQuantity = Math.Max(
            1,
            visible.Length == 0
                ? 1
                : visible.Max(country => country.TotalUnits));

        for (var index = 0; index < visible.Length; index++)
        {
            var country = visible[index];
            var rowY = y + rowsTopOffset + (index * rowHeight);

            canvas.AddText(
                x + panelPadding,
                rowY,
                .38,
                rowTextHeight,
                country.IsoCode,
                isoFontSize,
                canvas.Theme.HeaderAccent,
                true,
                "l",
                name: $"{country.CountryName} ISO code");
            canvas.AddText(
                countryNameX,
                rowY,
                countryNameWidth,
                rowTextHeight,
                Truncate(country.CountryName, 24),
                countryFontSize,
                canvas.Theme.TextPrimary,
                true,
                "l",
                name: $"{country.CountryName} name");
            canvas.AddText(
                quantityX,
                rowY,
                quantityColumnWidth,
                rowTextHeight,
                country.TotalUnits.ToString("N0", CultureInfo.InvariantCulture),
                quantityFontSize,
                canvas.Theme.TextPrimary,
                true,
                "r",
                name: $"{country.CountryName} quantity");

            var barY = rowY + barOffset;
            canvas.AddRoundedRect(
                x + panelPadding,
                barY,
                barWidth,
                barHeight,
                trackColor,
                null,
                .015,
                $"{country.CountryName} quantity bar background");

            var installed = barWidth * country.InstalledUnits / maximumQuantity;
            var delivered = barWidth * country.DeliveredNotInstalledUnits / maximumQuantity;
            var planned = barWidth * country.PlannedUnits / maximumQuantity;
            var cursor = x + panelPadding;

            if (installed > 0)
            {
                canvas.AddRect(
                    cursor,
                    barY,
                    installed,
                    barHeight,
                    canvas.Theme.Positive,
                    null,
                    0,
                    $"{country.CountryName} installed quantity");
                cursor += installed;
            }

            if (delivered > 0)
            {
                canvas.AddRect(
                    cursor,
                    barY,
                    delivered,
                    barHeight,
                    canvas.Theme.Accent,
                    null,
                    0,
                    $"{country.CountryName} delivered quantity");
                cursor += delivered;
            }

            if (planned > 0)
            {
                canvas.AddRect(
                    cursor,
                    barY,
                    planned,
                    barHeight,
                    plannedColor,
                    null,
                    0,
                    $"{country.CountryName} planned quantity");
            }
        }

        if (hasOverflow)
        {
            var remaining = data.Countries.Count - visible.Length;
            var overflowY = visible.Length == 0
                ? y + rowsTopOffset
                : y
                  + rowsTopOffset
                  + ((visible.Length - 1) * rowHeight)
                  + rowContentHeight
                  + overflowGap;

            canvas.AddText(
                x + panelPadding,
                overflowY,
                width - (panelPadding * 2),
                overflowTextHeight,
                $"+{remaining} more countr{(remaining == 1 ? "y" : "ies")}",
                8.7,
                canvas.Theme.TextMuted,
                false,
                "l",
                name: "FFC remaining countries");
        }
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
