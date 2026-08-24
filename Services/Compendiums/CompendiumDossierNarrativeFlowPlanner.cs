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
/// Deterministic narrative segmentation for Compendium dossier pages. Phase 37.3 keeps the
/// paragraph-first / sentence-second editorial rule, adding complete sentences sentence-by-sentence when they
/// physically fit. Decisions use DM Sans measurements at the actual side-column width; words and
/// sentences are never sliced.
/// </summary>
public static class CompendiumDossierNarrativeFlowPlanner
{
    private const int ContinuationBudget = 3300;
    private const float FullNarrativeWidthPoints = 519f;

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
        public bool HasExcessiveGap => RemainingHeightPoints > CompendiumDossierEditorialPolicy.MaximumFlowBelowGapPoints && !string.IsNullOrWhiteSpace(BelowSegment);
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
        float sideColumnWidthPoints = 223f,
        float firstPageNarrativeHeightPoints = 0f)
    {
        var clean = Normalize(narrative);
        narrativeAlignment = CompendiumNarrativeTypographyPolicy.Normalize(narrativeAlignment);
        if (clean.Length == 0)
        {
            return CompendiumDossierNarrativeFlowPlan.Empty with
            {
                Mode = mode,
                EffectiveAlignment = narrativeAlignment,
                SideAlignment = CompendiumNarrativeTypographyPolicy.ResolveAlignment(
                    narrativeAlignment,
                    CompendiumNarrativeSegment.BalancedSide),
                BelowAlignment = CompendiumNarrativeTypographyPolicy.ResolveAlignment(
                    narrativeAlignment,
                    CompendiumNarrativeSegment.BelowImage)
            };
        }

        var fullWidthAlignment = CompendiumNarrativeTypographyPolicy.ResolveAlignment(
            narrativeAlignment,
            CompendiumNarrativeSegment.FullWidth);
        var sideAlignment = CompendiumNarrativeTypographyPolicy.ResolveAlignment(
            narrativeAlignment,
            CompendiumNarrativeSegment.BalancedSide);
        var physicalFirstPage = firstPageNarrativeHeightPoints > .5f;
        var measurementSession = physicalFirstPage ? new CompendiumDossierTextMeasurementService.Session() : null;

        if (layout != CompendiumDossierLayout.Balanced || !hasPrimaryImage)
        {
            if (physicalFirstPage)
            {
                var split = SplitToMeasuredHeight(
                    clean,
                    firstPageNarrativeHeightPoints,
                    FullNarrativeWidthPoints,
                    narrativeFontScale,
                    measurementSession!,
                    includeHeading: true);
                var firstPage = split.Head;
                var continuations = SplitForPhysicalPages(
                    split.Tail,
                    FullNarrativeWidthPoints,
                    CompendiumPublicationNotePolicy.ContinuationBodyHeightPoints,
                    narrativeFontScale,
                    includeHeading: false);
                return new(mode, firstPage, string.Empty, continuations)
                {
                    EffectiveAlignment = narrativeAlignment,
                    SideAlignment = fullWidthAlignment,
                    BelowAlignment = fullWidthAlignment
                };
            }

            var chunks = SplitNatural(clean, Math.Max(760, firstPageNarrativeBudget), ContinuationBudget);
            return new(mode, chunks[0], string.Empty, chunks.Skip(1).ToArray())
            {
                EffectiveAlignment = narrativeAlignment,
                SideAlignment = fullWidthAlignment,
                BelowAlignment = fullWidthAlignment
            };
        }

        if (mode == CompendiumBalancedTextFlowMode.SideColumn)
        {
            string firstPage;
            IReadOnlyList<string> continuations;
            if (physicalFirstPage)
            {
                var split = SplitToMeasuredHeight(
                    clean,
                    firstPageNarrativeHeightPoints,
                    sideColumnWidthPoints,
                    narrativeFontScale,
                    measurementSession!,
                    includeHeading: true);
                firstPage = split.Head;
                continuations = SplitForPhysicalPages(
                    split.Tail,
                    FullNarrativeWidthPoints,
                    CompendiumPublicationNotePolicy.ContinuationBodyHeightPoints,
                    narrativeFontScale,
                    includeHeading: false);
            }
            else
            {
                var chunks = SplitNatural(clean, Math.Max(760, firstPageNarrativeBudget), ContinuationBudget);
                firstPage = chunks[0];
                continuations = chunks.Skip(1).ToArray();
            }

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

        if (physicalFirstPage)
        {
            var assessment = AssessSideFlow(
                clean,
                primaryImageHeightPoints,
                narrativeFontScale,
                sideColumnWidthPoints,
                measurementSession);
            var belowSplit = SplitToMeasuredHeight(
                assessment.BelowSegment,
                firstPageNarrativeHeightPoints,
                FullNarrativeWidthPoints,
                narrativeFontScale,
                measurementSession!,
                includeHeading: false);
            var continuations = SplitForPhysicalPages(
                belowSplit.Tail,
                FullNarrativeWidthPoints,
                CompendiumPublicationNotePolicy.ContinuationBodyHeightPoints,
                narrativeFontScale,
                includeHeading: false);
            return new(mode, assessment.SideSegment, belowSplit.Head, continuations)
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

        var legacyChunks = SplitNatural(clean, Math.Max(760, firstPageNarrativeBudget), ContinuationBudget);
        var legacyFirstPage = legacyChunks[0];
        var legacyContinuations = legacyChunks.Skip(1).ToArray();
        var legacyAssessment = AssessSideFlow(legacyFirstPage, primaryImageHeightPoints, narrativeFontScale, sideColumnWidthPoints);
        return new(mode, legacyAssessment.SideSegment, legacyAssessment.BelowSegment, legacyContinuations)
        {
            EffectiveAlignment = narrativeAlignment,
            SideAlignment = sideAlignment,
            BelowAlignment = fullWidthAlignment,
            SideRegionHeightPoints = legacyAssessment.RegionHeightPoints,
            SideUsedHeightPoints = legacyAssessment.UsedHeightPoints,
            SideRemainingHeightPoints = legacyAssessment.RemainingHeightPoints,
            SideUtilizationRatio = legacyAssessment.UtilizationRatio
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

    /// <summary>
    /// Splits an auxiliary publication block into physical pages using paragraph/sentence boundaries.
    /// A pathological single oversized sentence is kept intact; publication text is never word- or
    /// character-sliced simply to satisfy pagination.
    /// </summary>
    public static IReadOnlyList<string> SplitForPhysicalPages(
        string? text,
        float widthPoints,
        float pageHeightPoints,
        float narrativeFontScale = 1f,
        bool includeHeading = false,
        bool allowMinorHeadings = true)
    {
        var remaining = Normalize(text);
        if (remaining.Length == 0) return Array.Empty<string>();
        var result = new List<string>();
        var measurement = new CompendiumDossierTextMeasurementService.Session();
        var guard = 0;
        while (remaining.Length > 0 && guard++ < 100)
        {
            var split = SplitToMeasuredHeight(
                remaining,
                pageHeightPoints,
                widthPoints,
                narrativeFontScale,
                measurement,
                includeHeading,
                allowMinorHeadings);
            if (!string.IsNullOrWhiteSpace(split.Head))
            {
                result.Add(split.Head.Trim());
                remaining = split.Tail.Trim();
                continue;
            }

            result.Add(remaining);
            break;
        }
        return result;
    }

    private static (string Head, string Tail) SplitToMeasuredHeight(
        string text,
        float availableHeightPoints,
        float widthPoints,
        float narrativeFontScale,
        CompendiumDossierTextMeasurementService.Session measurementSession,
        bool includeHeading = true,
        bool allowMinorHeadings = true)
    {
        var document = CompendiumNarrativeParser.Parse(text, allowMinorHeadings);
        if (document.IsEmpty) return (string.Empty, text);

        var blocks = document.Blocks;
        var accepted = new List<CompendiumNarrativeBlock>();

        bool FitsCandidate(IEnumerable<CompendiumNarrativeBlock> candidateBlocks)
        {
            var candidate = new CompendiumNarrativeDocument(candidateBlocks.ToArray()).ToMarkdown();
            return measurementSession.Fits(
                candidate,
                widthPoints,
                availableHeightPoints,
                narrativeFontScale,
                includeHeading: includeHeading,
                allowMinorHeadings: allowMinorHeadings);
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (block.Kind == CompendiumNarrativeBlockKind.MinorHeading)
            {
                // Never strand a semantic heading at the bottom of a page. The planner only accepts
                // it when the first meaningful unit of the following block also fits.
                var keepUnit = index + 1 < blocks.Count ? FirstKeepWithNextUnit(blocks[index + 1]) : null;
                var keepCandidate = keepUnit is null
                    ? accepted.Append(block)
                    : accepted.Concat(new[] { block, keepUnit });
                if (FitsCandidate(keepCandidate))
                {
                    accepted.Add(block);
                    continue;
                }

                return (
                    new CompendiumNarrativeDocument(accepted).ToMarkdown(),
                    new CompendiumNarrativeDocument(blocks.Skip(index).ToArray()).ToMarkdown());
            }

            if (FitsCandidate(accepted.Append(block)))
            {
                accepted.Add(block);
                continue;
            }

            if (block.Kind == CompendiumNarrativeBlockKind.BulletList)
            {
                var acceptedItems = new List<string>();
                foreach (var item in block.Items)
                {
                    var candidateList = CompendiumNarrativeBlock.BulletList(acceptedItems.Append(item).ToArray());
                    if (!FitsCandidate(accepted.Append(candidateList))) break;
                    acceptedItems.Add(item);
                }

                if (acceptedItems.Count > 0)
                {
                    accepted.Add(CompendiumNarrativeBlock.BulletList(acceptedItems.ToArray()));
                    var tail = new List<CompendiumNarrativeBlock>();
                    var remainingItems = block.Items.Skip(acceptedItems.Count).ToArray();
                    if (remainingItems.Length > 0)
                        tail.Add(CompendiumNarrativeBlock.BulletList(remainingItems));
                    tail.AddRange(blocks.Skip(index + 1));
                    return (
                        new CompendiumNarrativeDocument(accepted).ToMarkdown(),
                        new CompendiumNarrativeDocument(tail).ToMarkdown());
                }

                return (
                    new CompendiumNarrativeDocument(accepted).ToMarkdown(),
                    new CompendiumNarrativeDocument(blocks.Skip(index).ToArray()).ToMarkdown());
            }

            // Paragraphs remain paragraph-first and sentence-second. Inline emphasis markers are
            // retained verbatim; only complete sentence units move between physical pages.
            var sentences = GetSentences(block.Markdown);
            var acceptedSentences = new List<string>();
            foreach (var sentence in sentences)
            {
                var partial = CompendiumNarrativeBlock.Paragraph(
                    string.Join(" ", acceptedSentences.Append(sentence)).Trim());
                if (!FitsCandidate(accepted.Append(partial))) break;
                acceptedSentences.Add(sentence);
            }

            if (acceptedSentences.Count > 0)
            {
                accepted.Add(CompendiumNarrativeBlock.Paragraph(string.Join(" ", acceptedSentences)));
                var tail = new List<CompendiumNarrativeBlock>();
                var remainingSentenceText = string.Join(" ", sentences.Skip(acceptedSentences.Count)).Trim();
                if (remainingSentenceText.Length > 0)
                    tail.Add(CompendiumNarrativeBlock.Paragraph(remainingSentenceText));
                tail.AddRange(blocks.Skip(index + 1));
                return (
                    new CompendiumNarrativeDocument(accepted).ToMarkdown(),
                    new CompendiumNarrativeDocument(tail).ToMarkdown());
            }

            return (
                new CompendiumNarrativeDocument(accepted).ToMarkdown(),
                new CompendiumNarrativeDocument(blocks.Skip(index).ToArray()).ToMarkdown());
        }

        return (new CompendiumNarrativeDocument(accepted).ToMarkdown(), string.Empty);
    }

    private static CompendiumNarrativeBlock? FirstKeepWithNextUnit(CompendiumNarrativeBlock block)
    {
        if (block.Kind == CompendiumNarrativeBlockKind.BulletList)
        {
            var first = block.Items.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
            return string.IsNullOrWhiteSpace(first)
                ? null
                : CompendiumNarrativeBlock.BulletList(new[] { first });
        }

        if (block.Kind == CompendiumNarrativeBlockKind.Paragraph)
        {
            var firstSentence = GetSentences(block.Markdown).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstSentence)
                ? null
                : CompendiumNarrativeBlock.Paragraph(firstSentence);
        }

        return block;
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
