using System.Net;
using Microsoft.AspNetCore.Html;

namespace ProjectManagement.Services.DocRepo;

/// <summary>
/// Turns a PostgreSQL ts_headline snippet into safe HTML. The snippet's text comes from inside
/// uploaded PDFs, and ts_headline passes through any markup it cannot parse as a tag, so the
/// whole snippet is encoded first and only the highlight markers are turned back into
/// &lt;mark&gt; elements.
/// </summary>
public static class DocRepoSnippetHtml
{
    private const string StartMarker = "<mark>";
    private const string StopMarker = "</mark>";

    public static IHtmlContent Render(string? snippet)
    {
        if (string.IsNullOrEmpty(snippet))
        {
            return HtmlString.Empty;
        }

        // OCR text separates pages with a form feed and may carry other control characters;
        // they would show as boxes, so each becomes a space.
        var readable = new string(snippet.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        var encoded = WebUtility.HtmlEncode(readable)
            .Replace(WebUtility.HtmlEncode(StartMarker), StartMarker, StringComparison.Ordinal)
            .Replace(WebUtility.HtmlEncode(StopMarker), StopMarker, StringComparison.Ordinal);

        // Text inside the PDF could itself contain "<mark>": balance any stray markers so a
        // highlight never runs on past the snippet.
        var opened = CountOf(encoded, StartMarker) - CountOf(encoded, StopMarker);
        if (opened > 0)
        {
            encoded += string.Concat(Enumerable.Repeat(StopMarker, opened));
        }

        return new HtmlString(encoded);
    }

    private static int CountOf(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
