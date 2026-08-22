using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// One authoritative layout policy for Project Particulars. Both publication skins consume the
/// same ordered module set and server-resolved column geometry; only the visual treatment differs.
/// Measurements are expressed in PDF points so the dossier planner, browser proof and QuestPDF
/// remain aligned.
/// </summary>
public static class CompendiumProjectParticularsLayoutPolicy
{
    public const float FullWidthPoints = CompendiumLayoutMetrics.ContentWidthPoints;

    public sealed record Layout(
        CompendiumProjectParticularsStyle Style,
        int Columns,
        int Rows,
        float HeightPoints,
        bool IsCompactSingle);

    public static Layout Resolve(
        CompendiumProjectParticularsStyle style,
        IReadOnlyList<CompendiumProgrammeModuleDto>? modules,
        CompendiumDossierTextMeasurementService.Session? measurementSession = null)
    {
        style = Normalize(style);
        var items = (modules ?? Array.Empty<CompendiumProgrammeModuleDto>())
            .Where(module => !string.IsNullOrWhiteSpace(module.Label)
                             && !string.IsNullOrWhiteSpace(module.Value))
            .Take(4)
            .ToArray();
        if (items.Length == 0)
            return new Layout(style, 0, 0, 0f, false);

        measurementSession ??= new CompendiumDossierTextMeasurementService.Session();
        var columns = style == CompendiumProjectParticularsStyle.Minimal
            ? ResolveMinimalColumns(items, measurementSession)
            : ResolvePanelColumns(items.Length);
        columns = Math.Clamp(columns, 1, items.Length);
        var rows = (int)Math.Ceiling((double)items.Length / columns);
        var compactSingle = items.Length == 1 && IsCompactSingle(items[0]);
        var height = style == CompendiumProjectParticularsStyle.Minimal
            ? EstimateMinimalHeight(items, columns, measurementSession)
            : EstimatePanelHeight(items, columns, measurementSession);
        return new Layout(style, columns, rows, height, compactSingle);
    }

    public static CompendiumProjectParticularsStyle Normalize(CompendiumProjectParticularsStyle value)
        => Enum.IsDefined(value) ? value : CompendiumProjectParticularsStyle.Panel;

    private static int ResolvePanelColumns(int moduleCount)
        => moduleCount switch
        {
            <= 1 => 1,
            2 => 2,
            3 => 3,
            _ => 2
        };

    private static int ResolveMinimalColumns(
        IReadOnlyList<CompendiumProgrammeModuleDto> modules,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        if (modules.Count <= 1) return 1;
        if (modules.Count == 2) return 2;

        // Minimal may use a single four-item row when every label/value remains genuinely compact.
        // If not, it falls back to the same calm two-column rhythm as Panel rather than shrinking type.
        if (modules.Count >= 4 && AllFitCompactly(modules, 4, measurementSession)) return 4;
        if (modules.Count == 3 && AllFitCompactly(modules, 3, measurementSession)) return 3;
        return 2;
    }

    private static bool AllFitCompactly(
        IReadOnlyList<CompendiumProgrammeModuleDto> modules,
        int columns,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        var itemWidth = FullWidthPoints / columns;
        var textWidth = Math.Max(46f, itemWidth - 30f); // icon + gutter + modest inter-item breathing room
        foreach (var module in modules)
        {
            var label = measurementSession.MeasureAtFontSize(
                module.Label.ToUpperInvariant(),
                textWidth,
                fontSizePoints: columns >= 4 ? 5.55f : 5.8f,
                lineHeightMultiplier: 1.08f,
                semiBold: true,
                letterSpacingPoints: columns >= 4 ? .04f : .1f);
            var value = measurementSession.MeasureAtFontSize(
                module.Value,
                textWidth,
                fontSizePoints: columns >= 4 ? 8.4f : 8.7f,
                lineHeightMultiplier: 1.08f,
                semiBold: true);
            if (label.LineCount > 2 || value.LineCount > 2) return false;
        }
        return true;
    }

