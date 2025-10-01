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
        DocumentFileDescriptor file,
        string requestedByUserId,
        CancellationToken cancellationToken);

    Task<ProjectDocumentRequest> CreateReplaceRequestAsync(
        int documentId,
        DocumentFileDescriptor file,
        string requestedByUserId,
        CancellationToken cancellationToken);

    Task<ProjectDocumentRequest> CreateDeleteRequestAsync(
        int documentId,
        string requestedByUserId,
        CancellationToken cancellationToken);
}
