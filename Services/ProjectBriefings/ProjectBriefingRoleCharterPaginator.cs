using ProjectManagement.Models.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Provides the shared Role &amp; Charter slide estimate and preserves source order.
/// Role &amp; Charter is intentionally a single-slide institutional statement: all
/// configured charter entries remain on the same slide and the composer adjusts
/// typography to the available geometry.
/// </summary>
public static class ProjectBriefingRoleCharterPaginator
{
    public static IReadOnlyList<ProjectBriefingRoleCharterPage> Paginate(
        ProjectBriefingRoleCharterData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var showRole = data.Layout != ProjectBriefingRoleCharterLayout.CharterOnly
            && data.RoleStatements.Count > 0;

        return new[]
        {
            new ProjectBriefingRoleCharterPage(
                IsContinuation: false,
                PageNumber: 1,
                RoleStatements: showRole
                    ? data.RoleStatements
                    : Array.Empty<ProjectBriefingRoleCharterEntry>(),
                CharterItems: data.CharterItems)
        };
    }

    public static int EstimateSlideCount(ProjectBriefingRoleCharterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalized = ProjectBriefingRoleCharterOptions.Normalize(
            options.IncludeSlide,
            options.Title,
            options.Layout,
            options.UseSharedContent,
            options.RoleStatements,
            options.CharterItems);

        return normalized.IncludeSlide ? 1 : 0;
    }

    public static int EstimateLineCount(
        ProjectBriefingRoleCharterEntry item,
        int charactersPerLine)
    {
        ArgumentNullException.ThrowIfNull(item);

        var combinedLength = (item.LeadPhrase?.Trim().Length ?? 0)
            + (item.Text?.Trim().Length ?? 0)
            + 3;

        return Math.Max(
            1,
            (int)Math.Ceiling(
                combinedLength / (double)Math.Max(1, charactersPerLine)));
    }
}

public sealed record ProjectBriefingRoleCharterPage(
    bool IsContinuation,
    int PageNumber,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> RoleStatements,
    IReadOnlyList<ProjectBriefingRoleCharterEntry> CharterItems);
