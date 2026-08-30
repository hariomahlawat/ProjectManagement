using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Services.SearchV2;

public sealed class SearchV2Options
{
    public const string SectionName = "Search:V2";

    public bool Enabled { get; set; } = true;
    public bool ServeV2 { get; set; } = false;
    public bool ShadowMode { get; set; } = true;
    public string[] ServeV2Users { get; set; } = Array.Empty<string>();
    public string[] ServeV2Roles { get; set; } = Array.Empty<string>();

    [Range(5, 100)]
    public int PageSize { get; set; } = 20;

    [Range(5, 100)]
    public int MaxPageSize { get; set; } = 50;

    [Range(5, 20)]
    public int SuggestionLimit { get; set; } = 6;

    [Range(0.05, 0.95)]
    public double FuzzyThreshold { get; set; } = 0.28;

    [Range(1, 500)]
    public int ReciprocalRankK { get; set; } = 60;

    /// <summary>
    /// Small same-tier preference for the canonical entity row (for example, the Project
    /// itself versus a linked document) without overriding stronger lexical match tiers.
    /// </summary>
    [Range(0.0, 0.05)]
    public double CanonicalEntityBoost { get; set; } = 0.0025;

    /// <summary>
    /// Fuzzy retrieval is a true fallback. Once this many strong lexical candidates exist,
    /// expensive trigram channels are skipped for the committed query.
    /// </summary>
    [Range(1, 20)]
    public int FuzzyFallbackStrongCandidateThreshold { get; set; } = 1;

    /// <summary>Minimum natural-language token length eligible for spelling assistance.</summary>
    [Range(3, 12)]
    public int CorrectionMinTokenLength { get; set; } = 4;

    /// <summary>Maximum tokens examined in one correction request.</summary>
    [Range(1, 12)]
    public int CorrectionMaxTokens { get; set; } = 6;

    /// <summary>Maximum edit distance accepted after candidate retrieval.</summary>
    [Range(1, 5)]
    public int CorrectionMaxEditDistance { get; set; } = 3;

    /// <summary>Maximum token-length difference considered during candidate retrieval.</summary>
    [Range(1, 6)]
    public int CorrectionMaxLengthDelta { get; set; } = 3;

    /// <summary>Low pg_trgm threshold used only to build a bounded correction shortlist.</summary>
    [Range(0.05, 0.8)]
    public double CorrectionCandidateTrigramThreshold { get; set; } = 0.18;

    /// <summary>Minimum deterministic multi-signal confidence required to offer a correction.</summary>
    [Range(0.3, 0.95)]
    public double CorrectionMinConfidence { get; set; } = 0.62;

    /// <summary>Maximum vocabulary candidates scored per token.</summary>
    [Range(5, 100)]
    public int CorrectionCandidateLimit { get; set; } = 32;

    /// <summary>Database/search-schema generation understood by this application build.</summary>
    [Range(1, int.MaxValue)]
    public int IndexVersion { get; set; } = 2;

    /// <summary>
    /// Semantic version of the SearchEntry projection. Bump this whenever categories,
    /// indexed fields, metadata, aliases or parent-context semantics change. The active
    /// index generation is rebuilt atomically when this value changes.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ProjectionVersion { get; set; } = 4;

    [Range(5, 3600)]
    public int WorkerIntervalSeconds { get; set; } = 15;

    [Range(1, 1440)]
    public int WorkItemLeaseMinutes { get; set; } = 10;

    [Range(1, 10080)]
    public int FullReconciliationMinutes { get; set; } = 1440;

    [Range(1, 3650)]
    public int QueryLogRetentionDays { get; set; } = 90;

    [Range(120, 1200)]
    public int MaxSnippetCharacters { get; set; } = 420;
}
