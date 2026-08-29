using Microsoft.Extensions.Options;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchV2QueryTests
{
    [Fact]
    public void Normalizer_IsDeterministicAndDatabaseIndependent()
    {
        var normalizer = new SearchQueryNormalizer();

        var result = normalizer.Normalize("  AURA   ToT  ");

        Assert.Equal("AURA ToT", result.Original);
        Assert.Equal("aura tot", result.Exact);
        Assert.Equal("aura tot", result.WebSearchQuery);
        Assert.Empty(result.Expansions);
        Assert.Contains("AURA", result.HighlightTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ToT", result.HighlightTerms, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ToT", "tot", "Transfer of Technology")]
    [InlineData("AoN", "aon", "Approval of Necessity")]
    [InlineData("ARPP", "arpp", "Annual Rolled-on Procurement Plan")]
    public void AliasExpander_ExpandsRegisteredMilitaryTerminology(string query, string normalizedAlias, string expansion)
    {
        var rules = new[] { new SearchAliasRule(query, normalizedAlias, expansion) };

        var result = SearchAliasQueryExpander.Expand(normalizedAlias, rules);

        Assert.Contains(normalizedAlias, result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expansion, result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expansion, result.Expansions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AliasExpander_MultiTermExpansionPreservesMandatoryTerms()
    {
        var rules = new[] { new SearchAliasRule("ToT", "tot", "Transfer of Technology") };

        var result = SearchAliasQueryExpander.Expand("aura tot", rules);

        Assert.Contains("aura tot", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aura \"Transfer of Technology\"", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" OR \"Transfer of Technology\"", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AliasExpander_DoesNotReplaceAliasInsideAnotherWord()
    {
        var rules = new[] { new SearchAliasRule("ToT", "tot", "Transfer of Technology") };

        var result = SearchAliasQueryExpander.Expand("total simulator", rules);

        Assert.Equal("total simulator", result.WebSearchQuery);
        Assert.Empty(result.Expansions);
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
    [InlineData("high-tech", "high tech")]
    [InlineData("HI–TECH", "hi tech")]
    [InlineData("high_tech", "high tech")]
    [InlineData("T—Hub", "t hub")]
    public void Normalizer_CanonicalizesDashAndUnderscoreSeparators(string input, string expected)
    {
        var normalizer = new SearchQueryNormalizer();

        Assert.Equal(expected, normalizer.NormalizeExact(input));
    }

    [Fact]
    public void Normalizer_AddsNormalizedTokensForCrossPunctuationHighlighting()
    {
        var normalizer = new SearchQueryNormalizer();

        var result = normalizer.Normalize("high-tech");

        Assert.Contains("high", result.HighlightTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("tech", result.HighlightTerms, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TextQuality_SanitizesReplacementAndControlCharacters()
    {
        var value = SearchTextQuality.SanitizeForDisplay("alpha\uFFFD\u0001  beta\n gamma");

        Assert.Equal("alpha beta gamma", value);
    }

    [Fact]
    public void TextQuality_SanitizationPreservesReadablePunctuation()
    {
        var value = SearchTextQuality.SanitizeForDisplay("AURA / High-Tech (TRL-8), IIT Hyderabad.");

        Assert.Equal("AURA / High-Tech (TRL-8), IIT Hyderabad.", value);
    }

    [Fact]
    public void TextQuality_PenalizesCorruptedOcrMoreThanReadableText()
    {
        var clean = SearchTextQuality.Score("High Tech symposium on autonomous systems and electronic warfare.");
        var noisy = SearchTextQuality.Score("provid* tkeff p*rti*ipa\uFFFD\u0001 ** ** 1 x qz");

        Assert.True(clean > 0.9);
        Assert.True(noisy < clean);
        Assert.True(noisy < 0.7);
    }

    [Fact]
    public void Snippet_SuppressesVeryLowQualityNarrativeWhenStructuredTextDoesNotMatch()
    {
        var service = new SearchHighlightService(Options.Create(new SearchV2Options()));
        var noisy = string.Join(" ", Enumerable.Repeat("\uFFFD * x", 80)) + " AURA";

        var snippet = service.BuildSnippet(null, noisy, new[] { "AURA" });

        Assert.Null(snippet);
    }

    [Fact]
    public void SearchResponse_NotReadyPreservesTypedFailureStatusAndDiagnosticId()
    {
        var response = SearchResponse.NotReady(
            "aura",
            SearchV2ExecutionStatus.QueryFailed,
            "ABC123DEF456");

        Assert.False(response.IsReady);
        Assert.Equal(SearchV2ExecutionStatus.QueryFailed, response.ExecutionStatus);
        Assert.Equal("ABC123DEF456", response.DiagnosticId);
        Assert.Equal(0L, response.TotalHits);
        Assert.Equal(0L, response.FilteredHits);
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
