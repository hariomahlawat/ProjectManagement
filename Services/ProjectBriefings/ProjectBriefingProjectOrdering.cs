namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Single ordering contract for the briefing builder, executive tables and project
/// detail slides. Stage maturity is authoritative; the saved deck order applies only
/// within the same present-stage group.
/// </summary>
public static class ProjectBriefingProjectOrdering
{
    public static IReadOnlyList<ProjectBriefingProjectVm> OrderProjects(
        IEnumerable<ProjectBriefingProjectVm> projects)
        => OrderCore(
            projects,
            project => project.PresentStageOrder,
            project => project.SortOrder,
            project => project.ProjectName,
            project => project.ProjectId);

    public static IReadOnlyList<ProjectBriefingPresentationProject> OrderProjects(
        IEnumerable<ProjectBriefingPresentationProject> projects)
        => OrderCore(
            projects,
            project => project.PresentStageOrder,
            project => project.SortOrder,
            project => project.ProjectName,
            project => project.ProjectId);

    private static IReadOnlyList<T> OrderCore<T>(
        IEnumerable<T> projects,
        Func<T, int> stageOrder,
        Func<T, int> manualOrder,
        Func<T, string> projectName,
        Func<T, int> projectId)
    {
        ArgumentNullException.ThrowIfNull(projects);

        return projects
            .OrderBy(stageOrder)
            .ThenBy(manualOrder)
            .ThenBy(projectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(projectId)
            .ToArray();
    }
}
