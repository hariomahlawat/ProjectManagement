using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Publications;

/// <summary>
/// Converts project narratives into deterministic A4 brochure page plans.
/// The planner deliberately changes the number of project cards per page instead
/// of shrinking body copy below the publication typography floor.
/// </summary>
public static partial class BrochureLayoutPlanner
{
    private const int LongNarrativeChunkWords = 210;

    public static IReadOnlyList<BrochurePagePlan> Plan(IReadOnlyList<BrochurePublicationProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var fragments = projects
            .SelectMany(CreateFragments)
            .ToList();

        var pages = new List<BrochurePagePlan>();
        var index = 0;
        while (index < fragments.Count)
        {
            var remaining = fragments.Count - index;
            var current = fragments[index];

            if (current.FragmentCount > 1 || current.IsContinuation || current.NarrativeWordCount > 210)
            {
                pages.Add(new BrochurePagePlan(
                    BrochurePageLayoutKind.SingleFeature,
                    new[] { current }));
                index++;
                continue;
            }

            if (remaining >= 4 && CanUseFour(fragments, index))
            {
                pages.Add(new BrochurePagePlan(
                    BrochurePageLayoutKind.FourCompact,
                    fragments.Skip(index).Take(4).ToArray()));
                index += 4;
                continue;
            }

            if (remaining >= 3 && CanUseThree(fragments, index))
            {
                pages.Add(new BrochurePagePlan(
                    BrochurePageLayoutKind.ThreeStandard,
                    fragments.Skip(index).Take(3).ToArray()));
                index += 3;
                continue;
            }

            if (remaining >= 2 && CanUseTwo(fragments, index))
            {
                pages.Add(new BrochurePagePlan(
                    BrochurePageLayoutKind.TwoFeature,
                    fragments.Skip(index).Take(2).ToArray()));
                index += 2;
                continue;
            }

            pages.Add(new BrochurePagePlan(
                BrochurePageLayoutKind.SingleFeature,
                new[] { current }));
            index++;
        }

        return pages;
    }

    public static int CountWords(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? 0
            : WordRegex().Matches(value).Count;

    private static bool CanUseFour(IReadOnlyList<BrochureProjectFragment> fragments, int index)
        => fragments.Skip(index).Take(4).All(fragment =>
            !fragment.IsContinuation && fragment.NarrativeWordCount <= 85);

    private static bool CanUseThree(IReadOnlyList<BrochureProjectFragment> fragments, int index)
        => fragments.Skip(index).Take(3).All(fragment =>
            !fragment.IsContinuation && fragment.NarrativeWordCount <= 125);

    private static bool CanUseTwo(IReadOnlyList<BrochureProjectFragment> fragments, int index)
        => fragments.Skip(index).Take(2).All(fragment =>
            !fragment.IsContinuation && fragment.NarrativeWordCount <= 210);

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

        var chunks = new List<string>();
        var current = new List<string>();
        var currentWords = 0;

        foreach (var paragraph in paragraphs)
        {
            var paragraphWords = CountWords(paragraph);
            if (paragraphWords > maximumWords)
            {
                FlushCurrent();
                foreach (var part in SplitLongParagraph(paragraph, maximumWords))
                {
                    chunks.Add(part);
                }
                continue;
            }

            if (currentWords > 0 && currentWords + paragraphWords > maximumWords)
            {
                FlushCurrent();
            }

            current.Add(paragraph);
            currentWords += paragraphWords;
        }

        FlushCurrent();
        return chunks.Count == 0 ? new[] { value } : chunks;

        void FlushCurrent()
        {
            if (current.Count == 0)
            {
                return;
            }

            chunks.Add(string.Join("\n\n", current));
            current.Clear();
            currentWords = 0;
        }
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph, int maximumWords)
    {
        var words = paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index += maximumWords)
        {
            yield return string.Join(" ", words.Skip(index).Take(maximumWords));
        }
    }

    [GeneratedRegex(@"\S+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();
}
