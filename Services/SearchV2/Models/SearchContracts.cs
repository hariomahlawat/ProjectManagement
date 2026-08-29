using System.Collections.ObjectModel;

namespace ProjectManagement.Services.SearchV2.Models;

public enum SearchV2ExecutionStatus
{
    Success = 0,
    Disabled = 1,
    IndexNotReady = 2,
    QueryFailed = 3
}

public sealed record SearchTextSegment(string Text, bool Highlighted);

public sealed record SearchRelatedResult(string SourceModule, string Label, long Count);

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
    string? MetadataJson,
    int? MatchTier = null,
    string? MatchChannels = null);

public sealed record SearchFacetValue(string Value, long Count, string? Label = null);

public sealed record SearchFacets(
    IReadOnlyList<SearchFacetValue> Categories,
    IReadOnlyList<SearchFacetValue> Sources,
    IReadOnlyList<SearchFacetValue> Projects,
    IReadOnlyList<SearchFacetValue> Statuses,
    IReadOnlyList<SearchFacetValue> FileTypes,
    IReadOnlyList<SearchFacetValue> Stages,
    bool DetailedLoaded = true)
{
    public static SearchFacets Empty { get; } = new(
        Array.Empty<SearchFacetValue>(),
        Array.Empty<SearchFacetValue>(),
        Array.Empty<SearchFacetValue>(),
        Array.Empty<SearchFacetValue>(),
        Array.Empty<SearchFacetValue>(),
        Array.Empty<SearchFacetValue>(),
        false);
}

public sealed record SearchRequest(
    string Query,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? Sources = null,
    string? Cursor = null,
    int? PageSize = null,
    IReadOnlyList<int>? ProjectIds = null,
    IReadOnlyList<string>? Statuses = null,
    IReadOnlyList<string>? FileTypes = null,
    IReadOnlyList<string>? Stages = null,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    bool IncludeDetailedFacets = true,
    bool FacetsOnly = false);

public sealed record SearchResponse(
    string Query,
    IReadOnlyList<SearchResult> Results,
    long TotalHits,
    long FilteredHits,
    SearchFacets Facets,
    string? NextCursor,
    long QueryTimeMilliseconds,
    bool IsReady,
    bool IsPartial = false,
    string? CorrectedQuery = null,
    SearchV2ExecutionStatus ExecutionStatus = SearchV2ExecutionStatus.Success,
    string? DiagnosticId = null)
{
    public static SearchResponse Empty(string query) => new(
        query,
        Array.Empty<SearchResult>(),
        0,
        0,
        SearchFacets.Empty,
        null,
        0,
        true);

    public static SearchResponse NotReady(
        string query,
        SearchV2ExecutionStatus status = SearchV2ExecutionStatus.IndexNotReady,
        string? diagnosticId = null) => new(
        query,
        Array.Empty<SearchResult>(),
        0,
        0,
        SearchFacets.Empty,
        null,
        0,
        false,
        false,
        null,
        status,
        diagnosticId);
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
    long FilteredHits,
    SearchFacets Facets,
    string? NextCursor,
    long QueryTimeMilliseconds,
    bool UsedSearchV2,
    bool IsPartial,
    string? CorrectedQuery,
    bool FellBackToLegacy = false,
    SearchV2ExecutionStatus V2ExecutionStatus = SearchV2ExecutionStatus.Disabled,
    string? V2DiagnosticId = null)
{
    public string EngineLabel => UsedSearchV2
        ? "V2"
        : FellBackToLegacy
            ? "Legacy fallback"
            : "Legacy";

    public static SearchGatewayResponse Empty(string query) => new(
        query,
        Array.Empty<SearchResult>(),
        0,
        0,
        SearchFacets.Empty,
        null,
        0,
        false,
        false,
        null);
}
