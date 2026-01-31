using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Projects;

// SECTION: Cost resolution contracts
public enum ProjectCostSource
{
    None = 0,
    CostLakhs = 1,
    Pnc = 2,
    L1 = 3,
    Aon = 4,
    Ipa = 5
}

public sealed record ProjectCostResolution(decimal? CostInCr, ProjectCostSource Source);

public interface IProjectCostResolver
{
    Task<Dictionary<int, ProjectCostResolution>> ResolveCostInCrAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken ct = default);
}

// SECTION: Cost resolution implementation
public sealed class ProjectCostResolver : IProjectCostResolver
{
    private const decimal LakhsToCroresDivisor = 100m;
    private const decimal RupeesToCroresDivisor = 10000000m;

    private readonly ApplicationDbContext _db;

    public ProjectCostResolver(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<Dictionary<int, ProjectCostResolution>> ResolveCostInCrAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken ct = default)
    {
        if (projectIds is null || projectIds.Count == 0)
        {
            return new Dictionary<int, ProjectCostResolution>();
        }

        // SECTION: Project CostLakhs lookup (Lakhs)
        var costLakhsRows = await _db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .Select(project => new { project.Id, project.CostLakhs })
            .ToListAsync(ct);

        var costLakhsByProject = costLakhsRows
            .ToDictionary(row => row.Id, row => row.CostLakhs);

        // SECTION: Fact cost lookups (INR)
        var pncFacts = await _db.ProjectPncFacts
            .AsNoTracking()
            .Where(fact => projectIds.Contains(fact.ProjectId))
            .Select(fact => new { fact.ProjectId, fact.PncCost, fact.CreatedOnUtc, fact.Id })
            .ToListAsync(ct);

        var l1Facts = await _db.ProjectCommercialFacts
            .AsNoTracking()
            .Where(fact => projectIds.Contains(fact.ProjectId))
            .Select(fact => new { fact.ProjectId, fact.L1Cost, fact.CreatedOnUtc, fact.Id })
            .ToListAsync(ct);

        var aonFacts = await _db.ProjectAonFacts
            .AsNoTracking()
            .Where(fact => projectIds.Contains(fact.ProjectId))
            .Select(fact => new { fact.ProjectId, fact.AonCost, fact.CreatedOnUtc, fact.Id })
            .ToListAsync(ct);

        var ipaFacts = await _db.ProjectIpaFacts
            .AsNoTracking()
            .Where(fact => projectIds.Contains(fact.ProjectId))
            .Select(fact => new { fact.ProjectId, fact.IpaCost, fact.CreatedOnUtc, fact.Id })
            .ToListAsync(ct);

        var pncByProject = BuildLatestFactMap(pncFacts, fact => fact.ProjectId, fact => fact.CreatedOnUtc, fact => fact.Id, fact => fact.PncCost);
        var l1ByProject = BuildLatestFactMap(l1Facts, fact => fact.ProjectId, fact => fact.CreatedOnUtc, fact => fact.Id, fact => fact.L1Cost);
        var aonByProject = BuildLatestFactMap(aonFacts, fact => fact.ProjectId, fact => fact.CreatedOnUtc, fact => fact.Id, fact => fact.AonCost);
        var ipaByProject = BuildLatestFactMap(ipaFacts, fact => fact.ProjectId, fact => fact.CreatedOnUtc, fact => fact.Id, fact => fact.IpaCost);

        // SECTION: Cost selection policy (CostLakhs -> PNC -> L1 -> AON -> IPA)
        var result = new Dictionary<int, ProjectCostResolution>(projectIds.Count);

        foreach (var projectId in projectIds)
        {
            if (TryGetMeaningfulCost(costLakhsByProject, projectId, out var costLakhs))
            {
                result[projectId] = new ProjectCostResolution(costLakhs / LakhsToCroresDivisor, ProjectCostSource.CostLakhs);
                continue;
            }

            if (TryGetMeaningfulCost(pncByProject, projectId, out var pnc))
            {
                result[projectId] = new ProjectCostResolution(pnc / RupeesToCroresDivisor, ProjectCostSource.Pnc);
                continue;
            }

            if (TryGetMeaningfulCost(l1ByProject, projectId, out var l1))
            {
                result[projectId] = new ProjectCostResolution(l1 / RupeesToCroresDivisor, ProjectCostSource.L1);
                continue;
            }

            if (TryGetMeaningfulCost(aonByProject, projectId, out var aon))
            {
                result[projectId] = new ProjectCostResolution(aon / RupeesToCroresDivisor, ProjectCostSource.Aon);
                continue;
            }

            if (TryGetMeaningfulCost(ipaByProject, projectId, out var ipa))
            {
                result[projectId] = new ProjectCostResolution(ipa / RupeesToCroresDivisor, ProjectCostSource.Ipa);
                continue;
            }

            result[projectId] = new ProjectCostResolution(null, ProjectCostSource.None);
        }

        return result;
    }

    // SECTION: Fact selection helpers
    private static Dictionary<int, decimal?> BuildLatestFactMap<TFact>(
        IEnumerable<TFact> facts,
        Func<TFact, int> projectIdSelector,
        Func<TFact, DateTime> createdSelector,
        Func<TFact, int> idSelector,
        Func<TFact, decimal?> costSelector)
    {
        return facts
            .GroupBy(projectIdSelector)
            .ToDictionary(
                group => group.Key,
                group => costSelector(group
                    .OrderByDescending(createdSelector)
                    .ThenByDescending(idSelector)
                    .First()));
    }

    private static bool TryGetMeaningfulCost(IReadOnlyDictionary<int, decimal?> map, int projectId, out decimal cost)
    {
        if (map.TryGetValue(projectId, out var value) && value.HasValue && value.Value > 0)
        {
            cost = value.Value;
            return true;
        }

        cost = 0m;
        return false;
    }
}
