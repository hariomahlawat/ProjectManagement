using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Documents;

public sealed record DocumentUploadRequestItem(string Title, DocumentFileDescriptor File);

public interface IDocumentRequestService
{
    Task<ProjectDocumentRequest> CreateUploadRequestAsync(
        int projectId,
        int? stageId,
        string nomenclature,
        int? totId,
        DocumentFileDescriptor file,
        string requestedByUserId,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<ProjectDocumentRequest>> CreateUploadRequestsAsync(
        int projectId,
        int? stageId,
        int? totId,
        IReadOnlyList<DocumentUploadRequestItem> items,
        string requestedByUserId,
        CancellationToken cancellationToken)
    {
        var requests = new List<ProjectDocumentRequest>(items.Count);
        foreach (var item in items)
        {
            requests.Add(await CreateUploadRequestAsync(
                projectId,
                stageId,
                item.Title,
                totId,
                item.File,
                requestedByUserId,
                cancellationToken));
        }

        return requests;
    }

    Task<ProjectDocumentRequest> CreateReplaceRequestAsync(
        int documentId,
        string? newTitle,
        DocumentFileDescriptor file,
        string requestedByUserId,
        CancellationToken cancellationToken);

    Task<ProjectDocumentRequest> CreateDeleteRequestAsync(
        int documentId,
        string? reason,
        string requestedByUserId,
        CancellationToken cancellationToken);
}
