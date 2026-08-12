using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Publications;

/// <summary>
/// Creates a deterministic, publication-aware A4 page plan while preserving the user's
/// project order. The planner uses dynamic programming rather than a greedy first-fit
/// strategy so that visually weak orphans such as 4+1 are avoided when 3+2 is available.
/// </summary>
public static partial class BrochureLayoutPlanner
{
    public const int FourProjectMaximumWords = 85;
    public const int ThreeProjectMaximumWords = 125;
    public const int TwoFeatureMaximumWords = 185;
    public const int LongNarrativeChunkWords = 210;

    public static IReadOnlyList<BrochurePagePlan> Plan(IReadOnlyList<BrochurePublicationProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var fragments = projects.SelectMany(CreateFragments).ToArray();
        if (fragments.Length == 0)
        {
            return Array.Empty<BrochurePagePlan>();
        }

        var result = new List<BrochurePagePlan>();
        var segment = new List<BrochureProjectFragment>();

        foreach (var fragment in fragments)
        {
            if (IsForcedFeature(fragment))
            {
                FlushSegment();
                result.Add(new BrochurePagePlan(
                    BrochurePageLayoutKind.SingleFeature,
                    new[] { fragment }));
                continue;
            }

            segment.Add(fragment);
        }

        FlushSegment();
        return result;

        void FlushSegment()
        {
            if (segment.Count == 0)
            {
                return;
            }

            result.AddRange(PlanRegularSegment(segment));
            segment.Clear();
        }
    }

    /// <summary>
    /// Screen-first planner for the Digital / Comfortable profile. It deliberately limits
    /// composition to one or two projects per page; readability and image prominence win
    /// over page-count minimisation. Project order remains authoritative.
    /// </summary>
    public static IReadOnlyList<BrochurePagePlan> PlanDigitalComfortable(
        IReadOnlyList<BrochurePublicationProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var fragments = projects.SelectMany(CreateFragments).ToArray();
        if (fragments.Length == 0)
        {
            return Array.Empty<BrochurePagePlan>();
        }

        var pages = new List<BrochurePagePlan>();
        var index = 0;
        while (index < fragments.Length)
        {
            var current = fragments[index];
            if (IsDigitalForcedFeature(current))
            {
                pages.Add(new BrochurePagePlan(
                    BrochurePageLayoutKind.SingleFeature,
                    new[] { current }));
                index++;
                continue;
            }

            if (index + 1 < fragments.Length)
            {
                var next = fragments[index + 1];
                if (CanUseDigitalPair(current, next))
                {
                    pages.Add(new BrochurePagePlan(
                        BrochurePageLayoutKind.TwoFeature,
                        new[] { current, next }));
                    index += 2;
                    continue;
                }
            }

            pages.Add(new BrochurePagePlan(
                BrochurePageLayoutKind.SingleFeature,
                new[] { current }));
            index++;
        }

        return pages;
    }

    private static bool IsDigitalForcedFeature(BrochureProjectFragment fragment)
        => fragment.FragmentCount > 1
           || fragment.IsContinuation
           || fragment.NarrativeWordCount > TwoFeatureMaximumWords
           || fragment.Project.ImageMode == BrochureImageMode.GalleryTwo;

    private static bool CanUseDigitalPair(
        BrochureProjectFragment first,
        BrochureProjectFragment second)
    {
        if (IsDigitalForcedFeature(first) || IsDigitalForcedFeature(second))
        {
            return false;
        }

        const int maximumCombinedWords = 350;
        const int maximumTitleLength = 135;
        return first.NarrativeWordCount + second.NarrativeWordCount <= maximumCombinedWords
               && first.Project.ProjectName.Length <= maximumTitleLength
               && second.Project.ProjectName.Length <= maximumTitleLength;
    }

