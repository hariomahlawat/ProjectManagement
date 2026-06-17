using System;

namespace ProjectManagement.Services.Workspace;

internal static class WorkspaceRouteHelper
{
    // SECTION: Project routes
    public static string ProjectOverview(int projectId)
        => $"/Projects/Overview/{projectId}";

    public static string ProjectTimeline(int projectId)
        => $"/Projects/Overview/{projectId}#timeline";

    public static string ProjectMedia(int projectId)
        => $"/Projects/Overview/{projectId}#media";

    public static string ProjectRemarks(int projectId)
        => $"/Projects/Remarks/{projectId}";

    public static string ProjectMetaRequest(int projectId)
        => $"/Projects/Meta/Request/{projectId}";

    public static string ProjectDocumentRequest(int projectId)
        => $"/Projects/Documents/UploadRequest?id={projectId}";

    public static string MyProjects(string userId)
        => $"/Projects/Ongoing?ProjectOfficerId={Uri.EscapeDataString(userId)}";

    // SECTION: Workspace cross-module routes
    public static string ActionTask(int taskId)
        => $"/ActionTasks?viewMode=MyWork&taskId={taskId}";

    public static string ActionTasksMyWork()
        => "/ActionTasks?viewMode=MyWork";

    public static string ProjectIdea(int ideaId)
        => $"/ProjectIdeas/Details/{ideaId}";

    public static string ProjectIdeasMine()
        => "/ProjectIdeas?MyIdeas=true";

    public static string PersonalReminders()
        => "/Tasks";

    public static string AotsInbox()
        => "/DocumentRepository/Documents?scope=aots";

    public static string AotsReader(Guid documentId)
        => $"/DocumentRepository/Documents/Reader/{documentId}?returnUrl={Uri.EscapeDataString(AotsInbox())}";
}
