using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Compendiums;

public sealed record CompendiumDossierNarrativeFlowPlan(
    CompendiumBalancedTextFlowMode Mode,
    string SideSegment,
    string BelowImageSegment,
    IReadOnlyList<string> ContinuationSegments)
{
    public static CompendiumDossierNarrativeFlowPlan Empty { get; } = new(
        CompendiumBalancedTextFlowMode.FlowBelowImage,
        string.Empty,
        string.Empty,
        Array.Empty<string>());

    public CompendiumNarrativeAlignment EffectiveAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumNarrativeAlignment SideAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public CompendiumNarrativeAlignment BelowAlignment { get; init; } = CompendiumNarrativeAlignment.Left;
    public float SideRegionHeightPoints { get; init; }
    public float SideUsedHeightPoints { get; init; }
    public float SideRemainingHeightPoints { get; init; }
    public float SideUtilizationRatio { get; init; }
    public int EstimatedPageCount => 1 + ContinuationSegments.Count;
    public bool Continues => ContinuationSegments.Count > 0;
}

/// <summary>
/// Deterministic narrative segmentation for Compendium dossier pages. Phase 37 fills the Balanced
/// side region paragraph-first and then sentence-by-sentence before flowing the remainder across
/// the full page width. Words and sentences are never sliced to satisfy a heuristic.
/// </summary>
public static class CompendiumDossierNarrativeFlowPlanner
{
    private const int ContinuationBudget = 3300;
    private const float SideHeadingReservePoints = 29f;
    private const float SideCharactersPerLine = 39f;
    private const float NarrativeLineHeightPoints = 12.5f;
    private const float ParagraphGapPoints = 5f;

    private static readonly Regex ParagraphBreak = new(@"\n\s*\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InitialismEnding = new(@"(?:\b[A-Za-z]\.){2,}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> NonTerminalAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "e.g.", "i.e.", "dr.", "mr.", "mrs.", "ms.", "prof.", "no.", "fig.", "ref.", "refs.", "para.", "sec."
    };

    public sealed record SideAssessment(
        string SideSegment,
        string BelowSegment,
        float RegionHeightPoints,
        float UsedHeightPoints,
        float RemainingHeightPoints,
        float UtilizationRatio)
    {
        public bool HasExcessiveGap => RemainingHeightPoints > 40f && !string.IsNullOrWhiteSpace(BelowSegment);
    }

    public static CompendiumDossierNarrativeFlowPlan Resolve(
        string? narrative,
        CompendiumBalancedTextFlowMode mode,
        CompendiumDossierLayout layout,
        bool hasPrimaryImage,
        float primaryImageHeightPoints,
        float narrativeFontScale,
        int firstPageNarrativeBudget,
        CompendiumNarrativeAlignment narrativeAlignment = CompendiumNarrativeAlignment.Left,
        float sideColumnWidthPoints = 223f)
    {
        var clean = Normalize(narrative);
        narrativeAlignment = CompendiumNarrativeTypographyPolicy.Normalize(narrativeAlignment);
        if (clean.Length == 0)
        {
            return CompendiumDossierNarrativeFlowPlan.Empty with
            {
                Mode = mode,
                EffectiveAlignment = narrativeAlignment,
                SideAlignment = CompendiumNarrativeTypographyPolicy.ResolveSideAlignment(narrativeAlignment, sideColumnWidthPoints),
                BelowAlignment = CompendiumNarrativeTypographyPolicy.ResolveFullWidthAlignment(narrativeAlignment)
            };
        }

        var chunks = SplitNatural(clean, Math.Max(760, firstPageNarrativeBudget), ContinuationBudget);
        var firstPage = chunks[0];
        var continuations = chunks.Skip(1).ToArray();
        var fullWidthAlignment = CompendiumNarrativeTypographyPolicy.ResolveFullWidthAlignment(narrativeAlignment);
        var sideAlignment = CompendiumNarrativeTypographyPolicy.ResolveSideAlignment(narrativeAlignment, sideColumnWidthPoints);

        if (layout != CompendiumDossierLayout.Balanced || !hasPrimaryImage)
        {
            return new(mode, firstPage, string.Empty, continuations)
            {
                EffectiveAlignment = narrativeAlignment,
                SideAlignment = fullWidthAlignment,
                BelowAlignment = fullWidthAlignment
            };
        }

        if (mode == CompendiumBalancedTextFlowMode.SideColumn)
        {
            var used = EstimateTextHeight(firstPage, narrativeFontScale, includeHeading: true);
            var region = Math.Max(120f, primaryImageHeightPoints);
            return new(mode, firstPage, string.Empty, continuations)
            {
                EffectiveAlignment = narrativeAlignment,
                SideAlignment = sideAlignment,
                BelowAlignment = fullWidthAlignment,
                SideRegionHeightPoints = region,
                SideUsedHeightPoints = Math.Min(region, used),
                SideRemainingHeightPoints = Math.Max(0f, region - used),
                SideUtilizationRatio = region <= 0 ? 1f : Math.Clamp(used / region, 0f, 1f)
            };
        }

        var assessment = AssessSideFlow(firstPage, primaryImageHeightPoints, narrativeFontScale);
        return new(mode, assessment.SideSegment, assessment.BelowSegment, continuations)
        {
            EffectiveAlignment = narrativeAlignment,
            SideAlignment = sideAlignment,
            BelowAlignment = fullWidthAlignment,
            SideRegionHeightPoints = assessment.RegionHeightPoints,
            SideUsedHeightPoints = assessment.UsedHeightPoints,
            SideRemainingHeightPoints = assessment.RemainingHeightPoints,
            SideUtilizationRatio = assessment.UtilizationRatio
        };
    }

