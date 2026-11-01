namespace ProjectManagement.Infrastructure;

/// <summary>
/// Browser tab naming for project pages. Links that open a project page in another tab use
/// <c>target="@ProjectTab.Name(id)"</c> instead of <c>_blank</c>, so each project gets one tab:
/// clicking any link to the same project again lands in the tab already open.
/// Script-built links use the same <c>prism-project-{id}</c> format.
/// </summary>
public static class ProjectTab
{
    public static string Name(int projectId) => $"prism-project-{projectId}";

    /// <summary>Falls back to an ordinary new tab when the project is not known.</summary>
    public static string Name(int? projectId) => projectId is { } id ? Name(id) : "_blank";
}
