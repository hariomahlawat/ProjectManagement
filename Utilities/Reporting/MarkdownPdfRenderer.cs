using System;
using System.Collections.Generic;
using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ProjectManagement.Utilities.Reporting;

// SECTION: Render Markdown content into QuestPDF components without HTML conversion.
internal static class MarkdownPdfRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .Build();

    public static void Render(IContainer container, string markdown)
    {
        var source = string.IsNullOrWhiteSpace(markdown) ? "Not recorded" : markdown;
        var doc = Markdig.Markdown.Parse(source, Pipeline);

        container.Column(col =>
        {
            col.Spacing(6);

            foreach (var block in doc)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        col.Item().Text(text =>
                        {
                            text.DefaultTextStyle(TextStyle.Default
                                .FontSize(HeadingSize(heading.Level))
                                .SemiBold()
                                .FontColor("#0F172A"));
                            RenderInlines(text, heading.Inline);
                        });
                        break;

                    case ParagraphBlock paragraph:
                        col.Item().Text(text =>
                        {
                            text.DefaultTextStyle(TextStyle.Default
                                .FontSize(10)
                                .FontColor("#0F172A")
                                .LineHeight(1.25f));
                            RenderInlines(text, paragraph.Inline);
                        });
                        break;

                    case ListBlock list:
                        RenderList(col, list, 0);
                        break;

                    case FencedCodeBlock fenced:
                        RenderCodeBlock(col, fenced);
                        break;

                    case CodeBlock code:
                        RenderCodeBlock(col, code);
                        break;

                    case ThematicBreakBlock:
                        col.Item().PaddingVertical(6).Element(e => e.Height(1).Background("#E2E8F0"));
                        break;

                    default:
                        col.Item().Text(block.ToString() ?? string.Empty)
                            .FontSize(10)
                            .FontColor("#0F172A");
                        break;
                }
            }
        });
    }

    // SECTION: Block rendering helpers
    private static float HeadingSize(int level) => level switch
    {
        1 => 14,
        2 => 13,
        3 => 12,
        _ => 11
    };

    private static void RenderList(ColumnDescriptor col, ListBlock list, int depth)
    {
        var index = int.TryParse(list.OrderedStart?.ToString(), out var parsedIndex) ? parsedIndex : 1;

        foreach (var item in list)
        {
            if (item is not ListItemBlock li)
            {
                continue;
            }

            var prefix = list.IsOrdered ? $"{index}. " : "• ";
            index++;

            col.Item().PaddingLeft(depth * 14).Row(row =>
            {
                row.ConstantItem(18).Text(prefix).FontSize(10).FontColor("#0F172A");
                row.RelativeItem().Column(itemCol =>
                {
                    itemCol.Spacing(4);

                    foreach (var block in li)
                    {
                        switch (block)
                        {
                            case ParagraphBlock paragraph:
                                itemCol.Item().Text(text =>
                                {
                                    text.DefaultTextStyle(TextStyle.Default
                                        .FontSize(10)
                                        .FontColor("#0F172A")
                                        .LineHeight(1.25f));
                                    RenderInlines(text, paragraph.Inline);
                                });
                                break;

                            case ListBlock nested:
                                RenderList(itemCol, nested, depth + 1);
                                break;

                            case FencedCodeBlock fenced:
                                RenderCodeBlock(itemCol, fenced);
                                break;

                            case CodeBlock code:
                                RenderCodeBlock(itemCol, code);
                                break;

                            default:
                                itemCol.Item().Text(block.ToString() ?? string.Empty)
                                    .FontSize(10)
                                    .FontColor("#0F172A");
                                break;
                        }
                    }
                });
            });
        }
    }

    private static void RenderCodeBlock(ColumnDescriptor col, CodeBlock code)
    {
        var content = string.Join("\n", code.Lines.Lines.Select(line => line.ToString()));
        RenderCodeBlockText(col, content);
    }

    private static void RenderCodeBlock(ColumnDescriptor col, FencedCodeBlock code)
    {
        var content = string.Join("\n", code.Lines.Lines.Select(line => line.ToString()));
        RenderCodeBlockText(col, content);
    }

    private static void RenderCodeBlockText(ColumnDescriptor col, string content)
    {
        var safe = string.IsNullOrWhiteSpace(content) ? string.Empty : content.TrimEnd();

        col.Item().Element(box =>
        {
            box.Border(1)
                .BorderColor("#E2E8F0")
                .Background("#F8FAFC")
                .Padding(10)
                .Text(safe)
                .FontSize(9)
                .FontColor("#0F172A");
        });
    }

    // SECTION: Inline rendering helpers
    private static void RenderInlines(TextDescriptor text, ContainerInline? inline)
    {
        if (inline is null)
        {
            return;
        }

        foreach (var child in inline)
        {
            RenderInline(text, child, isBold: false, isItalic: false);
        }
    }

    private static void RenderInline(TextDescriptor text, Inline child, bool isBold, bool isItalic)
    {
        switch (child)
        {
            case LiteralInline literal:
                ApplySpanStyle(text.Span(literal.Content.ToString()), isBold, isItalic);
                break;

            case LineBreakInline:
                text.Span("\n");
                break;

            case EmphasisInline emphasis:
                var nextBold = isBold || emphasis.DelimiterCount >= 2;
                var nextItalic = isItalic || emphasis.DelimiterCount == 1;
                foreach (var inner in emphasis)
                {
                    RenderInline(text, inner, nextBold, nextItalic);
                }
                break;

            case CodeInline code:
                ApplySpanStyle(text.Span(code.Content).FontSize(9), isBold, isItalic);
                break;

            case LinkInline link:
                RenderLink(text, link, isBold, isItalic);
                break;

            default:
                ApplySpanStyle(text.Span(child.ToString() ?? string.Empty), isBold, isItalic);
                break;
        }
    }

    private static void RenderLink(TextDescriptor text, LinkInline link, bool isBold, bool isItalic)
    {
        var label = InlineText(link);
        var url = link.GetDynamicUrl is not null ? link.GetDynamicUrl() : link.Url;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim();
        }

        ApplySpanStyle(text.Span(label), isBold, isItalic);

        if (!string.IsNullOrWhiteSpace(url))
        {
            text.Span($" ({url.Trim()})").FontSize(9).FontColor("#64748B");
        }
    }

    private static TextSpanDescriptor ApplySpanStyle(TextSpanDescriptor span, bool isBold, bool isItalic)
    {
        if (isBold)
        {
            span = span.SemiBold();
        }

        if (isItalic)
        {
            span = span.Italic();
        }

        return span;
    }

    private static string InlineText(Inline inline)
    {
        return inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            ContainerInline container => string.Concat(container.Select(InlineText)),
            _ => inline.ToString() ?? string.Empty
        };
    }
}
