using System.Text.RegularExpressions;
using SkiaSharp;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Physical narrative measurement for Compendium dossier planning. Authoritative measurements use
/// the same bundled DM Sans regular face as the publication renderer. A different platform font is
/// never silently substituted because that would make pagination depend on the host machine.
/// Measurements are expressed in PDF points; one Skia text unit is treated as one point so the
/// planner and QuestPDF share the same physical coordinate system.
/// </summary>
public static class CompendiumDossierTextMeasurementService
{
    private static readonly Regex ParagraphBreak = new(@"\n\s*\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLink = new(@"!?\[([^\]]*)\]\([^\)]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownNoise = new(@"(^|\s)[#>]+\s?|[*_`~]", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ListItemPrefix = new(@"^\s*(?:[-+*]\s+|\d+[.)]\s+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Lazy<SKTypeface> RegularTypeface = new(LoadRegularTypeface, LazyThreadSafetyMode.ExecutionAndPublication);

    public sealed record Measurement(
        float HeightPoints,
        int LineCount,
        int ParagraphCount)
    {
        public static Measurement Empty { get; } = new(0f, 0, 0);
    }

    /// <summary>
    /// Per-composition memoization. A publication can evaluate hundreds of candidate geometries;
    /// keeping this cache request-local avoids repeated Skia shaping without retaining project text
    /// in a process-wide cache.
    /// </summary>
    public sealed class Session
    {
        private readonly Dictionary<MeasurementKey, Measurement> _cache = new();

        public Measurement Measure(
            string? markdown,
            float widthPoints,
            float narrativeFontScale = 1f,
            bool includeHeading = false)
        {
            var scale = CompendiumNarrativeTypographyPolicy.NormalizeScale(narrativeFontScale);
            return MeasureAtFontSize(
                markdown,
                widthPoints,
                CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * scale,
                CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier,
                CompendiumNarrativeTypographyPolicy.ParagraphSpacingPoints,
                includeHeading ? CompendiumNarrativeTypographyPolicy.NarrativeHeadingReservePoints : 0f);
        }

        public Measurement MeasureAtFontSize(
            string? text,
            float widthPoints,
            float fontSizePoints,
            float lineHeightMultiplier,
            float paragraphSpacingPoints = 0f,
            float leadingReservePoints = 0f)
        {
            var normalized = Normalize(text);
            var key = new MeasurementKey(
                normalized,
                Quantize(widthPoints),
                Quantize(fontSizePoints),
                Quantize(lineHeightMultiplier),
                Quantize(paragraphSpacingPoints),
                Quantize(leadingReservePoints));
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var measured = MeasureAtFontSizeCore(
                normalized, widthPoints, fontSizePoints, lineHeightMultiplier, paragraphSpacingPoints, leadingReservePoints);
            _cache[key] = measured;
            return measured;
        }

        public bool Fits(
            string? markdown,
            float widthPoints,
            float availableHeightPoints,
            float narrativeFontScale = 1f,
            bool includeHeading = false,
            float tolerancePoints = .75f)
            => Measure(markdown, widthPoints, narrativeFontScale, includeHeading).HeightPoints
               <= Math.Max(0f, availableHeightPoints) + Math.Max(0f, tolerancePoints);

        private static int Quantize(float value)
            => (int)Math.Round(value * 100f, MidpointRounding.AwayFromZero);
    }

    private sealed record MeasurementKey(
        string Text,
        int WidthHundredths,
        int FontSizeHundredths,
        int LineHeightHundredths,
        int ParagraphSpacingHundredths,
        int LeadingReserveHundredths);

    public static Measurement Measure(
        string? markdown,
        float widthPoints,
        float narrativeFontScale = 1f,
        bool includeHeading = false)
    {
        var scale = CompendiumNarrativeTypographyPolicy.NormalizeScale(narrativeFontScale);
        return MeasureAtFontSize(
            markdown,
            widthPoints,
            CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * scale,
            CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier,
            CompendiumNarrativeTypographyPolicy.ParagraphSpacingPoints,
            includeHeading ? CompendiumNarrativeTypographyPolicy.NarrativeHeadingReservePoints : 0f);
    }

    public static Measurement MeasureAtFontSize(
        string? text,
        float widthPoints,
        float fontSizePoints,
        float lineHeightMultiplier,
        float paragraphSpacingPoints = 0f,
        float leadingReservePoints = 0f)
        => MeasureAtFontSizeCore(
            Normalize(text), widthPoints, fontSizePoints, lineHeightMultiplier, paragraphSpacingPoints, leadingReservePoints);

    private static Measurement MeasureAtFontSizeCore(
        string normalized,
        float widthPoints,
        float fontSizePoints,
        float lineHeightMultiplier,
        float paragraphSpacingPoints,
        float leadingReservePoints)
    {
        if (normalized.Length == 0)
        {
            return leadingReservePoints > 0f
                ? new Measurement(leadingReservePoints, 0, 0)
                : Measurement.Empty;
        }

        widthPoints = Math.Max(24f, widthPoints);
        fontSizePoints = Math.Max(5f, fontSizePoints);
        lineHeightMultiplier = Math.Max(1f, lineHeightMultiplier);
        paragraphSpacingPoints = Math.Max(0f, paragraphSpacingPoints);
        leadingReservePoints = Math.Max(0f, leadingReservePoints);
        var lineHeight = fontSizePoints * lineHeightMultiplier;
        var blocks = BuildMeasurementBlocks(normalized);

        if (blocks.Count == 0)
        {
            return leadingReservePoints > 0f
                ? new Measurement(leadingReservePoints, 0, 0)
                : Measurement.Empty;
        }

        using var paint = new SKPaint
        {
            Typeface = RegularTypeface.Value,
            TextSize = fontSizePoints,
            IsAntialias = true
        };

        var lines = 0;
        foreach (var block in blocks)
        {
            // Markdown list items render with an 18-point bullet/number gutter in QuestPDF.
            // Measure their text against the same reduced line width so Additional Notes and
            // narrative lists cannot silently consume more physical height than the planner saw.
            var blockWidth = block.IsListItem ? Math.Max(24f, widthPoints - 18f) : widthPoints;
            lines += MeasureWrappedLineCount(block.Text, blockWidth, paint);
        }

        var height = lines * lineHeight
                     + Math.Max(0, blocks.Count - 1) * paragraphSpacingPoints
                     + leadingReservePoints;

        return new Measurement(height, lines, blocks.Count);
    }

    public static bool Fits(
        string? markdown,
        float widthPoints,
        float availableHeightPoints,
        float narrativeFontScale = 1f,
        bool includeHeading = false,
        float tolerancePoints = .75f)
        => Measure(markdown, widthPoints, narrativeFontScale, includeHeading).HeightPoints
           <= Math.Max(0f, availableHeightPoints) + Math.Max(0f, tolerancePoints);

    private sealed record MeasurementBlock(string Text, bool IsListItem);

    private static IReadOnlyList<MeasurementBlock> BuildMeasurementBlocks(string normalized)
    {
        var result = new List<MeasurementBlock>();
        foreach (var rawBlock in ParagraphBreak.Split(normalized))
        {
            if (string.IsNullOrWhiteSpace(rawBlock)) continue;
            var lines = rawBlock.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            var containsListItem = lines.Any(line => ListItemPrefix.IsMatch(line));
            if (!containsListItem)
            {
                var paragraph = CleanMarkdownInline(rawBlock);
                if (paragraph.Length > 0) result.Add(new MeasurementBlock(paragraph, false));
                continue;
            }

            foreach (var rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                var isListItem = ListItemPrefix.IsMatch(rawLine);
                var content = isListItem ? ListItemPrefix.Replace(rawLine, string.Empty) : rawLine;
                content = CleanMarkdownInline(content);
                if (content.Length > 0) result.Add(new MeasurementBlock(content, isListItem));
            }
        }
        return result;
    }

    private static int MeasureWrappedLineCount(string paragraph, float widthPoints, SKPaint paint)
    {
        if (string.IsNullOrWhiteSpace(paragraph)) return 0;

        var words = Whitespace.Split(paragraph.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (words.Length == 0) return 0;

        var lineCount = 1;
        var line = words[0];
        for (var index = 1; index < words.Length; index++)
        {
            var word = words[index];
            var candidate = $"{line} {word}";
            if (paint.MeasureText(candidate) <= widthPoints)
            {
                line = candidate;
                continue;
            }

            lineCount++;
            line = word;

            // QuestPDF will still shape an unusually long token. Count the physical pressure of
            // that token conservatively without ever altering/splitting the source narrative.
            var wordWidth = paint.MeasureText(word);
            if (wordWidth > widthPoints)
            {
                lineCount += Math.Max(0, (int)Math.Ceiling(wordWidth / widthPoints) - 1);
            }
        }

        return lineCount;
    }

    private static string CleanMarkdownInline(string value)
    {
        var cleaned = MarkdownLink.Replace(value, "$1");
        cleaned = MarkdownNoise.Replace(cleaned, "$1");
        return Whitespace.Replace(cleaned, " ").Trim();
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();

    private static SKTypeface LoadRegularTypeface()
    {
        var attempted = new List<string>();
        foreach (var path in CandidateFontPaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            attempted.Add(path);
            try
            {
                if (!File.Exists(path)) continue;
                var typeface = SKTypeface.FromFile(path);
                if (typeface is not null) return typeface;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"The Compendium publication font could not be loaded from '{path}'. "
                    + "Physical page measurement cannot safely fall back to a different host font.",
                    ex);
            }
        }

        throw new InvalidOperationException(
            "The bundled DM Sans Regular font required for authoritative Compendium measurement was not found. "
            + $"Checked: {string.Join("; ", attempted)}");
    }

    private static IEnumerable<string> CandidateFontPaths()
    {
        var relative = Path.Combine("wwwroot", "fonts", "publications", "dm-sans", "DMSans-Regular.ttf");
        yield return Path.Combine(Directory.GetCurrentDirectory(), relative);
        yield return Path.Combine(AppContext.BaseDirectory, relative);
        yield return Path.Combine(AppContext.BaseDirectory, "fonts", "publications", "dm-sans", "DMSans-Regular.ttf");

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 7; depth++, directory = directory.Parent)
            yield return Path.Combine(directory.FullName, relative);
    }
}
