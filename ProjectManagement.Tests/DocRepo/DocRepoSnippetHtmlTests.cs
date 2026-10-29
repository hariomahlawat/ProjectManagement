using System.IO;
using System.Text.Encodings.Web;
using ProjectManagement.Services.DocRepo;
using Xunit;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>
/// Search snippets quote text from inside uploaded PDFs. PostgreSQL ts_headline passes through
/// markup it cannot parse as a tag, so these are the kinds of strings it really returns.
/// </summary>
public sealed class DocRepoSnippetHtmlTests
{
    [Theory]
    [InlineData("<mark>radar</mark> <img src=x onerror=alert(1)> <mark>radar</mark>", "<img")]
    [InlineData("<mark>radar</mark> <div style=position:fixed;inset:0;background:red> radar", "<div")]
    [InlineData("radar <script>alert(1)</script>", "<script")]
    public void Render_EncodesMarkupFromTheDocument(string snippet, string forbidden)
    {
        var html = Render(snippet);

        Assert.DoesNotContain(forbidden, html);
        Assert.Contains("&lt;", html);
    }

    [Fact]
    public void Render_KeepsTheSearchHighlights()
    {
        var html = Render("Revised <mark>delegation</mark> of powers & <mark>DFPDS</mark>");

        Assert.Equal("Revised <mark>delegation</mark> of powers &amp; <mark>DFPDS</mark>", html);
    }

    [Fact]
    public void Render_ShowsPageBreaksAsSpaces()
    {
        // ts_headline can quote across a page break of the OCR text (a form feed).
        var html = Render("estimate by 28 Mar 2022.\fThe committee reviewed the <mark>sonar</mark> array");

        Assert.Equal("estimate by 28 Mar 2022. The committee reviewed the <mark>sonar</mark> array", html);
    }

    [Fact]
    public void Render_ClosesAHighlightLeftOpenByTheDocumentText()
    {
        var html = Render("text <mark>radar</mark> and a stray <mark> from the PDF");

        Assert.EndsWith("</mark>", html);
        Assert.Equal(CountOf(html, "<mark>"), CountOf(html, "</mark>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Render_OfNothingIsEmpty(string? snippet)
        => Assert.Equal(string.Empty, Render(snippet));

    private static string Render(string? snippet)
    {
        using var writer = new StringWriter();
        DocRepoSnippetHtml.Render(snippet).WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private static int CountOf(string value, string token)
        => (value.Length - value.Replace(token, string.Empty).Length) / token.Length;
}
