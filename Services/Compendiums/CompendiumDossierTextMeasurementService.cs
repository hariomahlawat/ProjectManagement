using System.Text.RegularExpressions;
using ProjectManagement.Utilities.Reporting;
using SkiaSharp;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Physical narrative measurement for Compendium dossier planning. Authoritative measurements use
/// the same bundled DM Sans faces as the publication renderer. A different platform font is never
/// silently substituted because that would make pagination depend on the host machine.
/// Measurements are expressed in PDF points; one Skia text unit is treated as one point so the
/// planner and QuestPDF share the same physical coordinate system.
/// </summary>
public static class CompendiumDossierTextMeasurementService
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Lazy<SKTypeface> RegularTypeface = new(
        () => LoadTypeface("DMSans-Regular.ttf", "Regular"),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<SKTypeface> SemiBoldTypeface = new(
        () => LoadTypeface("DMSans-SemiBold.ttf", "SemiBold"),
        LazyThreadSafetyMode.ExecutionAndPublication);

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
            return MeasureSemanticAtFontSize(
                markdown,
                widthPoints,
                CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * scale,
                CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier,
                CompendiumNarrativeTypographyPolicy.ParagraphSpacingPoints,
                includeHeading ? CompendiumNarrativeTypographyPolicy.NarrativeHeadingReservePoints : 0f,
                allowMinorHeadings: true);
        }

        public Measurement MeasureAdditionalNote(
            string? markdown,
            float widthPoints,
            float narrativeFontScale = 1f,
            float leadingReservePoints = 0f)
        {
            var scale = CompendiumNarrativeTypographyPolicy.NormalizeScale(narrativeFontScale);
            return MeasureSemanticAtFontSize(
                markdown,
                widthPoints,
                CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * scale,
                CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier,
                CompendiumNarrativeTypographyPolicy.ParagraphSpacingPoints,
                leadingReservePoints,
                allowMinorHeadings: false);
        }

        /// <summary>
        /// Plain physical text measurement used for specification items and other non-narrative
        /// values. Markdown block syntax is intentionally not interpreted here.
        /// </summary>
        public Measurement MeasureAtFontSize(
            string? text,
            float widthPoints,
            float fontSizePoints,
            float lineHeightMultiplier,
            float paragraphSpacingPoints = 0f,
            float leadingReservePoints = 0f,
            bool semiBold = false,
            float letterSpacingPoints = 0f)
            => MeasurePlainAtFontSize(
                text,
                widthPoints,
                fontSizePoints,
                lineHeightMultiplier,
                paragraphSpacingPoints,
                leadingReservePoints,
                semiBold,
                letterSpacingPoints);

        public Measurement MeasureSemanticAtFontSize(
            string? markdown,
            float widthPoints,
            float fontSizePoints,
            float lineHeightMultiplier,
            float paragraphSpacingPoints = 0f,
            float leadingReservePoints = 0f,
            bool allowMinorHeadings = true)
        {
            var normalized = CompendiumNarrativeParser.Normalize(markdown);
            var key = new MeasurementKey(
                normalized,
                Quantize(widthPoints),
                Quantize(fontSizePoints),
                Quantize(lineHeightMultiplier),
                Quantize(paragraphSpacingPoints),
                Quantize(leadingReservePoints),
                Semantic: true,
                AllowMinorHeadings: allowMinorHeadings,
                SemiBold: false,
                LetterSpacingHundredths: 0);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var measured = MeasureSemanticAtFontSizeCore(
                normalized,
                widthPoints,
                fontSizePoints,
                lineHeightMultiplier,
                paragraphSpacingPoints,
                leadingReservePoints,
                allowMinorHeadings);
            _cache[key] = measured;
            return measured;
        }

        private Measurement MeasurePlainAtFontSize(
            string? text,
            float widthPoints,
            float fontSizePoints,
            float lineHeightMultiplier,
            float paragraphSpacingPoints,
            float leadingReservePoints,
            bool semiBold,
            float letterSpacingPoints)
        {
            var normalized = NormalizePlain(text);
            var key = new MeasurementKey(
                normalized,
                Quantize(widthPoints),
                Quantize(fontSizePoints),
                Quantize(lineHeightMultiplier),
                Quantize(paragraphSpacingPoints),
                Quantize(leadingReservePoints),
                Semantic: false,
                AllowMinorHeadings: false,
                SemiBold: semiBold,
                LetterSpacingHundredths: Quantize(letterSpacingPoints));
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var measured = MeasurePlainAtFontSizeCore(
                normalized,
                widthPoints,
                fontSizePoints,
                lineHeightMultiplier,
                paragraphSpacingPoints,
                leadingReservePoints,
                semiBold,
                letterSpacingPoints);
            _cache[key] = measured;
            return measured;
        }

        public bool Fits(
            string? markdown,
            float widthPoints,
            float availableHeightPoints,
            float narrativeFontScale = 1f,
            bool includeHeading = false,
            float tolerancePoints = .75f,
            bool allowMinorHeadings = true)
        {
            var scale = CompendiumNarrativeTypographyPolicy.NormalizeScale(narrativeFontScale);
            var measured = MeasureSemanticAtFontSize(
                markdown,
                widthPoints,
                CompendiumNarrativeTypographyPolicy.BodyFontSizePoints * scale,
                CompendiumNarrativeTypographyPolicy.BodyLineHeightMultiplier,
                CompendiumNarrativeTypographyPolicy.ParagraphSpacingPoints,
                includeHeading ? CompendiumNarrativeTypographyPolicy.NarrativeHeadingReservePoints : 0f,
                allowMinorHeadings);
            return measured.HeightPoints
                   <= Math.Max(0f, availableHeightPoints) + Math.Max(0f, tolerancePoints);
        }

        public bool FitsAdditionalNote(
            string? markdown,
            float widthPoints,
            float availableHeightPoints,
            float narrativeFontScale = 1f,
            float tolerancePoints = .75f)
            => MeasureAdditionalNote(markdown, widthPoints, narrativeFontScale).HeightPoints
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
        int LeadingReserveHundredths,
        bool Semantic,
        bool AllowMinorHeadings,
        bool SemiBold,
        int LetterSpacingHundredths);

    public static Measurement Measure(
        string? markdown,
        float widthPoints,
        float narrativeFontScale = 1f,
        bool includeHeading = false)
        => new Session().Measure(markdown, widthPoints, narrativeFontScale, includeHeading);

    public static Measurement MeasureAdditionalNote(
        string? markdown,
        float widthPoints,
        float narrativeFontScale = 1f,
        float leadingReservePoints = 0f)
        => new Session().MeasureAdditionalNote(markdown, widthPoints, narrativeFontScale, leadingReservePoints);

    public static Measurement MeasureAtFontSize(
        string? text,
        float widthPoints,
        float fontSizePoints,
        float lineHeightMultiplier,
        float paragraphSpacingPoints = 0f,
        float leadingReservePoints = 0f,
        bool semiBold = false,
        float letterSpacingPoints = 0f)
        => new Session().MeasureAtFontSize(
            text,
            widthPoints,
            fontSizePoints,
            lineHeightMultiplier,
            paragraphSpacingPoints,
            leadingReservePoints,
            semiBold,
            letterSpacingPoints);

    public static bool Fits(
        string? markdown,
        float widthPoints,
        float availableHeightPoints,
        float narrativeFontScale = 1f,
        bool includeHeading = false,
        float tolerancePoints = .75f)
        => Measure(markdown, widthPoints, narrativeFontScale, includeHeading).HeightPoints
           <= Math.Max(0f, availableHeightPoints) + Math.Max(0f, tolerancePoints);

    private static Measurement MeasureSemanticAtFontSizeCore(
        string normalized,
        float widthPoints,
        float fontSizePoints,
        float lineHeightMultiplier,
        float paragraphSpacingPoints,
        float leadingReservePoints,
        bool allowMinorHeadings)
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

        var document = CompendiumNarrativeParser.Parse(normalized, allowMinorHeadings);
        if (document.IsEmpty)
        {
            return leadingReservePoints > 0f
                ? new Measurement(leadingReservePoints, 0, 0)
                : Measurement.Empty;
        }

        using var bodyPaint = CreatePaint(RegularTypeface.Value, fontSizePoints);
        var headingFontSize = fontSizePoints * CompendiumNarrativeSemanticPolicy.MinorHeadingFontScale;
        using var headingPaint = CreatePaint(SemiBoldTypeface.Value, headingFontSize);

        var totalHeight = leadingReservePoints;
        var totalLines = 0;
        var blockCount = 0;
        foreach (var block in document.Blocks)
        {
            if (blockCount > 0) totalHeight += paragraphSpacingPoints;

            switch (block.Kind)
            {
                case CompendiumNarrativeBlockKind.MinorHeading:
                {
                    var text = CompendiumNarrativeParser.CleanInline(block.Markdown);
                    var lines = MeasureWrappedLineCount(text, widthPoints, headingPaint);
                    totalLines += lines;
                    totalHeight += CompendiumNarrativeSemanticPolicy.MinorHeadingTopSpacingPoints
                                   + lines * headingFontSize * CompendiumNarrativeSemanticPolicy.MinorHeadingLineHeightMultiplier
                                   + CompendiumNarrativeSemanticPolicy.MinorHeadingBottomSpacingPoints;
                    break;
                }

                case CompendiumNarrativeBlockKind.BulletList:
                {
                    var itemIndex = 0;
                    foreach (var item in block.Items)
                    {
                        var text = CompendiumNarrativeParser.CleanInline(item);
                        if (text.Length == 0) continue;
                        if (itemIndex++ > 0) totalHeight += CompendiumNarrativeSemanticPolicy.BulletItemSpacingPoints;
                        var lines = MeasureWrappedLineCount(
                            text,
                            Math.Max(24f, widthPoints - CompendiumNarrativeSemanticPolicy.BulletGutterPoints),
                            bodyPaint);
                        totalLines += lines;
                        totalHeight += lines * fontSizePoints * lineHeightMultiplier;
                    }
                    break;
                }

                default:
                {
                    var text = CompendiumNarrativeParser.CleanInline(block.Markdown);
                    var lines = MeasureWrappedLineCount(text, widthPoints, bodyPaint);
                    totalLines += lines;
                    totalHeight += lines * fontSizePoints * lineHeightMultiplier;
                    break;
                }
            }

            blockCount++;
        }

        return new Measurement(totalHeight, totalLines, blockCount);
    }

    private static Measurement MeasurePlainAtFontSizeCore(
        string normalized,
        float widthPoints,
        float fontSizePoints,
        float lineHeightMultiplier,
        float paragraphSpacingPoints,
        float leadingReservePoints,
        bool semiBold,
        float letterSpacingPoints)
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
        letterSpacingPoints = Math.Max(0f, letterSpacingPoints);

        var paragraphs = normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Whitespace.Replace(value, " ").Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (paragraphs.Length == 0) return Measurement.Empty;

        using var paint = CreatePaint(semiBold ? SemiBoldTypeface.Value : RegularTypeface.Value, fontSizePoints);
        var lines = paragraphs.Sum(paragraph => MeasureWrappedLineCount(paragraph, widthPoints, paint, letterSpacingPoints));
        var height = lines * fontSizePoints * lineHeightMultiplier
                     + Math.Max(0, paragraphs.Length - 1) * paragraphSpacingPoints
                     + leadingReservePoints;
        return new Measurement(height, lines, paragraphs.Length);
    }

    private static SKPaint CreatePaint(SKTypeface typeface, float textSize)
        => new()
        {
            Typeface = typeface,
            TextSize = textSize,
            IsAntialias = true
        };

    private static int MeasureWrappedLineCount(
        string paragraph,
        float widthPoints,
        SKPaint paint,
        float letterSpacingPoints = 0f)
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
            if (MeasureTextWidth(candidate, paint, letterSpacingPoints) <= widthPoints)
            {
                line = candidate;
                continue;
            }

            lineCount++;
            line = word;

            // QuestPDF still shapes an unusually long token. Count the physical pressure of that
            // token conservatively without ever altering/splitting the publication source.
            var wordWidth = MeasureTextWidth(word, paint, letterSpacingPoints);
            if (wordWidth > widthPoints)
            {
                lineCount += Math.Max(0, (int)Math.Ceiling(wordWidth / widthPoints) - 1);
            }
        }

        return lineCount;
    }


    private static float MeasureTextWidth(string value, SKPaint paint, float letterSpacingPoints)
    {
        var width = paint.MeasureText(value);
        if (letterSpacingPoints <= 0f || value.Length <= 1) return width;
        return width + (value.Length - 1) * letterSpacingPoints;
    }

    private static string NormalizePlain(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();

    private static SKTypeface LoadTypeface(string fileName, string faceName)
    {
        string path;
        try
        {
            path = PublicationFontContract.ResolveRequiredDmSansFile(fileName);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The bundled DM Sans {faceName} font required for authoritative Compendium measurement could not be resolved. "
                + "Physical page measurement cannot safely fall back to a different host font.",
                exception);
        }

        try
        {
            var typeface = SKTypeface.FromFile(path);
            return typeface ?? throw new InvalidOperationException(
                $"SkiaSharp returned no typeface for the Compendium publication font '{path}'.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The Compendium publication font face '{faceName}' could not be loaded from '{path}'. "
                + "Verify the font file, IIS application-pool read permission and SkiaSharp win-x64 native assets.",
                exception);
        }
    }
}
