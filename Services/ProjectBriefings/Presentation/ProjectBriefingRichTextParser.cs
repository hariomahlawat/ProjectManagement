using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public static class ProjectBriefingRichTextParser
{
    private static readonly Regex HtmlBreakRegex = new(
        @"<(br\s*/?|/p|/div|/li)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlListItemRegex = new(
        @"<li[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MarkdownHeadingRegex = new(
        @"^\s{0,3}#{1,6}\s+(?<text>.+?)\s*#*\s*$",
        RegexOptions.Compiled);

    private static readonly Regex WholeBoldRegex = new(
        @"^\s*(\*\*|__)(?<text>.+?)\1\s*$",
        RegexOptions.Compiled);

    private static readonly Regex BulletRegex = new(
        @"^\s*(?<marker>[•●▪‣◦\-*])\s+(?<text>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedRegex = new(
        @"^\s*(?<marker>\d{1,3}[.)])\s+(?<text>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex LetteredRegex = new(
        @"^\s*(?<marker>\(?[A-Za-z]\))\s+(?<text>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex StandaloneListMarkerRegex = new(
        @"^\s*(?<marker>(?:\d{1,3}[.)]|\(?[A-Za-z]\)|[•●▪‣◦\-*]))\s*$",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownEmphasisRegex = new(
        @"(\*\*|__|\*|_)(?<text>[^*_]+)\1",
        RegexOptions.Compiled);

    private static readonly HashSet<string> KnownHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aim",
        "Purpose",
        "Overview",
        "Capability",
        "Capabilities",
        "Capability Overview",
        "Features",
        "Salient Features",
        "Key Features",
        "Functionalities",
        "Modules",
        "Key Modules",
        "Key Deliverables",
        "Deliverables",
        "Operational Impact",
        "Training Impact",
        "System Features",
        "Major Features",
        "Technical Features",
        "Benefits",
        "Advantages",
        "Employment",
        "Scope"
    };

    public static IReadOnlyList<ProjectBriefingCapabilityBlock> Parse(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "Brief description not recorded.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Capability overview not recorded.", StringComparison.OrdinalIgnoreCase))
        {
            return MissingCapability();
        }

        var blocks = new List<ProjectBriefingCapabilityBlock>();
        var implicitListContext = false;

        var lines = normalized.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                implicitListContext = false;
                continue;
            }

            // Source records sometimes place the list marker in one paragraph and its
            // text in the next. Merge that pair before heading detection so a title-cased
            // item cannot become a false slide heading.
            if (TryStandaloneListMarker(line, out var standaloneType, out var standaloneMarker)
                && index + 1 < lines.Length
                && !string.IsNullOrWhiteSpace(lines[index + 1])
                && !StandaloneListMarkerRegex.IsMatch(lines[index + 1].Trim()))
            {
                blocks.Add(new ProjectBriefingCapabilityBlock(
                    standaloneType,
                    CleanInline(lines[++index]),
                    standaloneMarker));
                implicitListContext = true;
                continue;
            }

            // List recognition deliberately precedes heading recognition. Numeric and
            // alphabetic list markers are common in source project records and must never
            // be promoted to slide headings merely because the following text is title case.
            if (TryListItem(line, out var type, out var marker, out var listText))
            {
                blocks.Add(new ProjectBriefingCapabilityBlock(
                    type,
                    CleanInline(listText),
                    marker));
                implicitListContext = true;
                continue;
            }

            if (TryHeading(line, out var heading))
            {
                blocks.Add(new ProjectBriefingCapabilityBlock(
                    ProjectBriefingCapabilityBlockType.Heading,
                    heading));
                implicitListContext = IsListHeading(heading);
                continue;
            }

            var cleaned = CleanInline(line);
            if (implicitListContext && cleaned.Length <= 180)
            {
                blocks.Add(new ProjectBriefingCapabilityBlock(
                    ProjectBriefingCapabilityBlockType.Bullet,
                    cleaned,
                    "•"));
                continue;
            }

            blocks.Add(new ProjectBriefingCapabilityBlock(
                ProjectBriefingCapabilityBlockType.Paragraph,
                cleaned));
            implicitListContext = IntroducesList(cleaned);
        }

        if (blocks.Count > 1
            && blocks[0].Type == ProjectBriefingCapabilityBlockType.Heading
            && string.Equals(blocks[0].Text, "Capability Overview", StringComparison.OrdinalIgnoreCase))
        {
            blocks.RemoveAt(0);
        }

        return blocks.Count == 0 ? MissingCapability() : blocks;
    }

    private static IReadOnlyList<ProjectBriefingCapabilityBlock> MissingCapability()
        => new[]
        {
            new ProjectBriefingCapabilityBlock(
                ProjectBriefingCapabilityBlockType.Paragraph,
                "Capability overview not recorded.",
                IsMuted: true)
        };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\uF0B7', '•')
            .Replace('', '•')
            .Replace('◦', '•');

        text = HtmlListItemRegex.Replace(text, "\n• ");
        text = HtmlBreakRegex.Replace(text, "\n");
        text = HtmlTagRegex.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var builder = new StringBuilder(text.Length);
        var blankLinePending = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                blankLinePending = builder.Length > 0;
                continue;
            }

            if (blankLinePending)
            {
                builder.Append('\n');
                blankLinePending = false;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }
            builder.Append(line);
        }

        return builder.ToString().Trim();
    }

    private static bool TryHeading(string line, out string heading)
    {
        if (BulletRegex.IsMatch(line)
            || NumberedRegex.IsMatch(line)
            || LetteredRegex.IsMatch(line))
        {
            heading = string.Empty;
            return false;
        }

        var markdown = MarkdownHeadingRegex.Match(line);
        if (markdown.Success)
        {
            heading = CleanInline(markdown.Groups["text"].Value);
            return heading.Length > 0;
        }

        var bold = WholeBoldRegex.Match(line);
        if (bold.Success)
        {
            heading = CleanInline(bold.Groups["text"].Value);
            return heading.Length > 0;
        }

        var candidate = CleanInline(line.Trim().TrimEnd(':'));
        if (KnownHeadings.Contains(candidate)
            || IsUpperHeading(candidate)
            || IsCompactTitleHeading(line, candidate))
        {
            heading = candidate;
            return heading.Length > 0;
        }

        heading = string.Empty;
        return false;
    }

    private static bool IsUpperHeading(string value)
    {
        if (value.Length is < 3 or > 80)
        {
            return false;
        }

        var letters = value.Where(char.IsLetter).ToArray();
        return letters.Length >= 3 && letters.All(char.IsUpper);
    }

    private static bool IsCompactTitleHeading(string original, string candidate)
    {
        if (candidate.Length is < 3 or > 52)
        {
            return false;
        }

        if (original.EndsWith(".", StringComparison.Ordinal)
            || original.EndsWith(";", StringComparison.Ordinal)
            || original.EndsWith(",", StringComparison.Ordinal)
            || original.EndsWith("?", StringComparison.Ordinal)
            || original.EndsWith("!", StringComparison.Ordinal))
        {
            return false;
        }

        if (original.EndsWith(":", StringComparison.Ordinal))
        {
            return true;
        }

        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 1 or > 7)
        {
            return false;
        }

        var significant = words
            .Where(word => word.Length > 2
                && !string.Equals(word, "and", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(word, "for", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(word, "the", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(word, "with", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return significant.Length > 0
            && significant.All(word => char.IsUpper(word[0]));
    }

    private static bool IsListHeading(string heading)
        => heading.Contains("feature", StringComparison.OrdinalIgnoreCase)
            || heading.Contains("deliverable", StringComparison.OrdinalIgnoreCase)
            || heading.Contains("module", StringComparison.OrdinalIgnoreCase)
            || heading.Contains("function", StringComparison.OrdinalIgnoreCase)
            || heading.Contains("component", StringComparison.OrdinalIgnoreCase);

    private static bool IntroducesList(string value)
    {
        var normalized = value.Trim();
        return normalized.EndsWith(":", StringComparison.Ordinal)
            || normalized.EndsWith(":-", StringComparison.Ordinal)
            || normalized.EndsWith("i.e.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("as under", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("listed below", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("salient features", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryStandaloneListMarker(
        string line,
        out ProjectBriefingCapabilityBlockType type,
        out string marker)
    {
        var match = StandaloneListMarkerRegex.Match(line);
        if (!match.Success)
        {
            type = ProjectBriefingCapabilityBlockType.Paragraph;
            marker = string.Empty;
            return false;
        }

        marker = match.Groups["marker"].Value;
        if (char.IsDigit(marker[0]))
        {
            type = ProjectBriefingCapabilityBlockType.NumberedItem;
        }
        else if (marker.Any(char.IsLetter))
        {
            type = ProjectBriefingCapabilityBlockType.LetteredItem;
        }
        else
        {
            type = ProjectBriefingCapabilityBlockType.Bullet;
            marker = "•";
        }
        return true;
    }

    private static bool TryListItem(
        string line,
        out ProjectBriefingCapabilityBlockType type,
        out string marker,
        out string text)
    {
        var bullet = BulletRegex.Match(line);
        if (bullet.Success)
        {
            type = ProjectBriefingCapabilityBlockType.Bullet;
            marker = "•";
            text = bullet.Groups["text"].Value;
            return true;
        }

        var numbered = NumberedRegex.Match(line);
        if (numbered.Success)
        {
            type = ProjectBriefingCapabilityBlockType.NumberedItem;
            marker = numbered.Groups["marker"].Value;
            text = numbered.Groups["text"].Value;
            return true;
        }

        var lettered = LetteredRegex.Match(line);
        if (lettered.Success)
        {
            type = ProjectBriefingCapabilityBlockType.LetteredItem;
            marker = lettered.Groups["marker"].Value;
            text = lettered.Groups["text"].Value;
            return true;
        }

        type = ProjectBriefingCapabilityBlockType.Paragraph;
        marker = string.Empty;
        text = string.Empty;
        return false;
    }

    private static string CleanInline(string value)
    {
        var result = MarkdownEmphasisRegex.Replace(value, match => match.Groups["text"].Value);
        result = result
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal);

        while (result.Contains("  ", StringComparison.Ordinal))
        {
            result = result.Replace("  ", " ", StringComparison.Ordinal);
        }

        return result.Trim();
    }
}
