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

        Assert.Equal(normalizedAlias, result.WebSearchQuery);
        Assert.Contains(expansion, result.AliasWebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expansion, result.Expansions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AliasExpander_MultiTermExpansionPreservesMandatoryTerms()
    {
        var rules = new[] { new SearchAliasRule("ToT", "tot", "Transfer of Technology") };

        var result = SearchAliasQueryExpander.Expand("aura tot", rules);

        Assert.Equal("aura tot", result.WebSearchQuery);
        Assert.Contains("aura \"Transfer of Technology\"", result.AliasWebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aura transfer of technology", result.AliasExactQueries, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(" OR \"Transfer of Technology\"", result.AliasWebSearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AliasExpander_DoesNotReplaceAliasInsideAnotherWord()
    {
        var rules = new[] { new SearchAliasRule("ToT", "tot", "Transfer of Technology") };

        var result = SearchAliasQueryExpander.Expand("total simulator", rules);

        Assert.Equal("total simulator", result.WebSearchQuery);
        Assert.Empty(result.AliasWebSearchQuery);
        Assert.Empty(result.AliasExactQueries);
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
    [Fact]
    public void Highlight_PrefixMatchHighlightsWholeLexicalWord()
    {
        var service = new SearchHighlightService(Options.Create(new SearchV2Options()));

        var segments = service.Highlight("Transfer of Technology", new[] { "tech" });

        Assert.Contains(segments, segment => segment.Highlighted && segment.Text == "Technology");
        Assert.DoesNotContain(segments, segment => segment.Highlighted && segment.Text == "Tech");
    }

    [Fact]
    public void MatchEvidence_ComposesTitleAndDocumentTextForMultiFieldQuery()
    {
        var query = new SearchQueryNormalizer().Normalize("high tech");

        var evidence = SearchMatchEvidenceResolver.Resolve(
            query,
            title: "CPDS North Tech Symposium 2026",
            structuredText: null,
            narrativeText: "The document discusses high altitude challenges.",
            metadataJson: null,
            entityType: "DocRepoDocument",
            channels: "simple_fts");

        Assert.Equal("Title + document text", evidence);
    }

    [Fact]
    public void MatchEvidence_UsesTitleWhenEntireQueryIsCoveredByTitle()
    {
        var query = new SearchQueryNormalizer().Normalize("high tech");

        var evidence = SearchMatchEvidenceResolver.Resolve(
            query,
            title: "Mockup based Pinaka High-Tech Sml",
            structuredText: null,
            narrativeText: null,
            metadataJson: null,
            entityType: "Project",
            channels: "title_phrase,title_tokens_exact");

        Assert.Equal("Title", evidence);
    }

    [Fact]
    public void AliasExpander_TreatsHiTechAndHighTechAsControlledEquivalentPhrases()
    {
        var rules = new[]
        {
            new SearchAliasRule("High Tech", "high tech", "hi tech"),
            new SearchAliasRule("Hi Tech", "hi tech", "high tech")
        };

        var highTech = SearchAliasQueryExpander.Expand("high tech", rules);
        var hiTech = SearchAliasQueryExpander.Expand("hi tech", rules);

        Assert.Contains("hi tech", highTech.Expansions, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("high tech", hiTech.Expansions, StringComparer.OrdinalIgnoreCase);
    }


    [Theory]
    [InlineData("hydrbd", "hyderabad", 0.30, 18, 5)]
    [InlineData("hydrbad", "hyderabad", 0.38, 18, 5)]
    [InlineData("hyderbad", "hyderabad", 0.55, 18, 5)]
    public void CorrectionScorer_AcceptsRepresentativeHyderabadTypos(
        string input,
        string candidate,
        double trigram,
        int frequency,
        int authority)
    {
        var options = new SearchV2Options();
        var result = SearchCorrectionScorer.SelectBest(
            input,
            new[] { new SearchCorrectionCandidate(candidate, frequency, authority, trigram) },
            options);

        Assert.NotNull(result);
        Assert.Equal("hyderabad", result!.Token);
    }

    [Theory]
    [InlineData("AURA")]
    [InlineData("ARPP")]
    [InlineData("T90")]
    [InlineData("985060")]
    [InlineData("GEM2026B7803679")]
    public void CorrectionScorer_ProtectsAcronymsAndIdentifiers(string token)
    {
        Assert.True(SearchCorrectionScorer.IsProtectedOriginalToken(token));
    }

    [Fact]
    public void CorrectionScorer_PrefersAuthoritativeFrequentLocationOverSimilarLowAuthorityWord()
    {
        var options = new SearchV2Options();
        var result = SearchCorrectionScorer.SelectBest(
            "hydrbd",
            new[]
            {
                new SearchCorrectionCandidate("hybrid", 1, 2, 0.48),
                new SearchCorrectionCandidate("hyderabad", 18, 5, 0.30)
            },
            options);

        Assert.NotNull(result);
        Assert.Equal("hyderabad", result!.Token);
    }

    [Fact]
    public void CorrectionScorer_RebuildsMultiTokenQueryWithoutChangingProtectedTerms()
    {
        var rebuilt = SearchCorrectionScorer.ApplyReplacements(
            new[] { "iit", "hydrbad", "meeting" },
            new Dictionary<int, string> { [1] = "hyderabad" });

        Assert.Equal("iit hyderabad meeting", rebuilt);
    }

    [Fact]
    public void AliasExpander_KeepsPrimaryWebQueryLiteralAndSeparatesAliasQuery()
    {
        var rules = new[] { new SearchAliasRule("High Tech", "high tech", "hi tech") };

        var result = SearchAliasQueryExpander.Expand("high tech", rules);

        Assert.Equal("high tech", result.WebSearchQuery);
        Assert.Contains("hi tech", result.AliasWebSearchQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" OR ", result.WebSearchQuery, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void MatchEvidence_UsesTitleForControlledAliasTitlePhrase()
    {
        var query = new SearchQueryNormalizer().Normalize("high tech");

        var evidence = SearchMatchEvidenceResolver.Resolve(
            query,
            title: "MANAGEMENT OF HI-TECH /EXTENDED TENURE APPTS",
            structuredText: null,
            narrativeText: "High Tech policy material",
            metadataJson: null,
            entityType: "DocRepoDocument",
            channels: "alias_title_phrase,alias_fts");

        Assert.Equal("Title", evidence);
    }

    [Theory]
    [InlineData("NotStarted", "Not Started")]
    [InlineData("InProgress", "In Progress")]
    [InlineData("BID", "BID")]
    [InlineData("Not Started", "Not Started")]
    public void DisplayFormatter_HumanizesMachineStatusValues(string input, string expected)
    {
        Assert.Equal(expected, SearchDisplayValueFormatter.Status(input));
    }

}
