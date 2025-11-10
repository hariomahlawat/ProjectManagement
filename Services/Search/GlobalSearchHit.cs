using System;

namespace ProjectManagement.Services.Search;

// SECTION: Global search result model
public sealed record GlobalSearchHit(
    string Source,
    string Title,
    string? Snippet,
    string Url,
    DateTimeOffset? Date,
    decimal Score,
    string? FileType,
    string? Extra);