    /// <summary>
    /// Evaluates how naturally a first-page narrative fills the right-hand region beside a Balanced
    /// image. Pagination scoring uses the same assessment that later produces the physical segments.
    /// </summary>
    public static SideAssessment AssessSideFlow(
        string? firstPageNarrative,
        float imageHeightPoints,
        float narrativeFontScale)
    {
        var clean = Normalize(firstPageNarrative);
        var region = Math.Max(120f, imageHeightPoints);
        if (clean.Length == 0)
            return new(string.Empty, string.Empty, region, 0f, region, 0f);

        var sideBudget = ResolveSideBudget(region, narrativeFontScale);
        var split = SplitAtNaturalBoundary(clean, sideBudget);
        var used = EstimateTextHeight(split.Head, narrativeFontScale, includeHeading: true);
        used = Math.Min(region, used);
        var remaining = Math.Max(0f, region - used);
        return new(
            split.Head,
            split.Tail,
            region,
            used,
            remaining,
            region <= 0f ? 1f : Math.Clamp(used / region, 0f, 1f));
    }

    public static IReadOnlyList<string> SplitNatural(string? narrative, int firstBudget, int continuationBudget)
    {
        var clean = Normalize(narrative);
        if (clean.Length == 0) return new[] { string.Empty };

        var pages = new List<string>();
        var currentParagraphs = new List<string>();
        var budget = Math.Max(1, firstBudget);

        void Flush()
        {
            if (currentParagraphs.Count == 0) return;
            pages.Add(string.Join("\n\n", currentParagraphs).Trim());
            currentParagraphs.Clear();
            budget = Math.Max(1, continuationBudget);
        }

        foreach (var paragraph in GetParagraphs(clean))
        {
            var used = JoinedLength(currentParagraphs, "\n\n");
            var separator = currentParagraphs.Count == 0 ? 0 : 2;
            if (used + separator + paragraph.Length <= budget)
            {
                currentParagraphs.Add(paragraph);
                continue;
            }

            var remainingBudget = Math.Max(0, budget - used - separator);
            var sentenceSplit = SplitParagraphAtSentenceBoundary(paragraph, remainingBudget);
            if (!string.IsNullOrWhiteSpace(sentenceSplit.Head))
            {
                currentParagraphs.Add(sentenceSplit.Head);
                Flush();
                if (!string.IsNullOrWhiteSpace(sentenceSplit.Tail))
                {
                    AddOversizedParagraph(sentenceSplit.Tail, pages, currentParagraphs, ref budget, continuationBudget);
                }
                continue;
            }

            if (currentParagraphs.Count > 0)
            {
                Flush();
                if (paragraph.Length <= budget)
                {
                    currentParagraphs.Add(paragraph);
                    continue;
                }
            }

            AddOversizedParagraph(paragraph, pages, currentParagraphs, ref budget, continuationBudget);
        }

        Flush();
        return pages.Count == 0 ? new[] { clean } : pages;
    }

    private static void AddOversizedParagraph(
        string paragraph,
        List<string> pages,
        List<string> currentParagraphs,
        ref int budget,
        int continuationBudget)
    {
        var remaining = paragraph.Trim();
        while (remaining.Length > 0)
        {
            if (remaining.Length <= budget)
            {
                currentParagraphs.Add(remaining);
                return;
            }

            var split = SplitParagraphAtSentenceBoundary(remaining, budget);
            if (string.IsNullOrWhiteSpace(split.Head))
            {
                // An unpunctuated or single oversized sentence remains intact. Editorial content
                // is never word/character sliced merely to satisfy the heuristic.
                currentParagraphs.Add(remaining);
                pages.Add(string.Join("\n\n", currentParagraphs).Trim());
                currentParagraphs.Clear();
                budget = Math.Max(1, continuationBudget);
                return;
            }

            currentParagraphs.Add(split.Head);
            pages.Add(string.Join("\n\n", currentParagraphs).Trim());
            currentParagraphs.Clear();
            budget = Math.Max(1, continuationBudget);
            remaining = split.Tail;
        }
    }

