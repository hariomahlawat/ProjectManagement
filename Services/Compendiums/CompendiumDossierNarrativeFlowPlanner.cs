using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Compendiums;

public sealed record CompendiumDossierNarrativeFlowPlan(
    CompendiumBalancedTextFlowMode Mode,
    string SideSegment,
    string BelowImageSegment,
    IReadOnlyList<string> ContinuationSegments)
{
    public int EstimatedPageCount => 1 + ContinuationSegments.Count;
    public bool Continues => ContinuationSegments.Count > 0;
}

/// <summary>
/// Deterministic narrative segmentation for Compendium dossier pages. It only splits at paragraph
/// or sentence boundaries; the same segments are serialized to the browser proof and consumed by
/// the physical PDF page planner.
/// </summary>
public static class CompendiumDossierNarrativeFlowPlanner
{
    private const int ContinuationBudget = 3300;
    private static readonly Regex ParagraphBreak = new(@"\n\s*\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?])\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static CompendiumDossierNarrativeFlowPlan Resolve(
        string? narrative,
        CompendiumBalancedTextFlowMode mode,
        CompendiumDossierLayout layout,
        bool hasPrimaryImage,
        float primaryImageHeightPoints,
        float narrativeFontScale,
        int firstPageNarrativeBudget)
    {
        var clean = Normalize(narrative);
        if (clean.Length == 0)
            return new(mode, string.Empty, string.Empty, Array.Empty<string>());

        var chunks = SplitNatural(clean, Math.Max(760, firstPageNarrativeBudget), ContinuationBudget);
        var firstPage = chunks[0];
        var continuations = chunks.Skip(1).ToArray();

        if (layout != CompendiumDossierLayout.Balanced || !hasPrimaryImage)
            return new(mode, firstPage, string.Empty, continuations);

        if (mode == CompendiumBalancedTextFlowMode.SideColumn)
            return new(mode, firstPage, string.Empty, continuations);

        var sideBudget = ResolveSideBudget(primaryImageHeightPoints, narrativeFontScale);
        var sideSplit = SplitAtNaturalBoundary(firstPage, sideBudget);
        return new(mode, sideSplit.Head, sideSplit.Tail, continuations);
    }

    public static IReadOnlyList<string> SplitNatural(string? narrative, int firstBudget, int continuationBudget)
    {
        var clean = Normalize(narrative);
        if (clean.Length == 0) return new[] { string.Empty };

        var result = new List<string>();
        var page = new List<string>();
        var budget = Math.Max(1, firstBudget);

        void FlushPage()
        {
            if (page.Count == 0) return;
            result.Add(string.Join("\n\n", page).Trim());
            page.Clear();
            budget = Math.Max(1, continuationBudget);
        }

        foreach (var paragraph in GetParagraphs(clean))
        {
            var used = page.Count == 0 ? 0 : string.Join("\n\n", page).Length;
            var projected = used + (page.Count == 0 ? 0 : 2) + paragraph.Length;
            if (projected <= budget)
            {
                page.Add(paragraph);
                continue;
            }

            // Prefer moving an intact paragraph to the next page rather than consuming the
            // remaining space by splitting it. Only paragraphs that cannot fit on a fresh page
            // are decomposed at sentence boundaries.
            if (page.Count > 0)
            {
                FlushPage();
                if (paragraph.Length <= budget)
                {
                    page.Add(paragraph);
                    continue;
                }
            }

            var sentences = GetSentences(paragraph);
            if (sentences.Count <= 1)
            {
                // A single oversized sentence (or an unpunctuated requirement) is kept intact.
                // It may overflow the target budget, but editorial content is never word- or
                // character-sliced merely to satisfy a heuristic.
                page.Add(paragraph);
                FlushPage();
                continue;
            }

            var sentencePage = new List<string>();
            foreach (var sentence in sentences)
            {
                var sentenceUsed = sentencePage.Count == 0 ? 0 : string.Join(" ", sentencePage).Length;
                var sentenceProjected = sentenceUsed + (sentencePage.Count == 0 ? 0 : 1) + sentence.Length;
                if (sentencePage.Count > 0 && sentenceProjected > budget)
                {
                    result.Add(string.Join(" ", sentencePage).Trim());
                    sentencePage.Clear();
                    budget = Math.Max(1, continuationBudget);
                }
                sentencePage.Add(sentence);
            }

            if (sentencePage.Count > 0)
            {
                page.Add(string.Join(" ", sentencePage).Trim());
            }
        }

        FlushPage();
        return result.Count == 0 ? new[] { clean } : result;
    }

    private static (string Head, string Tail) SplitAtNaturalBoundary(string text, int budget)
    {
        if (text.Length <= budget) return (text, string.Empty);
        var paragraphs = GetParagraphs(text);
        if (paragraphs.Count == 0) return (string.Empty, text);

        var headParagraphs = new List<string>();
        var used = 0;
        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            var projected = used + (headParagraphs.Count == 0 ? 0 : 2) + paragraph.Length;
            if (projected <= budget)
            {
                headParagraphs.Add(paragraph);
                used = projected;
                continue;
            }

            if (headParagraphs.Count > 0)
            {
                return (
                    string.Join("\n\n", headParagraphs).Trim(),
                    string.Join("\n\n", paragraphs.Skip(index)).Trim());
            }

            // The first paragraph itself is too large for the side column. Use sentence
            // boundaries; if the first sentence is too large, leave the complete paragraph for
            // the full-width region below the image instead of slicing words or characters.
            var sentences = GetSentences(paragraph);
            if (sentences.Count <= 1) return (string.Empty, text);

            var headSentences = new List<string>();
            var sentenceUsed = 0;
            var sentenceIndex = 0;
            for (; sentenceIndex < sentences.Count; sentenceIndex++)
            {
                var sentence = sentences[sentenceIndex];
                var sentenceProjected = sentenceUsed + (headSentences.Count == 0 ? 0 : 1) + sentence.Length;
                if (headSentences.Count > 0 && sentenceProjected > budget) break;
                if (headSentences.Count == 0 && sentence.Length > budget) return (string.Empty, text);
                headSentences.Add(sentence);
                sentenceUsed = sentenceProjected;
            }

            var tailParts = new List<string>();
            if (sentenceIndex < sentences.Count)
                tailParts.Add(string.Join(" ", sentences.Skip(sentenceIndex)).Trim());
            tailParts.AddRange(paragraphs.Skip(index + 1));
            return (
                string.Join(" ", headSentences).Trim(),
                string.Join("\n\n", tailParts.Where(value => value.Length > 0)).Trim());
        }

        return (string.Join("\n\n", headParagraphs).Trim(), string.Empty);
    }

    private static IReadOnlyList<string> GetParagraphs(string text)
        => ParagraphBreak.Split(text)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();

    private static IReadOnlyList<string> GetSentences(string paragraph)
        => SentenceBreak.Split(paragraph)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();

    private static int ResolveSideBudget(float imageHeightPoints, float narrativeFontScale)
    {
        var scale = Math.Clamp(narrativeFontScale, 1f, 1.08f);
        var usableLines = Math.Max(7, (int)Math.Floor((Math.Max(120f, imageHeightPoints) - 34f) / (12.5f * scale)));
        return Math.Clamp((int)Math.Floor(usableLines * 39d), 260, 900);
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim();
}
