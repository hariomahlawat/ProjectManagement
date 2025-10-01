using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Documents;

public interface IDocumentDecisionService
{
    Task<ProjectDocumentRequest> ApproveAsync(
        int requestId,
        string decidedByUserId,
        string? note,
        CancellationToken cancellationToken);

    Task<ProjectDocumentRequest> RejectAsync(
        int requestId,
        string decidedByUserId,
        string? note,
        CancellationToken cancellationToken);
}
