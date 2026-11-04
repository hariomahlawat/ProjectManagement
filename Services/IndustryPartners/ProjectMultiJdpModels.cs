using ProjectManagement.Models;

namespace ProjectManagement.Services.IndustryPartners;

public sealed record ProjectJdpPartnerProfileDto(
    int Id,
    string Name,
    string? Location,
    IReadOnlyList<ProjectJdpLinkedProjectDto> OtherProjects)
{
    public int OtherProjectCount => OtherProjects.Count;

    public int OtherOngoingProjectCount => OtherProjects.Count(project => project.Status == JdpProjectStatus.Ongoing);

    public int OtherCompletedProjectCount => OtherProjects.Count(project => project.Status == JdpProjectStatus.Completed);

    public int OtherProjectStatusCount => Math.Max(
        0,
        OtherProjectCount - OtherOngoingProjectCount - OtherCompletedProjectCount);

    public string UsageSummary => JdpStatusRules.UsageSummary(
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

    private static string BuildPartnerNameSummary(IReadOnlyList<ProjectJdpPartnerProfileDto> partners)
    {
        const int visibleNames = 2;
        var visible = string.Join(" · ", partners.Take(visibleNames).Select(partner => partner.Name));
        var remaining = partners.Count - visibleNames;
        return remaining > 0 ? $"{visible} · +{remaining} more" : visible;
    }
}
