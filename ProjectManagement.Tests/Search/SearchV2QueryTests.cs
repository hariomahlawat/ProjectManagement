using Microsoft.Extensions.Options;
using ProjectManagement.Services.Search;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using Xunit;

namespace ProjectManagement.Tests.Search;

public sealed class SearchV2QueryTests
{
    [Theory]
    [InlineData(true, 8, 8)]
    [InlineData(true, 7, 7)]
    [InlineData(true, 6, null)]
    [InlineData(true, 9, null)]
    [InlineData(false, 8, null)]
    public void ServableIndexVersion_IsThisBuildsVersionOrAnOlderOneDownToTheFloor(bool hasActive, int activeVersion, int? expected)
    {
        var options = new SearchV2Options { ProjectionVersion = 8, MinimumServableProjectionVersion = 7 };
        var state = new SearchIndexActiveState(hasActive ? 12 : 0, activeVersion, null);

        Assert.Equal(expected, options.ServableIndexVersion(state));
    }

    [Theory]
    [InlineData("ir", false)]
    [InlineData("ab", false)]
    [InlineData("abc", true)]
    [InlineData("radar", true)]
    public void PartialMatchChannels_NeedThreeCharacters(string query, bool expectPartial)
    {
        var normalized = new SearchQueryNormalizer().Normalize(query);

        var terms = SearchQueryTerms.Create(normalized, Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(expectPartial, terms.ContainsLike.Length > 0);
        Assert.Equal(expectPartial, terms.PrefixTsQuery.Length > 0);
        // Whole-token and title-prefix (btree) matching still run for short searches.
        Assert.NotEmpty(terms.ExactTokenTsQuery);
        Assert.StartsWith(query, terms.PrefixLike, StringComparison.Ordinal);
    }

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

    // An expansion is literal text: "$1" or "$&" in it is not a regular-expression substitution.
    [Fact]
    public void AliasExpander_TreatsDollarSignsInAnExpansionAsText()
    {
        var rules = new[] { new SearchAliasRule("ToT", "tot", "Fund $1 and $& plan") };

        var result = SearchAliasQueryExpander.Expand("aura tot", rules);

        Assert.Contains("aura \"Fund $1 and $& plan\"", result.AliasWebSearchQuery, StringComparison.OrdinalIgnoreCase);
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

    private static readonly System.Text.RegularExpressions.Regex CorrectionTokens =
        new(@"[\p{L}\p{N}][\p{L}\p{N}._/-]*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static (int Index, int Length)[] Spans(string text) =>
        CorrectionTokens.Matches(text).Select(match => (match.Index, match.Length)).ToArray();

    [Fact]
    public void CorrectionScorer_RebuildsMultiTokenQueryWithoutChangingProtectedTerms()
    {
        var rebuilt = SearchCorrectionScorer.ApplyReplacements(
            "iit hydrbad meeting",
            Spans("iit hydrbad meeting"),
            new Dictionary<int, string> { [1] = "hyderabad" });

        Assert.Equal("iit hyderabad meeting", rebuilt);
    }

    [Fact]
    public void CorrectionScorer_KeepsJoinersBetweenTokens()
    {
        // "ir&d" is two tokens ("ir", "d") joined by "&"; rebuilding with spaces gave "ir d".
        var rebuilt = SearchCorrectionScorer.ApplyReplacements(
            "ir&d hydrbad",
            Spans("ir&d hydrbad"),
            new Dictionary<int, string> { [2] = "hyderabad" });

        Assert.Equal("ir&d hyderabad", rebuilt);
    }

    [Theory]
    [InlineData("anti-tank ATGM", "anti tank atgm", "false,false,true")]
    [InlineData("ATGM-launcher trials", "atgm launcher trials", "true,false,false")]
    [InlineData("Hydrbad AURA meeting", "hydrbad aura meeting", "false,true,false")]
    [InlineData("atomz-123 hydrbad", "atomz 123 hydrbad", "true,true,false")]
    public void CorrectionScorer_ProtectsEachTokenByItsOwnTypedSpelling(string original, string exact, string expected)
    {
        var normalizer = new SearchQueryNormalizer();
        Assert.Equal(exact, normalizer.NormalizeExact(original));
        var tokens = CorrectionTokens.Matches(exact).Select(match => match.Value).ToArray();

        var result = SearchCorrectionScorer.ProtectedTokens(original, tokens, normalizer.NormalizeExact, CorrectionTokens);

        Assert.Equal(expected, string.Join(',', result.Select(value => value ? "true" : "false")));
    }

    [Theory]
    [InlineData("Hydrabad Radar", "Hyderabad Radar")]
    [InlineData("HYDRABAD radar", "HYDERABAD radar")]
    [InlineData("hydrabad-radar", "hyderabad-radar")]
    public void CorrectionScorer_WritesTheCorrectionIntoTheTypedText(string original, string expected)
    {
        var normalizer = new SearchQueryNormalizer();
        var tokens = CorrectionTokens.Matches(normalizer.NormalizeExact(original)).Select(match => match.Value).ToArray();

        var result = SearchCorrectionScorer.ReplaceInTypedText(original, tokens, new Dictionary<int, string> { [0] = "hyderabad" }, normalizer.NormalizeExact, CorrectionTokens);

        Assert.Equal(expected, result);
    }

    // Phones show two lines of the excerpt: it starts a few words before the match instead of
    // centring it.
    [Fact]
    public void Snippet_StartsShortlyBeforeTheMatch()
    {
        var service = new SearchHighlightService(Options.Create(new SearchV2Options()));
        var text = string.Concat(Enumerable.Repeat("routine maintenance schedule ", 40)) + "the radar passed its trial " + string.Concat(Enumerable.Repeat("further notes follow ", 40));

        var snippet = service.BuildSnippet(null, text, new[] { "radar" })!;

        Assert.InRange(snippet.IndexOf("radar", StringComparison.Ordinal), 0, 80);
    }

    [Fact]
    public void Snippet_ChoosesAndMarksInflectionsOfAStem_NotWordsThatOnlyStartWithIt()
    {
        var service = new SearchHighlightService(Options.Create(new SearchV2Options()));
        var stems = new[] { "station" };

        var snippet = service.BuildSnippet("Stationery cupboard · Published", "Radar stations were moved north.", new[] { "stationz" }, stems);
        var marked = service.Highlight(snippet, new[] { "stationz" }, stems).Where(segment => segment.Highlighted).Select(segment => segment.Text).ToArray();

        Assert.Equal("Radar stations were moved north.", snippet);
        Assert.Equal(new[] { "stations" }, marked);
    }

    [Theory]
    [InlineData("Two reports were filed", true)]
    [InlineData("The stationery cupboard", false)]
    public void MatchEvidence_CountsAWordByStemOnlyAsAnInflection(string narrative, bool expected)
    {
        var normalizer = new SearchQueryNormalizer();
        var query = normalizer.Normalize(narrative.Contains("stationery") ? "stations" : "report");
        var stems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["stations"] = "station", ["report"] = "report" };

        var field = SearchMatchEvidenceResolver.Resolve(query, "Unrelated title", null, narrative, null, "ProjectDocument", "english_fts", stems);

        Assert.Equal(expected, field is not null);
    }

    [Theory]
    [InlineData("simulator -drone", "simulator", "drone")]
    [InlineData("radar -drone -uav", "radar", "drone,uav")]
    [InlineData("anti-tank simulator", "anti tank simulator", "")]
    [InlineData("-drone", "drone", "")]
    public void Normalizer_TreatsALeadingMinusAsAnExclusion(string query, string exact, string excluded)
    {
        var normalized = new SearchQueryNormalizer().Normalize(query);

        Assert.Equal(exact, normalized.Exact);
        Assert.Equal(excluded, string.Join(',', normalized.Excluded));
        Assert.All(normalized.Excluded, word => Assert.DoesNotContain(word, normalized.HighlightTerms, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void DidYouMean_IsNotOfferedOnAnEmptyTabOfASearchThatFoundResults()
    {
        var query = new SearchQueryNormalizer().Normalize("hydraulics");

        Assert.False(SearchEngine.ShouldOfferCorrectionOnPage(query, Array.Empty<SearchResultChannels>(), totalHits: 12));
        Assert.True(SearchEngine.ShouldOfferCorrectionOnPage(query, Array.Empty<SearchResultChannels>(), totalHits: 0));
    }

    [Fact]
    public void CorrectionScorer_ProtectsEverythingWhenTheTypedTextCannotBeAligned()
    {
        var result = SearchCorrectionScorer.ProtectedTokens("hydrbad", new[] { "hydrbad", "extra" }, value => value.ToLowerInvariant(), CorrectionTokens);

        Assert.All(result, Assert.True);
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
