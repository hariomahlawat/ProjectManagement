using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;
using Xunit;

namespace ProjectManagement.Tests.Search;

/// <summary>
/// Deterministic rules behind Search V2 relevance and query efficiency: normalisation,
/// tsquery construction, identifier keys, keyset cursors and the correction-offer rule.
/// </summary>
public sealed class SearchV2RelevanceRulesTests
{
    [Theory]
    [InlineData("Fd Arty ‘Fall of Shot obs Sml’ (VROP) - Sri Lanka 2025", "fd arty fall of shot obs sml vrop sri lanka 2025")]
    [InlineData("Fd Arty 'Fall of Shot obs Sml'", "fd arty fall of shot obs sml")]
    [InlineData("\"fall of shot\"", "fall of shot")]
    [InlineData("VR-based Ae Delivery System for C17, C130 and Chinook", "vr based ae delivery system for c17 c130 and chinook")]
    [InlineData("0.22 Rif Firing Sml", "0.22 rif firing sml")]
    [InlineData("TATA 2.5 TON DVG SML UG 1:4", "tata 2.5 ton dvg sml ug 1:4")]
    [InlineData("50106/IR&D/HMV/SDD/24", "50106/ir&d/hmv/sdd/24")]
    [InlineData("Ver. 2 (final)", "ver 2 final")]
    [InlineData("ＡＵＲＡ", "aura")]
    public void Normalizer_FoldsStandalonePunctuationButKeepsTokenJoiners(string input, string expected)
    {
        Assert.Equal(expected, new SearchQueryNormalizer().NormalizeExact(input));
    }

    [Fact]
    public void Normalizer_KeepsDevanagariCombiningMarks()
    {
        // "रक्षा" = र + क + ् (virama, a non-spacing mark) + ष + ा (vowel sign, spacing mark).
        const string word = "रक्षा";

        var exact = new SearchQueryNormalizer().NormalizeExact($"  {word}  मंत्रालय ");

        Assert.Equal($"{word} मंत्रालय", exact);
        Assert.Contains('्', exact);
        Assert.Contains('ा', exact);
    }

    [Fact]
    public void TsQueryBuilders_QuoteWholeTokensSoMarksDecimalsAndPathsSurvive()
    {
        Assert.Equal("'रक्षा' & 'मंत्रालय'", SearchQueryNormalizer.BuildTokenTsQuery("रक्षा मंत्रालय"));
        Assert.Equal("'0.22' & 'rif' & 'firing':*", SearchQueryNormalizer.BuildPrefixTsQuery("0.22 rif firing"));
        Assert.Equal("'30102/vrmdfs/sdd/24'", SearchQueryNormalizer.BuildTokenTsQuery("30102/vrmdfs/sdd/24"));
    }

    [Fact]
    public void TsQueryBuilders_EscapeQuotesAndBackslashesAndDropPunctuationOnlyTokens()
    {
        Assert.Equal("'o''neil' & 'a\\\\b'", SearchQueryNormalizer.BuildTokenTsQuery("o'neil a\\b &&"));
        Assert.Equal(string.Empty, SearchQueryNormalizer.BuildPrefixTsQuery("&& //"));
    }

