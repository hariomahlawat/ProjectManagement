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
    public const string Name = "Name";
    public const string Organisation = "Organisation";
    public const string Location = "Location";
    public const string Person = "Person";
    public const string Context = "Context";
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

public sealed record SearchFailedIndexWorkItem(
    long Id,
    string EntityType,
    string EntityKey,
    int RetryCount,
    DateTimeOffset RequestedAtUtc,
    string? LastError);

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

/// <summary>
/// The lightweight active-index pointer the indexing worker reads every tick.
/// </summary>
public sealed record SearchIndexActiveState(
    long ActiveGeneration,
    int IndexVersion,
    DateTimeOffset? LastFullRebuildUtc)
{
    public static SearchIndexActiveState None { get; } = new(0, 0, null);

    public bool HasActiveGeneration => ActiveGeneration > 0;
}

/// <summary>
/// Thrown when a full rebuild would replace an active index generation that was
/// built by a newer projection version (for example by a newer process during a
/// rolling deployment). The rebuild transaction is rolled back.
/// </summary>
public sealed class SearchIndexSupersededException : InvalidOperationException
{
    public SearchIndexSupersededException(int attemptedVersion)
        : base($"Search V2 index is already at a newer projection version than {attemptedVersion}; the rebuild was not activated.")
    {
        AttemptedVersion = attemptedVersion;
    }

    public int AttemptedVersion { get; }
}
