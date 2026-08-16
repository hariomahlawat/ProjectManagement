using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using ProjectManagement.Services.Compendiums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// QuestPDF renderer for the Compendium's deliberately constrained narrative vocabulary.
/// It accepts paragraphs, level-three minor headings, unordered bullets, bold and italic only.
/// Unsupported Markdown constructs are rendered as harmless text rather than as arbitrary HTML,
/// links, code blocks or publication-specific styling.
/// </summary>
internal static class CompendiumNarrativePdfRenderer
{
    private const string MinorHeadingColor = "#17382F";
    private static readonly MarkdownPipeline InlinePipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .Build();

    public static void Render(
        IContainer container,
        string markdown,
        bool justifyParagraphs,
        MarkdownPdfTypography typography,
        bool allowMinorHeadings = true)
    {
        var document = CompendiumNarrativeParser.Parse(markdown, allowMinorHeadings);
        if (document.IsEmpty) return;

        container.Column(column =>
        {
            var index = 0;
            foreach (var block in document.Blocks)
            {
                var topSpacing = index++ == 0 ? 0f : typography.BlockSpacing;
                switch (block.Kind)
                {
                    case CompendiumNarrativeBlockKind.MinorHeading:
                        column.Item()
                            .PaddingTop(topSpacing + CompendiumNarrativeSemanticPolicy.MinorHeadingTopSpacingPoints)
                            .PaddingBottom(CompendiumNarrativeSemanticPolicy.MinorHeadingBottomSpacingPoints)
                            .Text(text =>
                            {
                                text.DefaultTextStyle(BaseTextStyle
                                    .FontSize(typography.BodyFontSize * CompendiumNarrativeSemanticPolicy.MinorHeadingFontScale)
                                    .SemiBold()
                                    .FontColor(MinorHeadingColor)
                                    .LineHeight(CompendiumNarrativeSemanticPolicy.MinorHeadingLineHeightMultiplier));
                                RenderControlledInlines(text, block.Markdown);
                            });
                        break;

                    case CompendiumNarrativeBlockKind.BulletList:
                        column.Item().PaddingTop(topSpacing).Column(listColumn =>
                        {
                            listColumn.Spacing(CompendiumNarrativeSemanticPolicy.BulletItemSpacingPoints);
                            foreach (var item in block.Items.Where(item => !string.IsNullOrWhiteSpace(item)))
                            {
                                listColumn.Item().Row(row =>
                                {
                                    row.ConstantItem(CompendiumNarrativeSemanticPolicy.BulletGutterPoints)
                                        .Text("•")
                                        .FontSize(typography.BodyFontSize)
                                        .FontColor(typography.BodyFontColor);
                                    row.RelativeItem().Text(text =>
                                    {
                                        text.DefaultTextStyle(BaseTextStyle
                                            .FontSize(typography.BodyFontSize)
                                            .FontColor(typography.BodyFontColor)
                                            .LineHeight(typography.BodyLineHeight));
                                        RenderControlledInlines(text, item);
                                    });
                                });
                            }
                        });
                        break;

                    default:
                        column.Item().PaddingTop(topSpacing).Text(text =>
                        {
                            text.DefaultTextStyle(BaseTextStyle
                                .FontSize(typography.BodyFontSize)
                                .FontColor(typography.BodyFontColor)
                                .LineHeight(typography.BodyLineHeight));
                            if (justifyParagraphs) text.Justify();
                            RenderControlledInlines(text, block.Markdown);
                        });
                        break;
                }
            }
        });
    }

    private static void RenderControlledInlines(TextDescriptor text, string markdown)
    {
        var parsed = Markdig.Markdown.Parse(markdown ?? string.Empty, InlinePipeline);
        var paragraph = parsed.OfType<ParagraphBlock>().FirstOrDefault();
        if (paragraph?.Inline is null)
        {
            text.Span(markdown ?? string.Empty);
            return;
        }

        foreach (var inline in paragraph.Inline)
            RenderInline(text, inline, isBold: false, isItalic: false);
    }

    private static void RenderInline(TextDescriptor text, Inline inline, bool isBold, bool isItalic)
    {
        switch (inline)
        {
            case LiteralInline literal:
                ApplyStyle(text.Span(literal.Content.ToString()), isBold, isItalic);
                break;

            case LineBreakInline:
                text.Span(" ");
                break;

            case EmphasisInline emphasis:
                var nextBold = isBold || emphasis.DelimiterCount >= 2;
                var nextItalic = isItalic || emphasis.DelimiterCount == 1;
                foreach (var child in emphasis)
                    RenderInline(text, child, nextBold, nextItalic);
                break;

            case CodeInline code:
                ApplyStyle(text.Span(code.Content), isBold, isItalic);
                break;

            // Unsupported inline constructs are deliberately flattened to their visible label/text.
            // No URL target, arbitrary HTML or external behaviour is introduced.
            case ContainerInline container:
                foreach (var child in container)
                    RenderInline(text, child, isBold, isItalic);
                break;

            default:
                ApplyStyle(text.Span(inline.ToString() ?? string.Empty), isBold, isItalic);
                break;
        }
    }

    private static TextSpanDescriptor ApplyStyle(TextSpanDescriptor span, bool isBold, bool isItalic)
    {
        if (isBold) span = span.SemiBold();
        if (isItalic) span = span.Italic();
        return span;
    }

    private static TextStyle BaseTextStyle => TextStyle.Default
        .DisableFontFeature(FontFeatures.StandardLigatures);
}
