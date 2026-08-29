namespace ProjectManagement.Services.SearchV2.Models;

public enum SearchVisibilityMode : short
{
    Authenticated = 0,
    OwnerOnly = 1,
    Principals = 2
}

public static class SearchTermKinds
{
    public const string Identifier = "Identifier";
    public const string Alias = "Alias";
}

public sealed record SearchProjectionTerm(string Term, string NormalizedTerm, string Kind);

public sealed record SearchProjectionPrincipal(string Type, string Value);

public sealed record SearchProjection(
    string EntityType,
    string EntityKey,
    string CanonicalEntityType,
    string CanonicalEntityKey,
    int? ParentProjectId,
    string SourceModule,
    string ResultCategory,
    string Title,
    string NormalizedTitle,
    string? Subtitle,
    string CanonicalUrl,
    string? IdentifierText,
    string? AliasText,
    string? StructuredText,
    string? NarrativeText,
    string FuzzyText,
    string? Status,
    string? FileType,
    DateTimeOffset? EventDateUtc,
    DateTimeOffset UpdatedAtUtc,
    SearchVisibilityMode VisibilityMode,
    string? RequiredPolicy,
    string? OwnerUserId,
    int IndexVersion,
    IReadOnlyList<SearchProjectionTerm> Terms,
    IReadOnlyList<SearchProjectionPrincipal> Principals,
    string? MetadataJson = null);

public sealed record SearchIndexWorkItem(
    long Id,
    string EntityType,
    string EntityKey,
    int RetryCount);

public sealed record SearchIndexHealth(
    bool IsReady,
    long ActiveGeneration,
    int IndexVersion,
    long EntryCount,
    long PendingItems,
    long FailedItems,
    DateTimeOffset? LastFullRebuildUtc,
    DateTimeOffset? LastReconciliationUtc,
    string? LastError);
