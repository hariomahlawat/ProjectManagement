using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Ffc;

public interface IFfcFootprintService
{
    Task<FfcFootprintResult> GetAsync(
        FfcFootprintRequest request,
        CancellationToken cancellationToken = default);
}
