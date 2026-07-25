using ProjectManagement.Models;

namespace ProjectManagement.Services.IndustryPartners;

public sealed record ProjectJdpPartnerProfileDto(
    int Id,
    string Name,
    string? Location,
    IReadOnlyList<ProjectJdpLinkedProjectDto> OtherProjects)
{
    public int OtherProjectCount => OtherProjects.Count;

    public int OtherOngoingProjectCount => OtherProjects.Count(project => project.StatusLabel == "Ongoing");

    public int OtherCompletedProjectCount => OtherProjects.Count(project => project.StatusLabel == "Completed");

    public int OtherProjectStatusCount => Math.Max(
        0,
        OtherProjectCount - OtherOngoingProjectCount - OtherCompletedProjectCount);

    public string UsageSummary => ProjectMultiJdpProfileDto.BuildUsageSummary(
        OtherProjectCount,
        OtherOngoingProjectCount,
        OtherCompletedProjectCount);
}

public sealed record ProjectMultiJdpProfileDto(
    int ProjectId,
    IReadOnlyList<ProjectJdpPartnerProfileDto> Partners)
{
    public int Count => Partners.Count;

    public bool HasJdp => Count > 0;

    public string CardTitle => Count switch
    {
        0 => "No JDP linked",
        1 => Partners[0].Name,
        _ => $"{Count} JDPs linked"
    };

    public string CardSummary => Count switch
    {
        0 => "Link an industry partner",
        1 => Partners[0].UsageSummary,
        _ => BuildPartnerNameSummary(Partners)
    };

    public static ProjectMultiJdpProfileDto Empty(int projectId) =>
        new(projectId, Array.Empty<ProjectJdpPartnerProfileDto>());

    internal static string BuildUsageSummary(int total, int ongoing, int completed)
    {
        if (total == 0)
        {
            return "Not linked to any other project";
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

        var other = Math.Max(0, total - ongoing - completed);
        if (other > 0)
        {
            parts.Add($"{other} other");
        }

        return $"Also linked to {total} other {(total == 1 ? "project" : "projects")}" +
               (parts.Count == 0 ? string.Empty : $" · {string.Join(" · ", parts)}");
    }

    private static string BuildPartnerNameSummary(IReadOnlyList<ProjectJdpPartnerProfileDto> partners)
    {
        const int visibleNames = 2;
        var visible = string.Join(" · ", partners.Take(visibleNames).Select(partner => partner.Name));
        var remaining = partners.Count - visibleNames;
        return remaining > 0 ? $"{visible} · +{remaining} more" : visible;
    }
}
