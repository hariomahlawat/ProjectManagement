using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.Compendiums.Application;

// SECTION: Preference-aware proliferation metrics implementation
public sealed class ProliferationMetricsService : IProliferationMetricsService
{
    private readonly ApplicationDbContext _db;

    public ProliferationMetricsService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<int> GetAllTimeTotalAsync(int projectId, ProliferationSource source, CancellationToken cancellationToken)
    {
        // SECTION: Approved yearly totals per year
        var yearlyRows = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Source == source && x.ApprovalStatus == ApprovalStatus.Approved)
            .Select(x => new { x.Year, Yearly = x.TotalQuantity })
            .ToListAsync(cancellationToken);

        // SECTION: Approved granular totals per year
        var granularRows = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Source == source && x.ApprovalStatus == ApprovalStatus.Approved)
            .GroupBy(x => x.ProliferationDate.Year)
            .Select(x => new { Year = x.Key, Granular = x.Sum(y => y.Quantity) })
            .ToListAsync(cancellationToken);

        // SECTION: 515 ABW uses yearly figures only
        if (source == ProliferationSource.Abw515)
        {
            return yearlyRows.Sum(x => x.Yearly);
        }

        // SECTION: Preference-aware year level consolidation
        var yearlyLookup = yearlyRows.ToDictionary(x => x.Year, x => x.Yearly);
        var granularLookup = granularRows.ToDictionary(x => x.Year, x => x.Granular);
        var years = yearlyLookup.Keys.Union(granularLookup.Keys).Distinct().ToArray();

        if (years.Length == 0)
        {
            return 0;
        }

        var preferences = await _db.ProliferationYearPreferences
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Source == source && years.Contains(x.Year))
            .ToDictionaryAsync(x => x.Year, x => x.Mode, cancellationToken);

        var total = 0;
        foreach (var year in years)
        {
            var yearly = yearlyLookup.TryGetValue(year, out var y) ? y : 0;
            var granular = granularLookup.TryGetValue(year, out var g) ? g : 0;
            var mode = preferences.TryGetValue(year, out var preferenceMode)
                ? preferenceMode
                : YearPreferenceMode.UseYearlyAndGranular;

            total += mode switch
            {
                YearPreferenceMode.UseYearly => yearly,
                YearPreferenceMode.UseGranular => granular,
                YearPreferenceMode.Auto => granular > 0 ? granular : yearly,
                YearPreferenceMode.UseYearlyAndGranular => yearly + granular,
                _ => yearly + granular
            };
        }

        return total;
    }
}
