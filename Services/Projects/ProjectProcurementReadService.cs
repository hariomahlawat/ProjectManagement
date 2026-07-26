using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectProcurementReadService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthoritativeIpaPositionResolver _ipaResolver;

    public ProjectProcurementReadService(ApplicationDbContext db)
        : this(db, new AuthoritativeIpaPositionResolver(db))
    {
    }

    public ProjectProcurementReadService(
        ApplicationDbContext db,
        IAuthoritativeIpaPositionResolver ipaResolver)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ipaResolver = ipaResolver ?? throw new ArgumentNullException(nameof(ipaResolver));
    }

    public async Task<ProcurementAtAGlanceVm> GetAsync(int projectId, CancellationToken ct = default)
    {
        var values = await GetManyAsync(new[] { projectId }, ct);
        return values.GetValueOrDefault(projectId) ?? ProcurementAtAGlanceVm.Empty;
    }

    // SECTION: Batch procurement snapshot
    // IPA is resolved through the shared ARPP-first authority boundary. All other
    // procurement facts retain their existing latest-record behaviour.
    public async Task<IReadOnlyDictionary<int, ProcurementAtAGlanceVm>> GetManyAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        if (projectIds.Count == 0)
        {
            return new Dictionary<int, ProcurementAtAGlanceVm>();
        }

        var ids = projectIds
            .Where(projectId => projectId > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<int, ProcurementAtAGlanceVm>();
        }

        var ipaPositions = await _ipaResolver.ResolveManyAsync(ids, ct);

        var aonRows = await _db.ProjectAonFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.AonCost, f.CreatedOnUtc, f.Id })
            .ToListAsync(ct);

        var benchmarkRows = await _db.ProjectBenchmarkFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.BenchmarkCost, f.CreatedOnUtc, f.Id })
            .ToListAsync(ct);

        var l1Rows = await _db.ProjectCommercialFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.L1Cost, f.CreatedOnUtc, f.Id })
            .ToListAsync(ct);

        var pncRows = await _db.ProjectPncFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.PncCost, f.CreatedOnUtc, f.Id })
            .ToListAsync(ct);

        var supplyOrderRows = await _db.ProjectSupplyOrderFacts
            .AsNoTracking()
            .Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.SupplyOrderDate, f.CreatedOnUtc, f.Id })
            .ToListAsync(ct);

        var latestAon = aonRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (decimal?)group
                    .OrderByDescending(f => f.CreatedOnUtc)
                    .ThenByDescending(f => f.Id)
                    .First()
                    .AonCost);

        var latestBenchmark = benchmarkRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (decimal?)group
                    .OrderByDescending(f => f.CreatedOnUtc)
                    .ThenByDescending(f => f.Id)
                    .First()
                    .BenchmarkCost);

        var latestL1 = l1Rows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (decimal?)group
                    .OrderByDescending(f => f.CreatedOnUtc)
                    .ThenByDescending(f => f.Id)
                    .First()
                    .L1Cost);

        var latestPnc = pncRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (decimal?)group
                    .OrderByDescending(f => f.CreatedOnUtc)
                    .ThenByDescending(f => f.Id)
                    .First()
                    .PncCost);

        var latestSupplyOrder = supplyOrderRows
            .GroupBy(f => f.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => (DateOnly?)group
                    .OrderByDescending(f => f.CreatedOnUtc)
                    .ThenByDescending(f => f.Id)
                    .First()
                    .SupplyOrderDate);

        return ids.ToDictionary(
            projectId => projectId,
            projectId =>
            {
                ipaPositions.TryGetValue(projectId, out var ipaPosition);

                return new ProcurementAtAGlanceVm(
                    ipaPosition?.AmountInRupees,
                    latestAon.GetValueOrDefault(projectId),
                    latestBenchmark.GetValueOrDefault(projectId),
                    latestL1.GetValueOrDefault(projectId),
                    latestPnc.GetValueOrDefault(projectId),
                    latestSupplyOrder.GetValueOrDefault(projectId))
                {
                    IpaPosition = ipaPosition
                };
            });
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
    public AuthoritativeIpaPosition? IpaPosition { get; init; }

    public bool IsIpaManagedByArpp => IpaPosition?.IsManagedByArpp == true;

    public static ProcurementAtAGlanceVm Empty { get; } =
        new(null, null, null, null, null, null);
}
