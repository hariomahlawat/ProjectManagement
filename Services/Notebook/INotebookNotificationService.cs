using ProjectManagement.Models;

namespace ProjectManagement.Services.Notebook;

/// <summary>
/// Queues durable Notebook collaboration notifications into the current application DbContext.
/// The caller remains responsible for the final SaveChanges so collaboration and notification
/// dispatch are committed atomically.
/// </summary>
public interface INotebookNotificationService
{
    Task QueueSharedAsync(
        NotebookItem item,
        NotebookItemCollaborator collaboration,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task QueueRoleChangedAsync(
        NotebookItem item,
        NotebookItemCollaborator collaboration,
        NotebookCollaborationRole previousRole,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task QueueAccessRemovedAsync(
        NotebookItem item,
        NotebookItemCollaborator collaboration,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task QueueCollaborationLeftAsync(
        NotebookItem item,
        NotebookItemCollaborator collaboration,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
