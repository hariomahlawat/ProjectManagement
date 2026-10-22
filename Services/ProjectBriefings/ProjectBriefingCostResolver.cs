using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;

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

        // Resolve the authoritative IPA position once for the separate Total IPA Cost summary.
        var ipaPositions = await _ipaResolver.ResolveManyAsync(ids, cancellationToken);

        // Cost (R&D) is the project's cost under the one shared rule (ProjectCostResolver).
        var projectCosts = await new ProjectCostResolver(_db, _ipaResolver)
            .ResolveCostInCrAsync(ids, cancellationToken);

        var costRd = new Dictionary<int, ProjectBriefingCostValue>(ids.Length);
        var ipaCost = new Dictionary<int, ProjectBriefingCostValue>(ids.Length);

        foreach (var projectId in ids)
        {
            var authoritativeIpaAmount = ipaPositions.TryGetValue(projectId, out var position)
                ? position.AmountInRupees
                : 0m;
            ipaCost[projectId] = authoritativeIpaAmount > 0m
                ? Build(authoritativeIpaAmount, ProjectBriefingCostBasis.IPA, "IPA")
                : ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA);

            var resolved = projectCosts.GetValueOrDefault(projectId);
            costRd[projectId] = resolved?.AmountInRupees is decimal amount && BasisFor(resolved.Source) is { } basis
                ? Build(amount, basis, resolved.SourceLabel)
                : ProjectBriefingCostValue.Missing();
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
                && row.ApproxProductionCost is >= 0m)
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

    private static ProjectBriefingCostBasis? BasisFor(ProjectCostSource source) => source switch
    {
        ProjectCostSource.Pnc => ProjectBriefingCostBasis.PNC,
        ProjectCostSource.L1 => ProjectBriefingCostBasis.L1,
        ProjectCostSource.CostLakhs => ProjectBriefingCostBasis.Recorded,
        ProjectCostSource.Aon => ProjectBriefingCostBasis.AoN,
        ProjectCostSource.Ipa => ProjectBriefingCostBasis.IPA,
        _ => null
    };

    private static ProjectBriefingCostValue Build(
        decimal amountInRupees,
        ProjectBriefingCostBasis basis,
        string basisDisplay)
        => new(
            amountInRupees,
            basis,
            ProjectBriefingCurrencyFormatter.FormatRupees(amountInRupees),
            basisDisplay);
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
