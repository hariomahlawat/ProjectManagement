using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Scheduling;

public class ForecastBackfillService
{
    private readonly ApplicationDbContext _db;

    public ForecastBackfillService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<int> BackfillAsync(CancellationToken ct = default)
    {
        return _db.ProjectStages
            .Where(stage => stage.PlannedStart != null
                && stage.PlannedDue != null
                && stage.ForecastStart == null
                && stage.ForecastDue == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(stage => stage.ForecastStart, stage => stage.PlannedStart)
                .SetProperty(stage => stage.ForecastDue, stage => stage.PlannedDue), ct);
    }
}
