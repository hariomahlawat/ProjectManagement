using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Services.ProjectBriefings;

public sealed record ProjectBriefingResolvedCosts(
    IReadOnlyDictionary<int, ProjectBriefingCostValue> CostRd,
    IReadOnlyDictionary<int, ProjectBriefingCostValue> Ipa);

public interface IProjectBriefingCostResolver
{
    Task<ProjectBriefingResolvedCosts> ResolveCostsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);

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
    private readonly IAuthoritativeIpaPositionResolver _ipaResolver;

    public ProjectBriefingCostResolver(ApplicationDbContext db)
        : this(db, new AuthoritativeIpaPositionResolver(db))
    {
    }

    public ProjectBriefingCostResolver(
        ApplicationDbContext db,
        IAuthoritativeIpaPositionResolver ipaResolver)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ipaResolver = ipaResolver ?? throw new ArgumentNullException(nameof(ipaResolver));
    }

    public async Task<ProjectBriefingResolvedCosts> ResolveCostsAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        var ids = Normalize(projectIds);
        if (ids.Length == 0)
        {
            var empty = new Dictionary<int, ProjectBriefingCostValue>();
            return new ProjectBriefingResolvedCosts(empty, empty);
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

        // Resolve the authoritative IPA position once. The same snapshot is used both
        // as the R&D-cost fallback and for the separate Total IPA Cost summary.
        var ipaPositions = await _ipaResolver.ResolveManyAsync(ids, cancellationToken);

        var l1 = Latest(l1Rows);
        var aon = Latest(aonRows);
        var ipa = ipaPositions
            .Where(pair => pair.Value.AmountInRupees > 0m)
            .ToDictionary(pair => pair.Key, pair => pair.Value.AmountInRupees);

        var costRd = new Dictionary<int, ProjectBriefingCostValue>(ids.Length);
        var ipaCost = new Dictionary<int, ProjectBriefingCostValue>(ids.Length);

        foreach (var projectId in ids)
        {
            var authoritativeIpaAmount = ipa.GetValueOrDefault(projectId);
            ipaCost[projectId] = authoritativeIpaAmount > 0m
                ? Build(authoritativeIpaAmount, ProjectBriefingCostBasis.IPA, "IPA")
                : ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA);

            if (l1.TryGetValue(projectId, out var l1Amount))
            {
                costRd[projectId] = Build(l1Amount, ProjectBriefingCostBasis.L1, "L1");
            }
            else if (aon.TryGetValue(projectId, out var aonAmount))
            {
                costRd[projectId] = Build(aonAmount, ProjectBriefingCostBasis.AoN, "AoN");
            }
            else if (ipa.TryGetValue(projectId, out var ipaAmount))
            {
                costRd[projectId] = Build(ipaAmount, ProjectBriefingCostBasis.IPA, "IPA");
            }
            else
            {
                costRd[projectId] = ProjectBriefingCostValue.Missing();
            }
        }

        return new ProjectBriefingResolvedCosts(costRd, ipaCost);
    }

    public async Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveCostRdAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
        => (await ResolveCostsAsync(projectIds, cancellationToken)).CostRd;

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
        => FormatRupees(amount, minimumDecimalPlaces: 0);

    public static string FormatRupees(decimal amount, int minimumDecimalPlaces)
    {
        if (minimumDecimalPlaces is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumDecimalPlaces),
                minimumDecimalPlaces,
                "Currency precision must be between zero and two decimal places.");
        }

        var numberFormat = minimumDecimalPlaces switch
        {
            0 => "0.##",
            1 => "0.0#",
            _ => "0.00"
        };

        if (amount >= Crore)
        {
            return $"₹{(amount / Crore).ToString(numberFormat, System.Globalization.CultureInfo.InvariantCulture)} Cr";
        }

        if (amount >= Lakh)
        {
            return $"₹{(amount / Lakh).ToString(numberFormat, System.Globalization.CultureInfo.InvariantCulture)} Lakh";
        }

        return $"₹{amount:N0}";
    }
}
