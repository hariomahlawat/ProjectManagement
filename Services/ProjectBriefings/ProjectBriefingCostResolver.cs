using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.ProjectBriefings;

public interface IProjectBriefingCostResolver
{
    Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveCostRdAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveProliferationCostAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectBriefingCostResolver : IProjectBriefingCostResolver
{
    private const decimal RupeesPerLakh = 100_000m;
    private readonly ApplicationDbContext _db;

    public ProjectBriefingCostResolver(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveCostRdAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        var ids = Normalize(projectIds);
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectBriefingCostValue>();
        }

        var l1Rows = await _db.ProjectCommercialFacts
            .AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId) && row.L1Cost > 0m)
            .Select(row => new CostFactRow(row.ProjectId, row.L1Cost, row.CreatedOnUtc, row.Id))
            .ToListAsync(cancellationToken);

        var aonRows = await _db.ProjectAonFacts
            .AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId) && row.AonCost > 0m)
            .Select(row => new CostFactRow(row.ProjectId, row.AonCost, row.CreatedOnUtc, row.Id))
            .ToListAsync(cancellationToken);

        var ipaRows = await _db.ProjectIpaFacts
            .AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId) && row.IpaCost > 0m)
            .Select(row => new CostFactRow(row.ProjectId, row.IpaCost, row.CreatedOnUtc, row.Id))
            .ToListAsync(cancellationToken);

        var l1 = Latest(l1Rows);
        var aon = Latest(aonRows);
        var ipa = Latest(ipaRows);
        var result = new Dictionary<int, ProjectBriefingCostValue>(ids.Length);

        foreach (var projectId in ids)
        {
            if (l1.TryGetValue(projectId, out var l1Amount))
            {
                result[projectId] = Build(l1Amount, ProjectBriefingCostBasis.L1, "L1");
            }
            else if (aon.TryGetValue(projectId, out var aonAmount))
            {
                result[projectId] = Build(aonAmount, ProjectBriefingCostBasis.AoN, "AoN");
            }
            else if (ipa.TryGetValue(projectId, out var ipaAmount))
            {
                result[projectId] = Build(ipaAmount, ProjectBriefingCostBasis.IPA, "IPA");
            }
            else
            {
                result[projectId] = ProjectBriefingCostValue.Missing();
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveProliferationCostAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        var ids = Normalize(projectIds);
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectBriefingCostValue>();
        }

        var rows = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(row => ids.Contains(row.ProjectId))
            .Select(row => new
            {
                row.ProjectId,
                row.ApproxProductionCost,
                row.Remarks
            })
            .ToListAsync(cancellationToken);

        var byProject = rows.ToDictionary(row => row.ProjectId);
        var result = new Dictionary<int, ProjectBriefingCostValue>(ids.Length);

        foreach (var projectId in ids)
        {
            if (byProject.TryGetValue(projectId, out var row)
                && row.ApproxProductionCost is > 0m)
            {
                var amountInRupees = row.ApproxProductionCost.Value * RupeesPerLakh;
                result[projectId] = Build(
                    amountInRupees,
                    ProjectBriefingCostBasis.Proliferation,
                    "Proliferation");
            }
            else
            {
                result[projectId] = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation);
            }
        }

        return result;
    }

    private static int[] Normalize(IReadOnlyCollection<int> projectIds)
        => projectIds?
            .Where(id => id > 0)
            .Distinct()
            .ToArray() ?? Array.Empty<int>();

    private static Dictionary<int, decimal> Latest(IEnumerable<CostFactRow> rows)
        => rows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.CreatedAtUtc)
                    .ThenByDescending(row => row.Id)
                    .First()
                    .Amount);

    private static ProjectBriefingCostValue Build(
        decimal amountInRupees,
        ProjectBriefingCostBasis basis,
        string basisDisplay)
        => new(
            amountInRupees,
            basis,
            ProjectBriefingCurrencyFormatter.FormatRupees(amountInRupees),
            basisDisplay);

    private sealed record CostFactRow(
        int ProjectId,
        decimal Amount,
        DateTime CreatedAtUtc,
        int Id);
}

public static class ProjectBriefingCurrencyFormatter
{
    private const decimal Crore = 10_000_000m;
    private const decimal Lakh = 100_000m;

    public static string FormatRupees(decimal amount)
    {
        if (amount >= Crore)
        {
            return $"₹{amount / Crore:0.##} Cr";
        }

        if (amount >= Lakh)
        {
            return $"₹{amount / Lakh:0.##} Lakh";
        }

        return $"₹{amount:N0}";
    }
}
