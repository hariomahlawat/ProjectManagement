using System.Globalization;
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
        var visible = data.Countries.Take(data.MaximumCountryRows).ToArray();
        var plannedColor = FfcPlannedColor(canvas.Theme);
        var trackColor = FfcTrackColor(canvas.Theme);

        canvas.AddSubtleRoundedRect(
            x,
            y,
            width,
            height,
            canvas.Theme.Surface,
            canvas.Theme.Border,
            "FFC country-wise breakdown panel");
        canvas.AddText(
            x + .16,
            y + .14,
            2.42,
            .22,
            "COUNTRY-WISE BREAKDOWN",
            8.8,
            canvas.Theme.TextMuted,
            true,
            "l",
            name: "FFC country-wise breakdown heading");
        canvas.AddText(
            x + 2.68,
            y + .14,
            .42,
            .22,
            "QTY",
            9.0,
            canvas.Theme.TextMuted,
            true,
            "r",
            name: "FFC country quantity heading");
        canvas.AddLine(x + .16, y + .43, x + width - .16, y + .43, canvas.Theme.Divider, .55);

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
            canvas.AddText(
                x + .16,
                rowY,
                .38,
                .17,
                country.IsoCode,
                8.7,
                canvas.Theme.HeaderAccent,
                true,
                "l",
                name: $"{country.CountryName} ISO code");
            canvas.AddText(
                x + .58,
                rowY,
                1.94,
                .17,
                Truncate(country.CountryName, 24),
                compactRows ? 8.9 : 9.2,
                canvas.Theme.TextPrimary,
                true,
                "l",
                name: $"{country.CountryName} name");
            canvas.AddText(
                x + 2.68,
                rowY,
                .42,
                .17,
                country.TotalUnits.ToString("N0", CultureInfo.InvariantCulture),
                9.6,
                canvas.Theme.TextPrimary,
                true,
                "r",
                name: $"{country.CountryName} quantity");

            var barY = rowY + .20;
            const double barWidth = 2.94;
            canvas.AddRoundedRect(
                x + .16,
                barY,
                barWidth,
                .05,
                trackColor,
                null,
                .015,
                $"{country.CountryName} quantity bar background");
            var installed = barWidth * country.InstalledUnits / maximumQuantity;
            var delivered = barWidth * country.DeliveredNotInstalledUnits / maximumQuantity;
            var planned = barWidth * country.PlannedUnits / maximumQuantity;
            var cursor = x + .16;

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
                canvas.AddRect(cursor, barY, planned, .05, plannedColor, null, 0, $"{country.CountryName} planned quantity");
            }
        }

        if (hasOverflow)
        {
            var remaining = data.Countries.Count - visible.Length;
            canvas.AddText(
                x + .16,
                y + height - .26,
                width - .32,
                .17,
                $"+{remaining} more countr{(remaining == 1 ? "y" : "ies")}",
                8.5,
                canvas.Theme.TextMuted,
                false,
                "l",
                name: "FFC remaining countries");
        }
    }

    private static string FfcPlannedColor(ProjectBriefingThemeDefinition theme)
        => theme.IsDark ? "737D8E" : "AEB6C2";

    private static string FfcTrackColor(ProjectBriefingThemeDefinition theme)
        => theme.IsDark ? "2A303A" : "E9ECEF";
}
