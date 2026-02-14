using System;
using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;

namespace ProjectManagement.Services.Text;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    // SECTION: Markdown and sanitization pipelines
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public string ToSafeHtml(string? markdown)
    {
        var trimmed = markdown?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var unsafeHtml = Markdig.Markdown.ToHtml(trimmed, Pipeline);
        var safeHtml = Sanitizer.Sanitize(unsafeHtml).Trim();
        return safeHtml;
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // SECTION: Strict allowlist
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        sanitizer.AllowedTags.UnionWith(new[]
        {
            "p", "br", "hr", "strong", "em", "ul", "ol", "li", "h1", "h2", "h3", "h4", "a", "code", "pre", "blockquote"
        });

        sanitizer.AllowedAttributes.UnionWith(new[]
        {
            "href", "title", "rel"
        });

        // SECTION: Link hardening
        sanitizer.PostProcessNode += (_, args) =>
        {
            if (args.Node is not IElement element || !element.NodeName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var href = element.GetAttribute("href");
            if (!IsAllowedHref(href))
            {
                element.RemoveAttribute("href");
            }

            element.SetAttribute("rel", "noopener noreferrer");
            element.RemoveAttribute("target");
        };

        return sanitizer;
    }

    // SECTION: Link validation rules
    private static bool IsAllowedHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        if (!Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out var uri))
        {
            return false;
        }

        if (!uri.IsAbsoluteUri)
        {
            return true;
        }

        return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
    }
}
