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
        var values = await GetManyAsync(new[] { projectId }, ct);
        return values.GetValueOrDefault(projectId) ?? ProcurementAtAGlanceVm.Empty;
    }

    // SECTION: Batch procurement snapshot
    // Uses the same latest-record rule as the Project Overview "Procurement at a glance" card,
    // while avoiding one query per project when the workspace evaluates a portfolio.
    public async Task<IReadOnlyDictionary<int, ProcurementAtAGlanceVm>> GetManyAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, ProcurementAtAGlanceVm>();
        }

        var ids = projectIds.Distinct().ToArray();

        var ipaRows = await _db.ProjectIpaFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.IpaCost, f.CreatedOnUtc })
            .ToListAsync(ct);

        var aonRows = await _db.ProjectAonFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.AonCost, f.CreatedOnUtc })
            .ToListAsync(ct);

        var benchmarkRows = await _db.ProjectBenchmarkFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.BenchmarkCost, f.CreatedOnUtc })
            .ToListAsync(ct);

        var l1Rows = await _db.ProjectCommercialFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.L1Cost, f.CreatedOnUtc })
            .ToListAsync(ct);

        var pncRows = await _db.ProjectPncFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.PncCost, f.CreatedOnUtc })
            .ToListAsync(ct);

        var supplyOrderRows = await _db.ProjectSupplyOrderFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.SupplyOrderDate, f.CreatedOnUtc })
            .ToListAsync(ct);

        var latestIpa = ipaRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(g => g.Key, g => (decimal?)g.OrderByDescending(f => f.CreatedOnUtc).First().IpaCost);
        var latestAon = aonRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(g => g.Key, g => (decimal?)g.OrderByDescending(f => f.CreatedOnUtc).First().AonCost);
        var latestBenchmark = benchmarkRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(g => g.Key, g => (decimal?)g.OrderByDescending(f => f.CreatedOnUtc).First().BenchmarkCost);
        var latestL1 = l1Rows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(g => g.Key, g => (decimal?)g.OrderByDescending(f => f.CreatedOnUtc).First().L1Cost);
        var latestPnc = pncRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(g => g.Key, g => (decimal?)g.OrderByDescending(f => f.CreatedOnUtc).First().PncCost);
        var latestSupplyOrder = supplyOrderRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(g => g.Key, g => (DateOnly?)g.OrderByDescending(f => f.CreatedOnUtc).First().SupplyOrderDate);

        return ids.ToDictionary(
            projectId => projectId,
            projectId => new ProcurementAtAGlanceVm(
                latestIpa.GetValueOrDefault(projectId),
                latestAon.GetValueOrDefault(projectId),
                latestBenchmark.GetValueOrDefault(projectId),
                latestL1.GetValueOrDefault(projectId),
                latestPnc.GetValueOrDefault(projectId),
                latestSupplyOrder.GetValueOrDefault(projectId)));
    }
}

public sealed record ProcurementAtAGlanceVm(
    decimal? IpaCost,
    decimal? AonCost,
    decimal? BenchmarkCost,
    decimal? L1Cost,
    decimal? PncCost,
    DateOnly? SupplyOrderDate)
{
    public static ProcurementAtAGlanceVm Empty { get; } = new(null, null, null, null, null, null);
}
