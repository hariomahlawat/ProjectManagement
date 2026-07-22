using System;

namespace ProjectManagement.Services.Workspace;

internal static class WorkspaceRouteHelper
{
    // SECTION: Project routes
    public static string ProjectOverview(int projectId)
        => $"/Projects/Overview/{projectId}";

    public static string ProjectTimeline(int projectId)
        => $"/Projects/Overview/{projectId}#timeline";

    public static string ProjectMedia(int projectId, string mediaTab)
    {
        var normalized = mediaTab?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "photos" => ProjectPhotos(projectId),
            "videos" => ProjectVideos(projectId),
            _ => ProjectDocuments(projectId)
        };
    }

    public static string ProjectPhotos(int projectId)
        => $"/Projects/{projectId}/Photos";

    public static string ProjectVideos(int projectId)
        => $"/Projects/{projectId}/Videos";

    public static string ProjectDocuments(int projectId)
        => $"/Projects/{projectId}/Documents";

    // Backward-compatible name used by existing workspace consumers.
    public static string ProjectDocumentsTab(int projectId)
        => ProjectDocuments(projectId);

    public static string ProjectRemarks(int projectId)
        => $"/Projects/Overview/{projectId}#remarks";

    public static string ProjectMetaRequest(int projectId)
        => $"/Projects/Meta/Request/{projectId}";

    public static string ProjectMetaEdit(int projectId)
        => $"/Projects/Meta/Edit/{projectId}";

    public static string ProjectProcurementEdit(int projectId)
        => $"/Projects/Overview/{projectId}?oc=procurement#procurement";

    public static string ProjectTimelineActuals(int projectId)
        => $"/Projects/Overview/{projectId}?oc=actuals#timeline";

    public static string ProjectDocumentRequest(int projectId)
        => $"/Projects/Documents/UploadRequest?id={projectId}";

    public static string MyProjects(string userId)
        => $"/Projects/Ongoing?ProjectOfficerId={Uri.EscapeDataString(userId)}";

    public static string ProjectOfficerWorkspace(string view = "overview")
        => $"/Workspace?mode=project-officer&view={Uri.EscapeDataString(string.IsNullOrWhiteSpace(view) ? "overview" : view)}";

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
        => "/Notebook?view=today";

    public static string PersonalReminder(Guid itemId)
        => $"/Notebook?view=today&note={itemId}";

    public static string AotsInbox()
        => "/DocumentRepository/Documents?scope=aots";

    public static string AotsReader(Guid documentId)
        => $"/DocumentRepository/Documents/Reader/{documentId}?returnUrl={Uri.EscapeDataString(AotsInbox())}";

    public static string DocumentRepository()
        => "/DocumentRepository/Documents";

    public static string DocumentFavourites()
        => "/DocumentRepository/Documents?scope=favourites";

    public static string DocumentReader(Guid documentId, string? returnUrl = null)
    {
        var reader = $"/DocumentRepository/Documents/Reader/{documentId}";
        return string.IsNullOrWhiteSpace(returnUrl)
            ? reader
            : $"{reader}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
