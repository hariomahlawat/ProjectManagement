using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Documents;

public interface IDocumentNotificationService
{
    Task NotifyDocumentPublishedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task NotifyDocumentReplacedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task NotifyDocumentArchivedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task NotifyDocumentRestoredAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task NotifyDocumentDeletedAsync(
        ProjectDocument document,
        Project project,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
