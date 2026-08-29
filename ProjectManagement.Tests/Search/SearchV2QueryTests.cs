using Microsoft.Extensions.Options;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Query;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchV2QueryTests
{
    [Fact]
    public void Normalizer_ExpandsPrismTerminologyWithoutLosingLiteralQuery()
    {
        var normalizer = new SearchQueryNormalizer();

        var result = normalizer.Normalize("high-tech");

        Assert.Equal("high-tech", result.Original);
        Assert.Equal("high tech", result.Exact);
        Assert.Contains("hightech", result.Expansions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("high-tech", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ToT", "tot", "transfer of technology")]
    [InlineData("AoN", "aon", "approval of necessity")]
    [InlineData("ARPP", "arpp", "annual rolled-on procurement plan")]
    public void Normalizer_ExpandsRegisteredMilitaryTerminology(string query, string exact, string expansion)
    {
        var normalizer = new SearchQueryNormalizer();

        var result = normalizer.Normalize(query);

        Assert.Equal(exact, result.Exact);
        Assert.Contains(expansion, result.Expansions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cursor_IsBoundToTheOriginalQuery()
    {
        var cursor = new SearchCursorCodec();
        var encoded = cursor.Encode("AURA", 20);

        Assert.True(cursor.TryDecode("AURA", encoded, out var rank));
        Assert.Equal(20, rank);
        Assert.False(cursor.TryDecode("ASTRAE", encoded, out _));
    }


    [Fact]
    public void Normalizer_MultiTermExpansionPreservesMandatoryTerms()
    {
        var normalizer = new SearchQueryNormalizer();

        var result = normalizer.Normalize("AURA ToT");

        Assert.Contains("aura tot", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aura \"transfer of technology\"", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" OR \"transfer of technology\"", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cursor_IsRejectedWhenIndexGenerationChanges()
    {
        var cursor = new SearchCursorCodec();
        var encoded = cursor.Encode("AURA", 20, 41);

        Assert.True(cursor.TryDecode("AURA", encoded, 41, out var rank));
        Assert.Equal(20, rank);
        Assert.False(cursor.TryDecode("AURA", encoded, 42, out _));
    }

    [Fact]
    public void Highlight_ReturnsStructuredSegmentsRatherThanExecutableMarkup()
    {
        var service = new SearchHighlightService(Options.Create(new SearchV2Options()));
        const string source = "<img src=x onerror=alert(1)> AURA technical evaluation";

        var segments = service.Highlight(source, new[] { "AURA" });

        Assert.Contains(segments, segment => segment.Highlighted && segment.Text == "AURA");
        Assert.Contains(segments, segment => !segment.Highlighted && segment.Text.Contains("<img", StringComparison.Ordinal));
        Assert.DoesNotContain(segments, segment => segment.Text.Contains("<mark>", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LegacySnippet_StripsOnlyControlledHeadlineMarkers()
    {
        var service = new SearchHighlightService(Options.Create(new SearchV2Options()));

        var result = service.PlainLegacySnippet("alpha <mark>AURA</mark> <script>x</script>");

        Assert.Equal("alpha AURA <script>x</script>", result);
    }

    [Theory]
    [InlineData("50%", "%50\\%%")]
    [InlineData("A_B", "%A\\_B%")]
    [InlineData("C\\D", "%C\\\\D%")]
    public void LegacyLikePattern_EscapesWildcardCharacters(string input, string expected)
    {
        Assert.Equal(expected, SearchLikePattern.Contains(input));
    }
}
