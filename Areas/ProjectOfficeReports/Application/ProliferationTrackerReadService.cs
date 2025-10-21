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

        var granularSum = await _db.Set<ProliferationGranular>()
            .Where(x =>
                x.ProjectId == projectId &&
                x.Source == source &&
                x.ApprovalStatus == ApprovalStatus.Approved &&
                x.ProliferationDate.Year == year)
            .Select(x => (int?)x.Quantity)
            .SumAsync(cancellationToken) ?? 0;

        if (source == ProliferationSource.Abw515)
        {
            return yearly;
        }

        var preference = await _db.Set<ProliferationYearPreference>()
            .Where(x => x.ProjectId == projectId && x.Source == source && x.Year == year)
            .Select(x => (YearPreferenceMode?)x.Mode)
            .FirstOrDefaultAsync(cancellationToken) ?? YearPreferenceMode.UseYearlyAndGranular;

        return preference switch
        {
            YearPreferenceMode.UseYearly => yearly,
            YearPreferenceMode.UseGranular => granularSum,
            YearPreferenceMode.Auto => granularSum > 0 ? granularSum : yearly,
            YearPreferenceMode.UseYearlyAndGranular => yearly + granularSum,
            _ => yearly + granularSum
        };
    }
}
