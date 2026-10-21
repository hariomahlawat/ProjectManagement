namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Identifies the Project Officer workspace surface being requested. The value is
/// normalised once at the page boundary so query composition and rendering use the
/// same, closed set of views.
/// </summary>
public enum ProjectOfficerWorkspaceView
{
    Overview,
    Actions,
    Projects,
    Tasks,
    Ideas,
    Conference,
    FollowUps,
    Documents,
    Activity
}

public static class ProjectOfficerWorkspaceViewParser
{
    public static ProjectOfficerWorkspaceView Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "actions" or "action-queue" or "queue" => ProjectOfficerWorkspaceView.Actions,
        "projects" or "assigned-projects" => ProjectOfficerWorkspaceView.Projects,
        "tasks" or "assigned-tasks" => ProjectOfficerWorkspaceView.Tasks,
        "ideas" or "my-ideas" => ProjectOfficerWorkspaceView.Ideas,
        "conference" or "conference-review" or "my-conference" => ProjectOfficerWorkspaceView.Conference,
        "follow-ups" or "followups" or "reminders" => ProjectOfficerWorkspaceView.FollowUps,
        "documents" or "my-documents" => ProjectOfficerWorkspaceView.Documents,
        "activity" or "erp-activity" or "my-erp-activity" => ProjectOfficerWorkspaceView.Activity,
        _ => ProjectOfficerWorkspaceView.Overview
    };

    /// <summary>
    /// Views folded into the briefing desk. Their addresses keep working: the page redirects
    /// to the desk, at the matching section where there is one.
    /// </summary>
    public static bool IsFoldedIntoDesk(this ProjectOfficerWorkspaceView view, out string? deskSection)
    {
        deskSection = view switch
        {
            ProjectOfficerWorkspaceView.Tasks => "bd-section-task",
            ProjectOfficerWorkspaceView.Ideas => "bd-section-idea",
            ProjectOfficerWorkspaceView.FollowUps => "bd-week-title",
            _ => null
        };

        return view is ProjectOfficerWorkspaceView.Actions
            or ProjectOfficerWorkspaceView.Tasks
            or ProjectOfficerWorkspaceView.Ideas
            or ProjectOfficerWorkspaceView.FollowUps;
    }

    public static string ToRouteValue(this ProjectOfficerWorkspaceView view) => view switch
    {
        ProjectOfficerWorkspaceView.Actions => "actions",
        ProjectOfficerWorkspaceView.Projects => "projects",
        ProjectOfficerWorkspaceView.Tasks => "tasks",
        ProjectOfficerWorkspaceView.Ideas => "ideas",
        ProjectOfficerWorkspaceView.Conference => "conference",
        ProjectOfficerWorkspaceView.FollowUps => "follow-ups",
        ProjectOfficerWorkspaceView.Documents => "documents",
        ProjectOfficerWorkspaceView.Activity => "activity",
        _ => "overview"
    };
}
