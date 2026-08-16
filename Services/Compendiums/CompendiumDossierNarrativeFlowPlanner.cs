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
    public float SideOverflowHeightPoints { get; init; }
    public float SideBalanceRatio { get; init; } = 1f;
    public float SideUtilizationRatio { get; init; }
    public int EstimatedPageCount => 1 + ContinuationSegments.Count;
    public bool Continues => ContinuationSegments.Count > 0;
}

/// <summary>
/// Deterministic narrative segmentation for Compendium dossier pages. Phase 37.2 keeps the
/// paragraph-first / sentence-second editorial rule, adding complete sentences sentence-by-sentence when they
/// physically fit. Decisions use DM Sans measurements at the actual side-column width; words and
/// sentences are never sliced.
/// </summary>
public static class CompendiumDossierNarrativeFlowPlanner
{
    private const int ContinuationBudget = 3300;

    private static readonly Regex ParagraphBreak = new(@"\n\s*\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InitialismEnding = new(@"(?:\b[A-Za-z]\.){2,}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> NonTerminalAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "e.g.", "i.e.", "dr.", "mr.", "mrs.", "ms.", "prof.", "no.", "nos.", "fig.", "ref.", "refs.", "para.", "sec.",
        "stn.", "bn.", "regt.", "sqn.", "inf.", "arty.", "eqpt.", "wpn.", "dept.", "div.", "bde.", "hq.", "approx."
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
            var measuredHeight = CompendiumDossierTextMeasurementService.Measure(
                firstPage, sideColumnWidthPoints, narrativeFontScale, includeHeading: true).HeightPoints;
            var region = Math.Max(1f, primaryImageHeightPoints);
            var balance = CompendiumDossierEditorialPolicy.AssessSideColumn(region, measuredHeight);
            return new(mode, firstPage, string.Empty, continuations)
            {
                EffectiveAlignment = narrativeAlignment,
                SideAlignment = sideAlignment,
                BelowAlignment = fullWidthAlignment,
                SideRegionHeightPoints = region,
                SideUsedHeightPoints = Math.Min(region, measuredHeight),
                SideRemainingHeightPoints = balance.UnderfillHeightPoints,
                SideOverflowHeightPoints = balance.OverflowHeightPoints,
                SideBalanceRatio = balance.BalanceRatio,
                SideUtilizationRatio = region <= 0 ? 1f : Math.Clamp(measuredHeight / region, 0f, 1f)
            };
        }

        var assessment = AssessSideFlow(firstPage, primaryImageHeightPoints, narrativeFontScale, sideColumnWidthPoints);
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
        float narrativeFontScale,
        float sideColumnWidthPoints = 223f,
        CompendiumDossierTextMeasurementService.Session? measurementSession = null)
    {
        var clean = Normalize(firstPageNarrative);
        var region = Math.Max(1f, imageHeightPoints);
        if (clean.Length == 0)
            return new(string.Empty, string.Empty, region, 0f, region, 0f);

        measurementSession ??= new CompendiumDossierTextMeasurementService.Session();
        var split = SplitToMeasuredHeight(clean, region, sideColumnWidthPoints, narrativeFontScale, measurementSession);
        var used = measurementSession.Measure(
            split.Head, sideColumnWidthPoints, narrativeFontScale, includeHeading: true).HeightPoints;
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

    private static (string Head, string Tail) SplitToMeasuredHeight(
        string text,
        float availableHeightPoints,
        float widthPoints,
        float narrativeFontScale,
        CompendiumDossierTextMeasurementService.Session measurementSession)
    {
        var paragraphs = GetParagraphs(text);
        if (paragraphs.Count == 0) return (string.Empty, text);

        var head = new List<string>();
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            var wholeCandidate = JoinParagraphs(head.Append(paragraph));
            if (measurementSession.Fits(
                    wholeCandidate, widthPoints, availableHeightPoints, narrativeFontScale, includeHeading: true))
            {
                head.Add(paragraph);
                continue;
            }

            var sentences = GetSentences(paragraph);
            var acceptedSentences = new List<string>();
            for (var sentenceIndex = 0; sentenceIndex < sentences.Count; sentenceIndex++)
            {
                var sentenceCandidate = string.Join(" ", acceptedSentences.Append(sentences[sentenceIndex])).Trim();
                var candidateParagraphs = head.Concat(new[] { sentenceCandidate });
                var candidate = JoinParagraphs(candidateParagraphs);
                if (!measurementSession.Fits(
                        candidate, widthPoints, availableHeightPoints, narrativeFontScale, includeHeading: true))
                {
                    break;
                }

                acceptedSentences.Add(sentences[sentenceIndex]);
            }

            if (acceptedSentences.Count > 0)
            {
                head.Add(string.Join(" ", acceptedSentences));
                var tail = new List<string>();
                var remainingSentenceText = string.Join(" ", sentences.Skip(acceptedSentences.Count)).Trim();
                if (remainingSentenceText.Length > 0) tail.Add(remainingSentenceText);
                tail.AddRange(paragraphs.Skip(index + 1));
                return (JoinParagraphs(head), JoinParagraphs(tail));
            }

            return (JoinParagraphs(head), JoinParagraphs(paragraphs.Skip(index)));
        }

        return (JoinParagraphs(head), string.Empty);
    }

    private static string JoinParagraphs(IEnumerable<string> paragraphs)
        => string.Join("\n\n", paragraphs.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())).Trim();

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
