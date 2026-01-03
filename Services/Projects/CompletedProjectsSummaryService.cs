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

public sealed class CompletedProjectsSummaryService
{
    private readonly ApplicationDbContext _db;

    public CompletedProjectsSummaryService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<CompletedProjectSummaryDto>> GetAsync(
        int? technicalCategoryId,
        string? techStatus,
        bool? availableForProliferation,
        bool? totCompleted,
        int? completedYear,
        string? search,
        CancellationToken cancellationToken = default)
    {
        // SECTION: Base project selection
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p =>
                p.LifecycleStatus == ProjectLifecycleStatus.Completed
                && !p.IsDeleted
                && !p.IsArchived)
            .ToListAsync(cancellationToken);

        if (technicalCategoryId.HasValue)
        {
            projects = projects
                .Where(p => p.TechnicalCategoryId == technicalCategoryId.Value)
                .ToList();
        }

        var projectIds = projects.Select(p => p.Id).ToList();

        // SECTION: Related data lookups
        var costFacts = await _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(c => projectIds.Contains(c.ProjectId))
            .ToListAsync(cancellationToken);

        var techStatuses = await _db.ProjectTechStatuses
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId))
            .ToListAsync(cancellationToken);

        var lppRecords = await _db.ProjectLppRecords
            .AsNoTracking()
            .Where(l => projectIds.Contains(l.ProjectId))
            .ToListAsync(cancellationToken);

        var totStatusLookup = await _db.ProjectTots
            .AsNoTracking()
            .Where(t => projectIds.Contains(t.ProjectId))
            .Select(t => new { t.ProjectId, t.Status })
            .ToListAsync(cancellationToken);

        var totStatusByProjectId = totStatusLookup
            .GroupBy(t => t.ProjectId)
            .ToDictionary(group => group.Key, group => group.First().Status);

        // SECTION: DTO mapping
        var result = new List<CompletedProjectSummaryDto>(projects.Count);

        foreach (var p in projects)
        {
            var cost = costFacts.FirstOrDefault(x => x.ProjectId == p.Id);
            var tech = techStatuses.FirstOrDefault(x => x.ProjectId == p.Id);
            ProjectTotStatus? totStatus = null;
            if (totStatusByProjectId.TryGetValue(p.Id, out var foundStatus))
            {
                totStatus = foundStatus;
            }

            var latestLpp = lppRecords
                .Where(l => l.ProjectId == p.Id)
                .OrderByDescending(l => l.LppDate)
                .ThenByDescending(l => l.CreatedAtUtc)
                .FirstOrDefault();

            var dto = new CompletedProjectSummaryDto
            {
                ProjectId = p.Id,
                Name = p.Name,
                RdCostLakhs = p.CostLakhs,
                ApproxProductionCost = cost?.ApproxProductionCost,
                TechStatus = tech?.TechStatus,
                AvailableForProliferation = tech?.AvailableForProliferation,
                Remarks = tech?.Remarks ?? cost?.Remarks,
                CompletedYear = p.CompletedYear,
                TotStatus = totStatus,
                LatestLpp = latestLpp != null
                    ? new LatestLppViewModel
                    {
                        Amount = latestLpp.LppAmount,
                        Date = latestLpp.LppDate
                    }
                    : null
            };

            result.Add(dto);
        }

        IEnumerable<CompletedProjectSummaryDto> filtered = result;

        // SECTION: Filters
        if (!string.IsNullOrWhiteSpace(techStatus))
        {
            filtered = filtered.Where(r =>
                string.Equals(r.TechStatus, techStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (availableForProliferation.HasValue)
        {
            filtered = filtered.Where(r =>
                r.AvailableForProliferation == availableForProliferation);
        }

        if (totCompleted.HasValue)
        {
            filtered = filtered.Where(r => totCompleted.Value
                ? r.TotStatus == ProjectTotStatus.Completed
                : r.TotStatus != ProjectTotStatus.Completed);
        }

        if (completedYear.HasValue)
        {
            filtered = filtered.Where(r => r.CompletedYear == completedYear);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderBy(r => r.Name)
            .ToList();
    }
}

public sealed class CompletedProjectSummaryDto
{
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? RdCostLakhs { get; set; }
    public decimal? ApproxProductionCost { get; set; }
    public string? TechStatus { get; set; }
    public bool? AvailableForProliferation { get; set; }
    public string? Remarks { get; set; }
    public int? CompletedYear { get; set; }
    public ProjectTotStatus? TotStatus { get; set; }
    public LatestLppViewModel? LatestLpp { get; set; }
}

public sealed class LatestLppViewModel
{
    public decimal Amount { get; set; }
    public DateOnly? Date { get; set; }
}