    public static int CountWords(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? 0
            : WordRegex().Matches(value).Count;

    private static IReadOnlyList<BrochurePagePlan> PlanRegularSegment(
        IReadOnlyList<BrochureProjectFragment> fragments)
    {
        var count = fragments.Count;
        var bestCost = Enumerable.Repeat(double.PositiveInfinity, count + 1).ToArray();
        var bestChoice = new PageChoice?[count + 1];
        bestCost[count] = 0;

        for (var index = count - 1; index >= 0; index--)
        {
            foreach (var candidate in EnumerateCandidates(fragments, index))
            {
                var nextIndex = index + candidate.ItemCount;
                var cost = candidate.Penalty + bestCost[nextIndex];
                if (cost + .0001 < bestCost[index]
                    || (Math.Abs(cost - bestCost[index]) < .0001
                        && PreferCandidate(candidate, bestChoice[index])))
                {
                    bestCost[index] = cost;
                    bestChoice[index] = candidate;
                }
            }
        }

        var pages = new List<BrochurePagePlan>();
        var cursor = 0;
        while (cursor < count)
        {
            var choice = bestChoice[cursor] ?? new PageChoice(
                BrochurePageLayoutKind.SingleFeature,
                1,
                50);
            pages.Add(new BrochurePagePlan(
                choice.Layout,
                fragments.Skip(cursor).Take(choice.ItemCount).ToArray()));
            cursor += choice.ItemCount;
        }

        return pages;
    }

    private static IEnumerable<PageChoice> EnumerateCandidates(
        IReadOnlyList<BrochureProjectFragment> fragments,
        int index)
    {
        var remaining = fragments.Count - index;

        if (remaining >= 4 && CanUseFour(fragments, index))
        {
            yield return new PageChoice(
                BrochurePageLayoutKind.FourCompact,
                4,
                DensePagePenalty(fragments, index, 4, targetWords: 62));
        }

        if (remaining >= 3 && CanUseThree(fragments, index))
        {
            yield return new PageChoice(
                BrochurePageLayoutKind.ThreeStandard,
                3,
                2 + DensePagePenalty(fragments, index, 3, targetWords: 92));
        }

        if (remaining >= 2 && CanUseTwo(fragments, index))
        {
            yield return new PageChoice(
                BrochurePageLayoutKind.TwoFeature,
                2,
                4 + DensePagePenalty(fragments, index, 2, targetWords: 145));
        }

        // A lone project is deliberately expensive unless it is the only remaining item.
        // This is what steers five concise projects towards 3+2 rather than 4+1.
        var singlePenalty = remaining == 1 ? 8 : 32;
        yield return new PageChoice(
            BrochurePageLayoutKind.SingleFeature,
            1,
            singlePenalty + SingleFeaturePenalty(fragments[index]));
    }

    private static bool PreferCandidate(PageChoice candidate, PageChoice? current)
    {
        if (current is null)
        {
            return true;
        }

        // On an exact score tie prefer the more balanced, less dense page.
        return candidate.ItemCount < current.ItemCount;
    }

    private static double DensePagePenalty(
        IReadOnlyList<BrochureProjectFragment> fragments,
        int index,
        int itemCount,
        int targetWords)
    {
        var items = fragments.Skip(index).Take(itemCount).ToArray();
        var average = items.Average(item => item.NarrativeWordCount);
        var longestTitle = items.Max(item => item.Project.ProjectName.Length);
        var wordPenalty = Math.Max(0, average - targetWords) / 18d;
        var titlePenalty = Math.Max(0, longestTitle - 72) / 28d;
        var galleryPenalty = items.Count(item => item.Project.ImageMode == BrochureImageMode.GalleryTwo) * 8d;
        return wordPenalty + titlePenalty + galleryPenalty;
    }

    private static double SingleFeaturePenalty(BrochureProjectFragment fragment)
        => fragment.Project.ImageMode == BrochureImageMode.GalleryTwo ? 0 : 1;

    private static bool CanUseFour(IReadOnlyList<BrochureProjectFragment> fragments, int index)
        => fragments.Skip(index).Take(4).All(fragment =>
            !fragment.IsContinuation
            && fragment.NarrativeWordCount <= FourProjectMaximumWords
            && fragment.Project.ProjectName.Length <= 105
            && fragment.Project.ImageMode != BrochureImageMode.GalleryTwo);

    private static bool CanUseThree(IReadOnlyList<BrochureProjectFragment> fragments, int index)
        => fragments.Skip(index).Take(3).All(fragment =>
            !fragment.IsContinuation
            && fragment.NarrativeWordCount <= ThreeProjectMaximumWords
            && fragment.Project.ProjectName.Length <= 130
            && fragment.Project.ImageMode != BrochureImageMode.GalleryTwo);

    private static bool CanUseTwo(IReadOnlyList<BrochureProjectFragment> fragments, int index)
        => fragments.Skip(index).Take(2).All(fragment =>
            !fragment.IsContinuation
            && fragment.NarrativeWordCount <= TwoFeatureMaximumWords);

    private static bool IsForcedFeature(BrochureProjectFragment fragment)
        => fragment.FragmentCount > 1
           || fragment.IsContinuation
           || fragment.NarrativeWordCount > LongNarrativeChunkWords;

    private static IReadOnlyList<BrochureProjectFragment> CreateFragments(BrochurePublicationProject project)
    {
        if (project.NarrativeWordCount <= LongNarrativeChunkWords)
        {
            return new[]
            {
                new BrochureProjectFragment(
                    project,
                    project.Narrative,
                    project.NarrativeWordCount,
                    false,
                    1,
                    1)
            };
        }

        var chunks = SplitNarrative(project.Narrative, LongNarrativeChunkWords);
        return chunks
            .Select((chunk, chunkIndex) => new BrochureProjectFragment(
                project,
                chunk,
                CountWords(chunk),
                chunkIndex > 0,
                chunkIndex + 1,
                chunks.Count))
            .ToArray();
    }

    private static IReadOnlyList<string> SplitNarrative(string value, int maximumWords)
    {
        var paragraphs = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var tokens = paragraphs
            .SelectMany((paragraph, paragraphIndex) => paragraph
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => new NarrativeToken(word, paragraphIndex)))
            .ToArray();
        if (tokens.Length <= maximumWords)
        {
            return new[] { value.Trim() };
        }

        // Balance continuation pages rather than creating a nearly empty final page.
        // Example: 211 words becomes 106 + 105, not 210 + 1. Paragraph boundaries
        // are retained wherever they fall within each balanced word slice.
        var chunkCount = (int)Math.Ceiling(tokens.Length / (double)maximumWords);
        var baseSize = tokens.Length / chunkCount;
        var remainder = tokens.Length % chunkCount;
        var chunks = new List<string>(chunkCount);
        var offset = 0;

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunkSize = baseSize + (chunkIndex < remainder ? 1 : 0);
            var slice = tokens.AsSpan(offset, chunkSize);
            var builder = new System.Text.StringBuilder();
            var previousParagraph = -1;
            foreach (var token in slice)
            {
                if (builder.Length > 0)
                {
                    if (token.ParagraphIndex == previousParagraph)
                    {
                        builder.Append(' ');
                    }
                    else
                    {
                        builder.Append("\n\n");
                    }
                }

                builder.Append(token.Word);
                previousParagraph = token.ParagraphIndex;
            }

            chunks.Add(builder.ToString());
            offset += chunkSize;
        }

        return chunks;
    }

    private sealed record NarrativeToken(string Word, int ParagraphIndex);

    private sealed record PageChoice(
        BrochurePageLayoutKind Layout,
        int ItemCount,
        double Penalty);

    [GeneratedRegex(@"\S+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();
}
