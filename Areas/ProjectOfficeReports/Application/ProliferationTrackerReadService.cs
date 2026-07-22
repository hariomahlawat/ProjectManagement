using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationTrackerReadService
{
    private readonly ProliferationAggregateReadService _aggregateReadService;

    public ProliferationTrackerReadService(ProliferationAggregateReadService aggregateReadService)
    {
        _aggregateReadService = aggregateReadService ?? throw new ArgumentNullException(nameof(aggregateReadService));
    }

    public async Task<int> GetEffectiveTotalAsync(
        int projectId,
        ProliferationSource source,
        int year,
        CancellationToken cancellationToken)
    {
        var aggregate = await _aggregateReadService.GetApprovedAggregateAsync(
            projectId,
            source,
            year,
            cancellationToken);

        return aggregate?.ReportedTotal ?? 0;
    }
}