    [Fact]
    public void Normalizer_ClampsVeryLongQueriesAtAWordBoundary()
    {
        var query = string.Join(' ', Enumerable.Repeat("simulator", 60));

        var result = new SearchQueryNormalizer().Normalize(query);

        Assert.True(result.Original.Length <= SearchQueryNormalizer.MaxQueryLength);
        Assert.EndsWith("simulator", result.Original, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PRJ-0042", "prj42")]
    [InlineData("PRJ0042", "prj42")]
    [InlineData("prj 42", "prj42")]
    [InlineData("SW-15193/2022", "sw151932022")]
    [InlineData("SW15193/2022", "sw151932022")]
    [InlineData("30102 VRMDFS SDD 24", "30102vrmdfssdd24")]
    [InlineData("A-000", "a0")]
    [InlineData("simulator", "")]
    public void IdentifierKey_IsSeparatorAndLeadingZeroInsensitive(string input, string expected)
    {
        Assert.Equal(expected, SearchQueryNormalizer.IdentifierKey(input));
    }

    [Theory]
    [InlineData("t90 driving sml", "t 90 driving sml")]
    [InlineData("t 90 driving sml", "t90 driving sml")]
    [InlineData("84mm rl", "84 mm rl")]
    public void AlphanumericVariants_BridgeHyphenatedAndCompactSpellings(string exact, string expectedVariant)
    {
        Assert.Contains(expectedVariant, SearchQueryNormalizer.AlphanumericVariants(exact));
    }

    [Fact]
    public void AlphanumericVariants_AreEmptyForPlainWords()
    {
        Assert.Empty(SearchQueryNormalizer.AlphanumericVariants("simulator drone"));
    }

    [Theory]
    [InlineData("50%", "50\\%%")]
    [InlineData("a_b", "a\\_b%")]
    [InlineData("c\\d", "c\\\\d%")]
    public void EscapeLike_TreatsUserWildcardsLiterally(string input, string expectedPrefixPattern)
    {
        Assert.Equal(expectedPrefixPattern, SearchQueryNormalizer.EscapeLike(input) + "%");
    }

    [Fact]
    public void KeysetCursor_RoundTripsThePositionExactly()
    {
        var codec = new SearchCursorCodec();
        var position = new SearchCursorPosition(
            3,
            1,
            0.123456789,
            new DateTimeOffset(2025, 4, 1, 10, 30, 15, 123, TimeSpan.Zero).AddTicks(4560),
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            987654321,
            40);

        var encoded = codec.EncodeKeyset("simulator", position, 9);
        var state = codec.DecodeKeyset("simulator", encoded, 9, out var decoded);

        Assert.Equal(SearchCursorState.Valid, state);
        Assert.Equal(position, decoded);
    }

    [Fact]
    public void KeysetCursor_SupportsRowsWithoutAnEventDate()
    {
        var codec = new SearchCursorCodec();
        var position = new SearchCursorPosition(5, 0, 0.5, null, DateTimeOffset.UnixEpoch, 1, 20);

        var state = codec.DecodeKeyset("aura", codec.EncodeKeyset("aura", position, 2), 2, out var decoded);

        Assert.Equal(SearchCursorState.Valid, state);
        Assert.Null(decoded!.EventDate);
    }

    [Fact]
    public void KeysetCursor_ReportsStaleInsteadOfSilentlyRestarting()
    {
        var codec = new SearchCursorCodec();
        var encoded = codec.EncodeKeyset("aura", new SearchCursorPosition(1, 0, 1, null, DateTimeOffset.UnixEpoch, 5, 20), 41);

        Assert.Equal(SearchCursorState.None, codec.DecodeKeyset("aura", null, 41, out _));
        Assert.Equal(SearchCursorState.Stale, codec.DecodeKeyset("aura", encoded, 42, out _));
        Assert.Equal(SearchCursorState.Stale, codec.DecodeKeyset("astrae", encoded, 41, out _));
        Assert.Equal(SearchCursorState.Stale, codec.DecodeKeyset("aura", "not-a-cursor", 41, out _));
        // A rank-offset cursor issued before keyset paging cannot be continued safely.
        Assert.Equal(SearchCursorState.Stale, codec.DecodeKeyset("aura", codec.Encode("aura", 20, 41), 41, out _));
    }

    [Fact]
    public void StaleCursorStatus_IsAReadyOutcome()
    {
        Assert.Equal(4, (int)SearchV2ExecutionStatus.StaleCursor);
    }

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "title_fuzzy" }, true)]
    [InlineData(new[] { "fuzzy,title_fuzzy", "title_fuzzy", "fuzzy", "fuzzy" }, true)]
    [InlineData(new[] { "title_fuzzy", "english_title" }, false)]
    [InlineData(new[] { "identifier_number" }, false)]
    [InlineData(new[] { "simple_fts" }, false)]
    public void CorrectionOffer_DependsOnLexicalEvidenceNotOnResultCount(string[] channels, bool expected)
    {
        var query = new SearchQueryNormalizer().Normalize("simulatr drnoe");

        var offered = SearchEngine.ShouldOfferCorrection(
            query,
            channels.Select(value => new SearchResultChannels(value)).ToArray());

        Assert.Equal(expected, offered);
    }

    [Theory]
    [InlineData(new[] { "0.22 Rif Firing Sml" }, "Simulator", "2024", "0.22 Rif Firing Sml — Training")]
    [InlineData(new[] { "Tk Tp Tac Trg Sml (T4S)", "Al Based Legal Advice Sys Ver 1.0", "al based legal advice sys ver 1.0" }, "Simulator", null, "Al Based Legal Advice Sys Ver 1.0 (+1 more) — Training")]
    [InlineData(new string[0], "Simulator", "2024", "Simulator training 2024")]
    [InlineData(new string[0], null, null, "Training")]
    public void TrainingTitle_NamesWhatWasTrainedOnRatherThanTheGenericType(string[] projects, string? type, string? year, string expected)
    {
        Assert.Equal(expected, SearchProjectionBuilder.TrainingTitle(projects, type, year));
    }

    [Fact]
    public void IdentifierKeyTermKind_FitsTheTermTypeColumn()
    {
        Assert.True(SearchTermKinds.IdentifierKey.Length <= 24);
    }
}