    private static (string Head, string Tail) SplitAtNaturalBoundary(string text, int budget)
    {
        if (text.Length <= budget) return (text, string.Empty);
        var paragraphs = GetParagraphs(text);
        if (paragraphs.Count == 0) return (string.Empty, text);

        var head = new List<string>();
        var used = 0;
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            var separator = head.Count == 0 ? 0 : 2;
            var projected = used + separator + paragraph.Length;
            if (projected <= budget)
            {
                head.Add(paragraph);
                used = projected;
                continue;
            }

            // Phase 37 improvement: after intact paragraphs, consume complete sentences from the
            // next paragraph when they fit. This is what eliminates the large blank side-column gap.
            var remainingBudget = Math.Max(0, budget - used - separator);
            var sentenceSplit = SplitParagraphAtSentenceBoundary(paragraph, remainingBudget);
            if (!string.IsNullOrWhiteSpace(sentenceSplit.Head))
            {
                head.Add(sentenceSplit.Head);
                var tailParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(sentenceSplit.Tail)) tailParts.Add(sentenceSplit.Tail);
                tailParts.AddRange(paragraphs.Skip(index + 1));
                return (
                    string.Join("\n\n", head).Trim(),
                    string.Join("\n\n", tailParts.Where(value => value.Length > 0)).Trim());
            }

            return (
                string.Join("\n\n", head).Trim(),
                string.Join("\n\n", paragraphs.Skip(index)).Trim());
        }

        return (string.Join("\n\n", head).Trim(), string.Empty);
    }

    private static (string Head, string Tail) SplitParagraphAtSentenceBoundary(string paragraph, int budget)
    {
        if (budget <= 0) return (string.Empty, paragraph.Trim());
        if (paragraph.Length <= budget) return (paragraph.Trim(), string.Empty);
        var sentences = GetSentences(paragraph);
        if (sentences.Count <= 1) return (string.Empty, paragraph.Trim());

        var head = new List<string>();
        var used = 0;
        var index = 0;
        for (; index < sentences.Count; index++)
        {
            var sentence = sentences[index];
            var projected = used + (head.Count == 0 ? 0 : 1) + sentence.Length;
            if (projected > budget) break;
            head.Add(sentence);
            used = projected;
        }

        if (head.Count == 0) return (string.Empty, paragraph.Trim());
        return (
            string.Join(" ", head).Trim(),
            string.Join(" ", sentences.Skip(index)).Trim());
    }

    private static int ResolveSideBudget(float imageHeightPoints, float narrativeFontScale)
    {
        var scale = Math.Clamp(narrativeFontScale, 1f, 1.10f);
        var usableHeight = Math.Max(70f, Math.Max(120f, imageHeightPoints) - SideHeadingReservePoints);
        var usableLines = Math.Max(5, (int)Math.Floor(usableHeight / (NarrativeLineHeightPoints * scale)));
        // A small safety factor keeps the deterministic character estimate on the conservative side.
        return Math.Clamp((int)Math.Floor(usableLines * SideCharactersPerLine * .965d), 220, 1100);
    }

    private static float EstimateTextHeight(string? text, float narrativeFontScale, bool includeHeading)
    {
        var clean = Normalize(text);
        if (clean.Length == 0) return 0f;
        var scale = Math.Clamp(narrativeFontScale, 1f, 1.10f);
        var paragraphs = GetParagraphs(clean);
        var lines = paragraphs.Sum(paragraph => Math.Max(1, (int)Math.Ceiling(paragraph.Length / SideCharactersPerLine)));
        return (includeHeading ? SideHeadingReservePoints : 0f)
               + lines * NarrativeLineHeightPoints * scale
               + Math.Max(0, paragraphs.Count - 1) * ParagraphGapPoints;
    }

    private static int JoinedLength(IReadOnlyList<string> values, string separator)
        => values.Count == 0 ? 0 : values.Sum(value => value.Length) + separator.Length * Math.Max(0, values.Count - 1);

    private static IReadOnlyList<string> GetParagraphs(string text)
        => ParagraphBreak.Split(text)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();

    private static IReadOnlyList<string> GetSentences(string paragraph)
    {
        var raw = SentenceBreak.Split(paragraph)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (raw.Length <= 1) return raw;

        var result = new List<string>();
        var pending = raw[0];
        for (var index = 1; index < raw.Length; index++)
        {
            if (EndsWithNonTerminalAbbreviation(pending))
            {
                pending = $"{pending} {raw[index]}".Trim();
                continue;
            }

            result.Add(pending);
            pending = raw[index];
        }
        result.Add(pending);
        return result;
    }

    private static bool EndsWithNonTerminalAbbreviation(string value)
    {
        var trimmed = value.TrimEnd();
        var finalToken = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        return NonTerminalAbbreviations.Contains(finalToken) || InitialismEnding.IsMatch(finalToken);
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim();
}
