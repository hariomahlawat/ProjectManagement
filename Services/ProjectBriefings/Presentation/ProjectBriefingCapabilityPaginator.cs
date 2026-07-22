using System.Text.RegularExpressions;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public static class ProjectBriefingCapabilityPaginator
{
    private static readonly LayoutProfile PrimaryProfile = new(
        AvailableHeight: 4.42,
        CharactersPerLine: 72);

    private static readonly LayoutProfile ContinuationProfile = new(
        AvailableHeight: 4.58,
        CharactersPerLine: 118);

    public static ProjectBriefingCapabilityPagination Paginate(string? value)
        => PaginateBlocks(ProjectBriefingRichTextParser.Parse(value));

    private static ProjectBriefingCapabilityPagination PaginateBlocks(
        IReadOnlyList<ProjectBriefingCapabilityBlock> sourceBlocks)
    {
        ArgumentNullException.ThrowIfNull(sourceBlocks);

        var blocks = sourceBlocks.Count == 0
            ? ProjectBriefingRichTextParser.Parse(null)
            : sourceBlocks;

        var pending = new LinkedList<ProjectBriefingCapabilityBlock>(blocks);
        var pages = new List<ProjectBriefingCapabilityPage>();
        var pageNumber = 1;

        while (pending.First is not null)
        {
            var profile = pageNumber == 1 ? PrimaryProfile : ContinuationProfile;
            var rendered = new List<ProjectBriefingCapabilityLayoutBlock>();
            var remaining = profile.AvailableHeight;

            while (pending.First is not null)
            {
                var block = pending.First.Value;

                if (block.Type == ProjectBriefingCapabilityBlockType.Heading
                    && rendered.Count > 0
                    && pending.First.Next is not null)
                {
                    var headingHeight = CreateLayout(block, profile).TotalHeight;
                    var nextMinimum = MinimumFollowingHeight(pending.First.Next.Value, profile);
                    if (headingHeight + nextMinimum > remaining)
                    {
                        break;
                    }
                }

                var layout = CreateLayout(block, profile);
                if (layout.TotalHeight <= remaining + .001)
                {
                    pending.RemoveFirst();
                    rendered.Add(layout);
                    remaining -= layout.TotalHeight;
                    continue;
                }

                if (rendered.Count > 0)
                {
                    var headingNeedsContent = rendered.Count == 1
                        && rendered[0].Type == ProjectBriefingCapabilityBlockType.Heading;

                    if (headingNeedsContent
                        && TrySplit(block, profile, remaining, out var first, out var remainder))
                    {
                        pending.RemoveFirst();
                        rendered.Add(CreateLayout(first!, profile));
                        if (remainder is not null)
                        {
                            pending.AddFirst(remainder);
                        }
                    }

                    break;
                }

                if (TrySplit(block, profile, remaining, out var splitFirst, out var splitRemainder))
                {
                    pending.RemoveFirst();
                    rendered.Add(CreateLayout(splitFirst!, profile));
                    if (splitRemainder is not null)
                    {
                        pending.AddFirst(splitRemainder);
                    }
                    break;
                }

                pending.RemoveFirst();
                rendered.Add(layout with
                {
                    TextHeight = Math.Max(.20, remaining - layout.SpaceAfter),
                    SpaceAfter = 0
                });
                break;
            }

            if (rendered.Count == 0)
            {
                var forced = pending.First!.Value;
                pending.RemoveFirst();
                rendered.Add(CreateLayout(forced, profile));
            }

            pages.Add(new ProjectBriefingCapabilityPage(
                pageNumber,
                pageNumber == 1,
                rendered));
            pageNumber++;
        }

        return new ProjectBriefingCapabilityPagination(pages);
    }

    private static ProjectBriefingCapabilityLayoutBlock CreateLayout(
        ProjectBriefingCapabilityBlock block,
        LayoutProfile profile)
    {
        var (fontSize, lineHeight, spaceAfter) = block.Type switch
        {
            ProjectBriefingCapabilityBlockType.Heading => (13.6, .235, .105),
            ProjectBriefingCapabilityBlockType.Bullet => (12.7, .215, .060),
            ProjectBriefingCapabilityBlockType.NumberedItem => (12.7, .215, .060),
            ProjectBriefingCapabilityBlockType.LetteredItem => (12.7, .215, .060),
            _ => (block.IsMuted ? 12.5 : 13.0, .222, .105)
        };

        var characterBudget = profile.CharactersPerLine;
        if (block.Type is ProjectBriefingCapabilityBlockType.Bullet
            or ProjectBriefingCapabilityBlockType.NumberedItem
            or ProjectBriefingCapabilityBlockType.LetteredItem)
        {
            characterBudget = Math.Max(20, characterBudget - 7);
        }

        var lines = EstimateLines(block.Text, characterBudget);
        var textHeight = Math.Max(lineHeight + .015, (lines * lineHeight) + .025);

        return new ProjectBriefingCapabilityLayoutBlock(
            block.Type,
            block.Text,
            block.Marker,
            block.IsContinuation,
            block.IsMuted,
            fontSize,
            textHeight,
            spaceAfter);
    }

    private static double MinimumFollowingHeight(
        ProjectBriefingCapabilityBlock block,
        LayoutProfile profile)
    {
        var layout = CreateLayout(block, profile);
        return Math.Min(layout.TotalHeight, .56);
    }

    private static int EstimateLines(string text, int charactersPerLine)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 1;
        }

        return text
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Sum(line => Math.Max(
                1,
                (int)Math.Ceiling(WeightedLength(line) / (double)Math.Max(1, charactersPerLine))));
    }

    private static int WeightedLength(string text)
    {
        var total = 0d;
        foreach (var character in text)
        {
            total += character switch
            {
                'W' or 'M' or 'w' or 'm' => 1.22,
                'I' or 'i' or 'l' or '1' => .62,
                _ => 1d
            };
        }

        return Math.Max(1, (int)Math.Ceiling(total));
    }

    private static bool TrySplit(
        ProjectBriefingCapabilityBlock block,
        LayoutProfile profile,
        double availableHeight,
        out ProjectBriefingCapabilityBlock? first,
        out ProjectBriefingCapabilityBlock? remainder)
    {
        first = null;
        remainder = null;

        if (block.Type == ProjectBriefingCapabilityBlockType.Heading
            || string.IsNullOrWhiteSpace(block.Text))
        {
            return false;
        }

        var sample = CreateLayout(block, profile);
        var lineHeight = block.Type switch
        {
            ProjectBriefingCapabilityBlockType.Bullet => .215,
            ProjectBriefingCapabilityBlockType.NumberedItem => .215,
            ProjectBriefingCapabilityBlockType.LetteredItem => .215,
            _ => .222
        };

        var usableHeight = availableHeight - sample.SpaceAfter - .025;
        var maximumLines = (int)Math.Floor(usableHeight / lineHeight);
        if (maximumLines < 1)
        {
            return false;
        }

        var characterBudget = profile.CharactersPerLine;
        if (block.Type is ProjectBriefingCapabilityBlockType.Bullet
            or ProjectBriefingCapabilityBlockType.NumberedItem
            or ProjectBriefingCapabilityBlockType.LetteredItem)
        {
            characterBudget = Math.Max(20, characterBudget - 7);
        }

        if (EstimateLines(block.Text, characterBudget) <= maximumLines)
        {
            return false;
        }

        var maximumCharacters = FindMaximumPrefixLength(
            block.Text,
            characterBudget,
            maximumLines);
        var splitIndex = FindSplitIndex(block.Text, maximumCharacters);
        if (splitIndex <= 0 || splitIndex >= block.Text.Length)
        {
            return false;
        }

        var firstText = block.Text[..splitIndex].Trim();
        var remainderText = block.Text[splitIndex..].Trim();
        if (firstText.Length == 0 || remainderText.Length == 0)
        {
            return false;
        }

        first = block with { Text = firstText };
        remainder = block with
        {
            Text = remainderText,
            IsContinuation = true
        };
        return true;
    }

    private static int FindMaximumPrefixLength(
        string text,
        int charactersPerLine,
        int maximumLines)
    {
        var low = 1;
        var high = Math.Max(1, text.Length - 1);
        var best = 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = text[..middle];
            if (EstimateLines(candidate, charactersPerLine) <= maximumLines)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Clamp(best, 1, Math.Max(1, text.Length - 1));
    }

    private static int FindSplitIndex(string text, int maximumCharacters)
    {
        var limit = Math.Min(text.Length - 1, Math.Max(1, maximumCharacters));
        var minimum = Math.Max(16, (int)Math.Floor(limit * .55));

        for (var index = limit; index >= minimum; index--)
        {
            if (index + 1 >= text.Length || !char.IsWhiteSpace(text[index + 1]))
            {
                continue;
            }

            if (text[index] is '.' or '?' or '!' or ';' or ':')
            {
                return index + 1;
            }
        }

        for (var index = limit; index >= minimum; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        var match = Regex.Match(text[..limit], @"\s+\S+$");
        return match.Success ? match.Index : limit;
    }

    private sealed record LayoutProfile(
        double AvailableHeight,
        int CharactersPerLine);
}
