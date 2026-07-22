using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Ffc.Presentation;

public interface IFfcPresentationDataService
{
    Task<FfcPresentationData> GetAsync(
        FfcPowerPointExportRequest request,
        CancellationToken cancellationToken = default);
}
