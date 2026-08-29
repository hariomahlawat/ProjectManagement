using System.Collections.ObjectModel;

namespace ProjectManagement.Services.SearchV2.Models;

public sealed record SearchTextSegment(string Text, bool Highlighted);

public sealed record SearchRelatedResult(string SourceModule, string Label);

public sealed record SearchResult(
    long SearchEntryId,
    string EntityType,
    string EntityKey,
    string CanonicalEntityType,
    string CanonicalEntityKey,
    int? ParentProjectId,
    string SourceModule,
    string Category,
    string Title,
    IReadOnlyList<SearchTextSegment> TitleSegments,
    string? Subtitle,
    string Url,
    string? Snippet,
    IReadOnlyList<SearchTextSegment> SnippetSegments,
    string? MatchedField,
    DateTimeOffset? EventDate,
    string? FileType,
    string? Status,
    double Score,
    int Rank,
    IReadOnlyList<SearchRelatedResult> RelatedResults,
    string? MetadataJson);

public sealed record SearchFacetValue(string Value, long Count);

public sealed record SearchFacets(
    IReadOnlyList<SearchFacetValue> Categories,
    IReadOnlyList<SearchFacetValue> Sources)
{
    public static SearchFacets Empty { get; } = new(
        Array.Empty<SearchFacetValue>(),
        Array.Empty<SearchFacetValue>());
}

public sealed record SearchRequest(
    string Query,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? Sources = null,
    string? Cursor = null,
    int? PageSize = null);

public sealed record SearchResponse(
    string Query,
    IReadOnlyList<SearchResult> Results,
    long TotalHits,
    SearchFacets Facets,
    string? NextCursor,
    long QueryTimeMilliseconds,
    bool IsReady,
    bool IsPartial = false,
    string? CorrectedQuery = null)
{
    public static SearchResponse NotReady(string query) => new(
        query,
        Array.Empty<SearchResult>(),
        0,
        SearchFacets.Empty,
        null,
        0,
        false);
}

public sealed record SearchSuggestion(
    string Title,
    string? Subtitle,
    string Url,
    string SourceModule,
    string Category,
    string? Identifier);

public sealed record SearchGatewayResponse(
    string Query,
    IReadOnlyList<SearchResult> Results,
    long TotalHits,
    SearchFacets Facets,
    string? NextCursor,
    long QueryTimeMilliseconds,
    bool UsedSearchV2,
    bool IsPartial,
    string? CorrectedQuery)
{
    public static SearchGatewayResponse Empty(string query) => new(
        query,
        Array.Empty<SearchResult>(),
        0,
        SearchFacets.Empty,
        null,
        0,
        false,
        false,
        null);
}
