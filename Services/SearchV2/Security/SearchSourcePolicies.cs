using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;

namespace ProjectManagement.Services.SearchV2.Security;

/// <summary>
/// The single map from a search source (a Search V2 <c>SourceModule</c> or a Legacy
/// <c>GlobalSearchHit.Source</c> label) to the authorization policy a user needs to
/// discover its records. The projection builder stamps <c>RequiredPolicy</c> from this
/// map and the Legacy fallback filters with it, so both engines share one boundary.
/// It is deny-by-default: a source missing from the map is hidden from search results
/// and cannot be indexed, so adding a search provider forces a deliberate entry here.
/// </summary>
public static class SearchSourcePolicies
{
    // A null policy means "any authenticated user", matching the module's own pages.
    private static readonly IReadOnlyDictionary<string, string?> Map =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            // Search V2 source modules.
            ["Projects"] = null,
            ["Project documents"] = null,
            ["Document repository"] = Policies.Documents.View,
            ["FFC"] = null,
            ["IPR"] = Policies.Ipr.View,
            ["Activities"] = null,
            ["Visits"] = ProjectOfficeReportsPolicies.ViewVisits,
            ["Social media"] = null,
            ["Training tracker"] = ProjectOfficeReportsPolicies.ViewTrainingTracker,
            ["ToT"] = ProjectOfficeReportsPolicies.ViewTotTracker,
            ["Proliferation"] = ProjectOfficeReportsPolicies.ViewProliferationTracker,
            ["ARPP"] = ProjectOfficeReportsPolicies.ViewArpp,

            // Legacy fan-out source labels that differ from the V2 names
            // ("Document Repository" matches "Document repository" case-insensitively).
            ["Visits tracker"] = ProjectOfficeReportsPolicies.ViewVisits,
            ["Social media tracker"] = null,
            ["TOT tracker"] = ProjectOfficeReportsPolicies.ViewTotTracker,
            ["Proliferation survey"] = ProjectOfficeReportsPolicies.ViewProliferationTracker
        };

    public static IReadOnlyList<string> KnownSources { get; } = Map.Keys.ToArray();

    /// <summary>Every policy the map can require; the access context evaluates exactly these.</summary>
    public static IReadOnlyList<string> AllPolicies { get; } = Map.Values
        .Where(policy => policy is not null)
        .Select(policy => policy!)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static bool TryGetRequiredPolicy(string? source, out string? policy)
    {
        policy = null;
        return !string.IsNullOrWhiteSpace(source) && Map.TryGetValue(source.Trim(), out policy);
    }

    /// <summary>
    /// The policy to stamp on an indexed entry. Throws for an unmapped source so a new
    /// projection cannot silently become visible to every user.
    /// </summary>
    public static string? RequiredPolicyFor(string sourceModule) =>
        TryGetRequiredPolicy(sourceModule, out var policy)
            ? policy
            : throw new InvalidOperationException(
                $"Search source '{sourceModule}' has no entry in {nameof(SearchSourcePolicies)}. Add it with the policy its pages require.");

    /// <summary>Deny-by-default visibility check for a source.</summary>
    public static bool IsVisible(string? source, IReadOnlySet<string> allowedPolicies)
    {
        ArgumentNullException.ThrowIfNull(allowedPolicies);
        return TryGetRequiredPolicy(source, out var policy)
               && (policy is null || allowedPolicies.Contains(policy));
    }
}
