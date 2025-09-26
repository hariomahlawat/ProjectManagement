using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectProcurementReadService
{
    private readonly ApplicationDbContext _db;

    public ProjectProcurementReadService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ProcurementAtAGlanceVm> GetAsync(int projectId, CancellationToken ct = default)
    {
        decimal? latestIpa = await _db.ProjectIpaFacts
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedOnUtc)
            .Select(f => (decimal?)f.IpaCost)
            .FirstOrDefaultAsync(ct);

        decimal? latestAon = await _db.ProjectAonFacts
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedOnUtc)
            .Select(f => (decimal?)f.AonCost)
            .FirstOrDefaultAsync(ct);

        decimal? latestBenchmark = await _db.ProjectBenchmarkFacts
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedOnUtc)
            .Select(f => (decimal?)f.BenchmarkCost)
            .FirstOrDefaultAsync(ct);

        decimal? latestL1 = await _db.ProjectCommercialFacts
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedOnUtc)
            .Select(f => (decimal?)f.L1Cost)
            .FirstOrDefaultAsync(ct);

        decimal? latestPnc = await _db.ProjectPncFacts
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedOnUtc)
            .Select(f => (decimal?)f.PncCost)
            .FirstOrDefaultAsync(ct);

        DateOnly? latestSo = await _db.ProjectSupplyOrderFacts
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedOnUtc)
            .Select(f => (DateOnly?)f.SupplyOrderDate)
            .FirstOrDefaultAsync(ct);

        return new ProcurementAtAGlanceVm(
            latestIpa,
            latestAon,
            latestBenchmark,
            latestL1,
            latestPnc,
            latestSo);
    }
}

public sealed record ProcurementAtAGlanceVm(
    decimal? IpaCost,
    decimal? AonCost,
    decimal? BenchmarkCost,
    decimal? L1Cost,
    decimal? PncCost,
    DateOnly? SupplyOrderDate);
