using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProliferationProjectReadService
{
    Task<ProliferationProjectAggregationResult> GetAggregatesAsync(
        ProliferationProjectAggregationRequest request,
        CancellationToken cancellationToken);
}
