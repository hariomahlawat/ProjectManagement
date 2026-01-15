using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Documents;

public interface IDocumentDecisionService
{
    // SECTION: Approval decisions
    Task<ProjectDocumentRequest> ApproveAsync(
        int requestId,
        string decidedByUserId,
        bool isAdmin,
        bool isHoD,
        string? note,
        CancellationToken cancellationToken);

    // SECTION: Rejection decisions
    Task<ProjectDocumentRequest> RejectAsync(
        int requestId,
        string decidedByUserId,
        bool isAdmin,
        bool isHoD,
        string? note,
        CancellationToken cancellationToken);
}
