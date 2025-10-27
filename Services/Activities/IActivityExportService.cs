using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Activities;

public interface IActivityExportService
{
    Task<ActivityExportResult> ExportByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default);
}

public sealed record ActivityExportResult(string FileName, string ContentType, byte[] Content);
