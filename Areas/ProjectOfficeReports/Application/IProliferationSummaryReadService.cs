using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public interface IProliferationSummaryReadService
{
    Task<ProliferationSummaryViewModel> GetSummaryAsync(CancellationToken cancellationToken);

    Task<ProliferationOperationalSnapshot> GetOperationalSnapshotAsync(
        int recentProliferationLimit,
        int recentActivityLimit,
        CancellationToken cancellationToken);
}
