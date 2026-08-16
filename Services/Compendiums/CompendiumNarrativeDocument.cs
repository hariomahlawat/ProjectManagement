using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Controlled semantic model for Compendium narrative content. The publication intentionally
/// supports a very small authoring vocabulary: paragraphs, level-three minor headings, unordered
/// bullets, bold and italic inline emphasis. Unsupported block syntax is retained as ordinary text
/// rather than becoming executable/HTML content.
/// </summary>
public enum CompendiumNarrativeBlockKind
{
    Paragraph = 0,
    MinorHeading = 1,
    BulletList = 2
}

public sealed record CompendiumNarrativeBlock(
    CompendiumNarrativeBlockKind Kind,
    string Markdown,
    IReadOnlyList<string> Items)
{
    public static CompendiumNarrativeBlock Paragraph(string markdown)
        => new(CompendiumNarrativeBlockKind.Paragraph, markdown.Trim(), Array.Empty<string>());

    public static CompendiumNarrativeBlock MinorHeading(string markdown)
        => new(CompendiumNarrativeBlockKind.MinorHeading, markdown.Trim(), Array.Empty<string>());

    public static CompendiumNarrativeBlock BulletList(IReadOnlyList<string> items)
        => new(CompendiumNarrativeBlockKind.BulletList, string.Empty, items);

    public string ToMarkdown()
        => Kind switch
        {
            CompendiumNarrativeBlockKind.MinorHeading => $"### {Markdown.Trim()}",
            CompendiumNarrativeBlockKind.BulletList => string.Join("\n", Items
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => $"- {item.Trim()}")),
            _ => Markdown.Trim()
        };
}

public sealed record CompendiumNarrativeDocument(IReadOnlyList<CompendiumNarrativeBlock> Blocks)
{
    public static CompendiumNarrativeDocument Empty { get; } = new(Array.Empty<CompendiumNarrativeBlock>());

    public bool IsEmpty => Blocks.Count == 0;

    public string ToMarkdown()
        => string.Join("\n\n", Blocks
            .Select(block => block.ToMarkdown())
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
}

public static class CompendiumNarrativeSemanticPolicy
{
    public const float MinorHeadingFontScale = 1.08f;
    public const float MinorHeadingLineHeightMultiplier = 1.15f;
    public const float MinorHeadingTopSpacingPoints = 4f;
    public const float MinorHeadingBottomSpacingPoints = 1.5f;
    public const float BulletGutterPoints = 18f;
    public const float BulletItemSpacingPoints = 2.5f;
}

/// <summary>
/// Deterministic parser shared by page measurement, QuestPDF composition and the browser proof
/// contract. It deliberately avoids arbitrary HTML and broad Markdown extensions.
/// </summary>
public static class CompendiumNarrativeParser
{
    private static readonly Regex MarkdownHeading = new(@"^\s*(#{1,6})\s+(.+?)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Bullet = new(@"^\s*[-*]\s+(.+?)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLink = new(@"!?\[([^\]]*)\]\([^\)]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StrongAsterisk = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StrongUnderscore = new(@"__(.+?)__", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ItalicAsterisk = new(@"(?<!\*)\*([^*\n]+)\*(?!\*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ItalicUnderscore = new(@"(?<!_)_([^_\n]+)_(?!_)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InlineCode = new(@"`([^`]+)`", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static CompendiumNarrativeDocument Parse(string? markdown, bool allowMinorHeadings = true)
    {
        var normalized = Normalize(markdown);
        if (normalized.Length == 0) return CompendiumNarrativeDocument.Empty;

        var blocks = new List<CompendiumNarrativeBlock>();
        var paragraphLines = new List<string>();
        var bulletItems = new List<string>();

        void FlushParagraph()
        {
            if (paragraphLines.Count == 0) return;
            var paragraph = string.Join(" ", paragraphLines)
                .Replace("\t", " ", StringComparison.Ordinal)
                .Trim();
            if (paragraph.Length > 0) blocks.Add(CompendiumNarrativeBlock.Paragraph(paragraph));
            paragraphLines.Clear();
        }

        void FlushBullets()
        {
            if (bulletItems.Count == 0) return;
            blocks.Add(CompendiumNarrativeBlock.BulletList(bulletItems.ToArray()));
            bulletItems.Clear();
        }

        foreach (var raw in normalized.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                FlushBullets();
                continue;
            }

            var heading = MarkdownHeading.Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                FlushBullets();
                var marker = heading.Groups[1].Value;
                var content = heading.Groups[2].Value.Trim();
                if (content.Length > 0)
                {
                    blocks.Add(allowMinorHeadings && marker.Length == 3
                        ? CompendiumNarrativeBlock.MinorHeading(content)
                        : CompendiumNarrativeBlock.Paragraph(content));
                }
                continue;
            }

            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                var content = bullet.Groups[1].Value.Trim();
                if (content.Length > 0) bulletItems.Add(content);
                continue;
            }

            FlushBullets();
            paragraphLines.Add(line);
        }

        FlushParagraph();
        FlushBullets();
        return blocks.Count == 0 ? CompendiumNarrativeDocument.Empty : new CompendiumNarrativeDocument(blocks);
    }

    public static string ToPlainText(string? markdown, bool allowMinorHeadings = true)
    {
        var document = Parse(markdown, allowMinorHeadings);
        if (document.IsEmpty) return string.Empty;
        var values = new List<string>();
        foreach (var block in document.Blocks)
        {
            if (block.Kind == CompendiumNarrativeBlockKind.BulletList)
            {
                values.AddRange(block.Items.Select(CleanInline).Where(value => value.Length > 0));
                continue;
            }

            var clean = CleanInline(block.Markdown);
            if (clean.Length > 0) values.Add(clean);
        }
        return string.Join("\n\n", values);
    }

    public static string CleanInline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = MarkdownLink.Replace(value, "$1");
        cleaned = StrongAsterisk.Replace(cleaned, "$1");
        cleaned = StrongUnderscore.Replace(cleaned, "$1");
        cleaned = ItalicAsterisk.Replace(cleaned, "$1");
        cleaned = ItalicUnderscore.Replace(cleaned, "$1");
        cleaned = InlineCode.Replace(cleaned, "$1");
        return Whitespace.Replace(cleaned, " ").Trim();
    }

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Trim();
}
