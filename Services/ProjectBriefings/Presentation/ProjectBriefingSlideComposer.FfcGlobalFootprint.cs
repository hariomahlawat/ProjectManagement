using System.Globalization;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static void RenderFfcGlobalFootprint(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        AddProjectSlideHeader(
            canvas,
            Truncate(data.Title, 110),
            subtitle: null,
            variant: ProjectSlideHeaderVariant.Standard);

        RenderFfcHeadlineMetrics(canvas, data);
        RenderFfcQuantityPosition(canvas, data);

        if (data.Countries.Count == 0)
        {
            canvas.AddRoundedRect(.72, 2.55, 11.88, 3.92, canvas.Theme.Surface, canvas.Theme.Border, .05, "FFC empty-state panel");
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
        }
        else
        {
            RenderFfcMap(canvas, data);
            RenderFfcCountryPosition(canvas, data);
        }

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
            canvas.AddRect(cursor, y, plannedWidth, .12, canvas.Theme.Divider, null, 0, "FFC planned segment");
        }

        AddFfcLegendItem(canvas, x + .18, y + .17, "Installed", data.Summary.InstalledUnits, canvas.Theme.Positive);
        AddFfcLegendItem(canvas, x + 3.10, y + .17, "Delivered, awaiting installation", data.Summary.DeliveredNotInstalledUnits, canvas.Theme.Accent);
        AddFfcLegendItem(canvas, x + 8.70, y + .17, "Planned", data.Summary.PlannedUnits, canvas.Theme.Divider);
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

    private static void RenderFfcMap(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        const double x = .72;
        const double y = 2.52;
        const double width = 7.15;
        const double height = 4.30;
        canvas.AddRoundedRect(x, y, width, height, "FFFFFF", canvas.Theme.Border, .045, "FFC footprint map frame");
        if (data.MapImage.Length > 0)
        {
            canvas.AddImage(data.MapImage, "image/png", x + .06, y + .06, width - .12, height - .12, "FFC global footprint map");
        }
        else
        {
            canvas.AddText(x + .3, y + 1.65, width - .6, .42, "Map not available", 16, canvas.Theme.TextMuted, true, "ctr", name: "FFC map unavailable");
        }
    }

    private static void RenderFfcCountryPosition(
        SlideCanvas canvas,
        ProjectBriefingFfcGlobalFootprintData data)
    {
        const double x = 8.12;
        const double y = 2.52;
        const double width = 4.48;
        const double height = 4.30;
        var visible = data.Countries.Take(data.MaximumCountryRows).ToArray();
        canvas.AddRoundedRect(x, y, width, height, canvas.Theme.Surface, canvas.Theme.Border, .045, "FFC country-position panel");
        canvas.AddText(x + .20, y + .14, 2.72, .22, "COUNTRY POSITION", 10.5, canvas.Theme.TextMuted, true, "l", name: "FFC country-position heading");
        canvas.AddText(x + 3.42, y + .14, .78, .22, "TOTAL QTY", 9.6, canvas.Theme.TextMuted, true, "r", name: "FFC country quantity heading");
        canvas.AddLine(x + .20, y + .43, x + width - .20, y + .43, canvas.Theme.Divider, .55);

        var hasOverflow = data.Countries.Count > visible.Length;
        var compactRows = visible.Length >= 9;
        var overflowReserve = hasOverflow ? .34 : .14;
        var availableHeight = height - .70 - overflowReserve;
        var preferredRowHeight = compactRows ? .33 : .42;
        var rowHeight = visible.Length == 0
            ? preferredRowHeight
            : Math.Min(preferredRowHeight, availableHeight / visible.Length);
        var maximumQuantity = Math.Max(1, visible.Length == 0 ? 1 : visible.Max(country => country.TotalUnits));
        for (var index = 0; index < visible.Length; index++)
        {
            var country = visible[index];
            var rowY = y + .50 + (index * rowHeight);
            canvas.AddText(x + .20, rowY, .44, .17, country.IsoCode, 9.0, canvas.Theme.HeaderAccent, true, "l", name: $"{country.CountryName} ISO code");
            canvas.AddText(x + .70, rowY, 2.35, .17, Truncate(country.CountryName, 30), 9.5, canvas.Theme.TextPrimary, true, "l", name: $"{country.CountryName} name");
            canvas.AddText(x + 3.42, rowY, .78, .17, country.TotalUnits.ToString("N0", CultureInfo.InvariantCulture), 10.0, canvas.Theme.TextPrimary, true, "r", name: $"{country.CountryName} quantity");

            var barY = rowY + .20;
            var barWidth = 4.00;
            canvas.AddRoundedRect(x + .20, barY, barWidth, .05, canvas.Theme.SurfaceMuted, null, .015, $"{country.CountryName} quantity bar background");
            var installed = barWidth * country.InstalledUnits / maximumQuantity;
            var delivered = barWidth * country.DeliveredNotInstalledUnits / maximumQuantity;
            var planned = barWidth * country.PlannedUnits / maximumQuantity;
            var cursor = x + .20;
            if (installed > 0)
            {
                canvas.AddRect(cursor, barY, installed, .05, canvas.Theme.Positive, null, 0, $"{country.CountryName} installed quantity");
                cursor += installed;
            }
            if (delivered > 0)
            {
                canvas.AddRect(cursor, barY, delivered, .05, canvas.Theme.Accent, null, 0, $"{country.CountryName} delivered quantity");
                cursor += delivered;
            }
            if (planned > 0)
            {
                canvas.AddRect(cursor, barY, planned, .05, canvas.Theme.Divider, null, 0, $"{country.CountryName} planned quantity");
            }
        }

        if (data.Countries.Count > visible.Length)
        {
            canvas.AddText(
                x + .20,
                y + height - .26,
                width - .40,
                .17,
                $"+ {data.Countries.Count - visible.Length} more countr{(data.Countries.Count - visible.Length == 1 ? "y" : "ies")}",
                8.5,
                canvas.Theme.TextMuted,
                false,
                "l",
                name: "FFC remaining countries");
        }
    }
}
