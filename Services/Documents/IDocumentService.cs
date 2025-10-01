using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Documents;

public interface IDocumentService
{
    int CreateTempRequestToken();

    Task<DocumentFileDescriptor> SaveTempAsync(
        int requestId,
        Stream content,
        string originalFileName,
        string? contentType,
        CancellationToken cancellationToken);

    Task<ProjectDocument> PublishNewAsync(
        int projectId,
        int? stageId,
        string nomenclature,
        string tempStorageKey,
        string originalFileName,
        long fileSize,
        string contentType,
        string uploadedByUserId,
        string performedByUserId,
        CancellationToken cancellationToken);

    Task<ProjectDocument> OverwriteAsync(
        int documentId,
        string tempStorageKey,
        string originalFileName,
        long fileSize,
        string contentType,
        string uploadedByUserId,
        string performedByUserId,
        CancellationToken cancellationToken);

    Task<ProjectDocument> SoftDeleteAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken);

    Task<ProjectDocument> RestoreAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken);

    Task HardDeleteAsync(
        int documentId,
        string performedByUserId,
        CancellationToken cancellationToken);

    Task<DocumentStreamResult?> OpenStreamAsync(
        int documentId,
        CancellationToken cancellationToken);

    Task DeleteTempAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
