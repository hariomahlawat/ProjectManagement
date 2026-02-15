using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.Compendiums.Application;

// SECTION: Preference-aware proliferation metrics contract
public interface IProliferationMetricsService
{
    Task<int> GetAllTimeTotalAsync(int projectId, ProliferationSource source, CancellationToken cancellationToken);
}
