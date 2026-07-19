using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Ffc.Presentation;

public interface IFfcPowerPointExportService
{
    Task<FfcPowerPointExportResult> GenerateAsync(
        FfcPowerPointExportRequest request,
        CancellationToken cancellationToken = default);
}
