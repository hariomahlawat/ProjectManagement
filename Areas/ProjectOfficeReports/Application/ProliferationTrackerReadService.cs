using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationTrackerReadService
{
    private readonly ApplicationDbContext _db;

    public ProliferationTrackerReadService(ApplicationDbContext db) => _db = db;

    public async Task<int> GetEffectiveTotalAsync(
        int projectId,
        ProliferationSource source,
        int year,
        CancellationToken cancellationToken)
    {
        var yearly = await _db.Set<ProliferationYearly>()
            .Where(x => x.ProjectId == projectId && x.Source == source && x.Year == year && x.ApprovalStatus == ApprovalStatus.Approved)
            .Select(x => (int?)x.TotalQuantity)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var granularSum = await _db.Set<VwProliferationGranularYearly>()
            .Where(x => x.ProjectId == projectId && x.Source == source && x.Year == year)
            .Select(x => x.TotalQuantity)
            .FirstOrDefaultAsync(cancellationToken);

        if (source == ProliferationSource.Abw515)
        {
            return yearly;
        }

        var preference = await _db.Set<ProliferationYearPreference>()
            .Where(x => x.ProjectId == projectId && x.Source == source && x.Year == year)
            .Select(x => (YearPreferenceMode?)x.Mode)
            .FirstOrDefaultAsync(cancellationToken) ?? YearPreferenceMode.Auto;

        return preference switch
        {
            YearPreferenceMode.UseYearly => yearly,
            YearPreferenceMode.UseGranular => granularSum,
            _ => granularSum > 0 ? granularSum : yearly
        };
    }
}
