using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Documents;

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
