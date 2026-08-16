using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Projects;

public enum ProjectSupplyOrderValueBasis
{
    None = 0,
    Pnc = 1,
    L1 = 2
}

public sealed record ProjectSupplyOrderValue(
    decimal? AmountInRupees,
    ProjectSupplyOrderValueBasis Basis)
{
    public bool IsAvailable => AmountInRupees is > 0m;

    public static ProjectSupplyOrderValue Missing { get; } =
        new(null, ProjectSupplyOrderValueBasis.None);
}

public interface IProjectSupplyOrderValueResolver
{
    Task<IReadOnlyDictionary<int, ProjectSupplyOrderValue>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the formal Supply Order amount using the SDD reporting rule:
/// latest positive PNC cost, otherwise latest positive L1 cost. AoN and IPA are
/// deliberately not valid fallbacks because they must never be represented as
/// an issued Supply Order value.
/// </summary>
public sealed class ProjectSupplyOrderValueResolver : IProjectSupplyOrderValueResolver
{
    private readonly ApplicationDbContext _db;

    public ProjectSupplyOrderValueResolver(ApplicationDbContext db)
        => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyDictionary<int, ProjectSupplyOrderValue>> ResolveAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectSupplyOrderValue>();
        }

        var pncRows = await _db.ProjectPncFacts
            .AsNoTracking()
            .Where(fact => ids.Contains(fact.ProjectId) && fact.PncCost > 0m)
            .Select(fact => new CostRow(fact.ProjectId, fact.PncCost, fact.CreatedOnUtc, fact.Id))
            .ToListAsync(cancellationToken);

        var l1Rows = await _db.ProjectCommercialFacts
            .AsNoTracking()
            .Where(fact => ids.Contains(fact.ProjectId) && fact.L1Cost > 0m)
            .Select(fact => new CostRow(fact.ProjectId, fact.L1Cost, fact.CreatedOnUtc, fact.Id))
            .ToListAsync(cancellationToken);

        var pnc = Latest(pncRows);
        var l1 = Latest(l1Rows);
        var result = new Dictionary<int, ProjectSupplyOrderValue>(ids.Length);

        foreach (var projectId in ids)
        {
            if (pnc.TryGetValue(projectId, out var pncAmount))
            {
                result[projectId] = new ProjectSupplyOrderValue(pncAmount, ProjectSupplyOrderValueBasis.Pnc);
            }
            else if (l1.TryGetValue(projectId, out var l1Amount))
            {
                result[projectId] = new ProjectSupplyOrderValue(l1Amount, ProjectSupplyOrderValueBasis.L1);
            }
            else
            {
                result[projectId] = ProjectSupplyOrderValue.Missing;
            }
        }

        return result;
    }

    private static Dictionary<int, decimal> Latest(IEnumerable<CostRow> rows)
        => rows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.CreatedOnUtc)
                    .ThenByDescending(row => row.Id)
                    .First()
                    .AmountInRupees);

    private sealed record CostRow(
        int ProjectId,
        decimal AmountInRupees,
        DateTime CreatedOnUtc,
        int Id);
}