    private static float EstimatePanelHeight(
        IReadOnlyList<CompendiumProgrammeModuleDto> modules,
        int columns,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        var paddingVertical = modules.Count switch { 1 => 5f, 2 or 3 => 6f, _ => 7f };
        var spacing = modules.Count == 1 ? 4f : 6f;
        var compactSingle = modules.Count == 1 && IsCompactSingle(modules[0]);
        const float borderPoints = 1f;
        var innerWidth = Math.Max(80f, FullWidthPoints - (2f * borderPoints) - 20f); // border + 10pt horizontal padding
        var itemWidth = innerWidth / Math.Max(1, columns);
        if (compactSingle) itemWidth /= 2f;
        var textWidth = Math.Max(42f, itemWidth - 37f); // right padding + icon + gutter
        var labelSize = columns switch { >= 3 => 6.05f, 2 => 6.3f, _ => 6.5f };
        const float valueSize = 9.1f;
        var rowHeights = new List<float>();
        foreach (var row in modules.Chunk(columns))
        {
            var rowHeight = row.Max(module =>
            {
                var label = measurementSession.MeasureAtFontSize(
                    module.Label.ToUpperInvariant(),
                    textWidth,
                    labelSize,
                    1.08f,
                    semiBold: true,
                    letterSpacingPoints: columns switch { >= 3 => .08f, 2 => .12f, _ => .16f }).HeightPoints;
                var value = measurementSession.MeasureAtFontSize(
                    module.Value, textWidth, valueSize, 1.08f, semiBold: true).HeightPoints;
                return Math.Max(22f, label + 2f + value);
            });
            rowHeights.Add(rowHeight);
        }

        var heading = measurementSession.MeasureAtFontSize(
            "PROJECT PARTICULARS",
            innerWidth,
            fontSizePoints: 7.2f,
            lineHeightMultiplier: 1.08f,
            semiBold: true,
            letterSpacingPoints: .32f).HeightPoints;

        const float topRule = 2.25f;
        return (2f * borderPoints)
               + topRule
               + paddingVertical * 2f
               + heading
               + spacing * rowHeights.Count
               + rowHeights.Sum();
    }

    private static float EstimateMinimalHeight(
        IReadOnlyList<CompendiumProgrammeModuleDto> modules,
        int columns,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        var compactSingle = modules.Count == 1 && IsCompactSingle(modules[0]);
        var itemWidth = FullWidthPoints / Math.Max(1, columns);
        if (compactSingle) itemWidth /= 2f;
        var rightPadding = columns >= 4 ? 5f : 10f;
        var textWidth = Math.Max(44f, itemWidth - rightPadding - 19f - 6f);
        var labelSize = columns >= 4 ? 5.55f : columns >= 3 ? 5.75f : 6f;
        var valueSize = columns >= 4 ? 8.4f : 8.8f;
        var rowHeights = new List<float>();
        foreach (var row in modules.Chunk(columns))
        {
            var rowHeight = row.Max(module =>
            {
                var label = measurementSession.MeasureAtFontSize(
                    module.Label.ToUpperInvariant(),
                    textWidth,
                    labelSize,
                    1.08f,
                    semiBold: true,
                    letterSpacingPoints: columns >= 4 ? .04f : .1f).HeightPoints;
                var value = measurementSession.MeasureAtFontSize(
                    module.Value, textWidth, valueSize, 1.08f, semiBold: true).HeightPoints;
                return Math.Max(19f, label + 1.6f + value);
            });
            rowHeights.Add(rowHeight);
        }

        var header = measurementSession.MeasureAtFontSize(
            "PROJECT PARTICULARS",
            Math.Max(80f, FullWidthPoints - 9f),
            fontSizePoints: 7.2f,
            lineHeightMultiplier: 1.08f,
            semiBold: true,
            letterSpacingPoints: .32f).HeightPoints;

        // QuestPDF uses one 7pt Column.Spacing gap between the header and every rendered row.
        return Math.Max(header, 5.4f)
               + rowHeights.Sum()
               + rowHeights.Count * 7f;
    }

    private static bool IsCompactSingle(CompendiumProgrammeModuleDto module)
        => !module.Value.Contains('\n') && module.Value.Trim().Length <= 48;
}
