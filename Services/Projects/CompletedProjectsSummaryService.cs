using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;

namespace ProjectManagement.Services.Projects;

// SECTION: Completed projects summary service
public sealed class CompletedProjectsSummaryService
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db;

    public CompletedProjectsSummaryService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // SECTION: Query methods
    public async Task<IReadOnlyList<CompletedProjectSummaryDto>> GetAsync(
        string? techStatus,
        bool? availableForProliferation,
        int? completedYear,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _db.Projects
            .AsNoTracking()
            .Where(p => p.LifecycleStatus == ProjectLifecycleStatus.Completed);

        if (completedYear.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.CompletedYear == completedYear);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            baseQuery = baseQuery.Where(p => EF.Functions.ILike(p.Name, term));
        }

        var projectionQuery =
            from project in baseQuery
            join productionCost in _db.ProjectProductionCostFacts.AsNoTracking()
                on project.Id equals productionCost.ProjectId into productionCostJoin
            from productionCost in productionCostJoin.DefaultIfEmpty()
            join tech in _db.ProjectTechStatuses.AsNoTracking()
                on project.Id equals tech.ProjectId into techJoin
            from tech in techJoin.DefaultIfEmpty()
            join lpp in _db.ProjectLppRecords.AsNoTracking()
                on project.Id equals lpp.ProjectId into lppJoin
            let latestLpp = lppJoin
                .OrderByDescending(x => x.LppDate ?? DateOnly.MinValue)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault()
            select new CompletedProjectSummaryDto
            {
                ProjectId = project.Id,
                Name = project.Name,
                RdCostLakhs = project.CostLakhs,
                ApproxProductionCost = productionCost != null ? productionCost.ApproxProductionCost : null,
                TechStatus = tech != null ? tech.TechStatus : null,
                AvailableForProliferation = tech != null ? tech.AvailableForProliferation : (bool?)null,
                Remarks = tech != null && tech.Remarks != null
                    ? tech.Remarks
                    : productionCost != null
                        ? productionCost.Remarks
                        : null,
                CompletedYear = project.CompletedYear,
                LatestLpp = latestLpp == null
                    ? null
                    : new LatestLppViewModel
                    {
                        Amount = latestLpp.LppAmount,
                        Date = latestLpp.LppDate
                    }
            };

        if (!string.IsNullOrWhiteSpace(techStatus))
        {
            projectionQuery = projectionQuery.Where(x => x.TechStatus == techStatus);
        }

        if (availableForProliferation.HasValue)
        {
            projectionQuery = projectionQuery.Where(x => x.AvailableForProliferation == availableForProliferation);
        }

        var items = await projectionQuery
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return items;
    }
}

// SECTION: DTOs
public sealed class CompletedProjectSummaryDto
{
    // SECTION: Identity
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;

    // SECTION: Cost metadata
    public decimal? RdCostLakhs { get; set; }
    public decimal? ApproxProductionCost { get; set; }

    // SECTION: Technology metadata
    public string? TechStatus { get; set; }
    public bool? AvailableForProliferation { get; set; }
    public string? Remarks { get; set; }

    // SECTION: Completion metadata
    public int? CompletedYear { get; set; }

    // SECTION: LPP metadata
    public LatestLppViewModel? LatestLpp { get; set; }
}

public sealed class LatestLppViewModel
{
    public decimal Amount { get; set; }
    public DateOnly? Date { get; set; }
}
