using ProjectManagement.Models;

namespace ProjectManagement.Services.IndustryPartners;

// SECTION: JDP status of a linked project, in display order
public enum JdpProjectStatus
{
    Ongoing = 0,
    Completed = 1,
    Cancelled = 2,
    Archived = 3,
    Deleted = 4
}

// SECTION: Single source for JDP status labels, ordering and summaries
// Every directory, project Overview and lookup projection derives its wording from here.
// Database queries that filter on "current" use the same definition: a project that is
// not deleted, not archived and has an Active lifecycle status.
public static class JdpStatusRules
{
    public const string ContactOnlyKey = "contact";
    public const string CurrentKey = "current";
    public const string PastKey = "past";

    public static JdpProjectStatus ForProject(
        ProjectLifecycleStatus lifecycleStatus,
        bool isArchived,
        bool isDeleted)
    {
        if (isDeleted)
        {
            return JdpProjectStatus.Deleted;
        }

        if (isArchived)
        {
            return JdpProjectStatus.Archived;
        }

        return lifecycleStatus switch
        {
            ProjectLifecycleStatus.Completed => JdpProjectStatus.Completed,
            ProjectLifecycleStatus.Cancelled => JdpProjectStatus.Cancelled,
            _ => JdpProjectStatus.Ongoing
        };
    }

    public static string Label(JdpProjectStatus status) => status switch
    {
        JdpProjectStatus.Completed => "Completed",
        JdpProjectStatus.Cancelled => "Cancelled",
        JdpProjectStatus.Archived => "Archived",
        JdpProjectStatus.Deleted => "Deleted project",
        _ => "Ongoing"
    };

    /// <summary>
    /// Organisation status from its links to live (not deleted) projects: no live link is
    /// "contact only", any current link is "current JDP", otherwise "past JDP".
    /// </summary>
    public static string PartnerStatusKey(int liveProjectCount, int currentProjectCount) =>
        liveProjectCount == 0
            ? ContactOnlyKey
            : currentProjectCount > 0
                ? CurrentKey
                : PastKey;

    public static string PartnerStatusLabel(string statusKey) => statusKey switch
    {
        CurrentKey => "Current JDP",
        PastKey => "Past JDP",
        _ => "Contact only"
    };

    public const string NoOtherProjects = "Not a JDP on any other project";

    /// <summary>
    /// How else an organisation is used, from the status of each of its other (live) projects:
    /// "Also a JDP on 1 other project (archived)" or
    /// "Also a JDP on 3 other projects · 2 ongoing · 1 completed".
    /// </summary>
    public static string UsageSummary(IReadOnlyCollection<JdpProjectStatus> otherProjects)
    {
        ArgumentNullException.ThrowIfNull(otherProjects);
        if (otherProjects.Count == 0)
        {
            return NoOtherProjects;
        }

        if (otherProjects.Count == 1)
        {
            return $"Also a JDP on 1 other project ({Label(otherProjects.First()).ToLowerInvariant()})";
        }

        var parts = otherProjects
            .GroupBy(status => status)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Count()} {Label(group.Key).ToLowerInvariant()}");
        return $"Also a JDP on {otherProjects.Count} other projects · {string.Join(" · ", parts)}";
    }

    /// <summary>The same wording from counts only (the Add JDP search results), where the rest is "other".</summary>
    public static string UsageSummary(int otherProjects, int ongoing, int completed)
    {
        if (otherProjects == 0)
        {
            return NoOtherProjects;
        }

        var parts = new List<string>();
        if (ongoing > 0)
        {
            parts.Add($"{ongoing} ongoing");
        }

        if (completed > 0)
        {
            parts.Add($"{completed} completed");
        }

        var other = Math.Max(0, otherProjects - ongoing - completed);
        if (other > 0)
        {
            parts.Add($"{other} other");
        }

        return $"Also a JDP on {otherProjects} other {(otherProjects == 1 ? "project" : "projects")}" +
               (parts.Count == 0 ? string.Empty : $" · {string.Join(" · ", parts)}");
    }
}
