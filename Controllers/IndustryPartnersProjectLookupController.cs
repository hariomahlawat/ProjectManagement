using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.IndustryPartners;

namespace ProjectManagement.Controllers;

[Authorize(Policy = Policies.IndustryPartners.View)]
[ApiController]
[Route("api/industry-partners/projects")]
public sealed class IndustryPartnersProjectLookupController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public IndustryPartnersProjectLookupController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] string? q, [FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        // SECTION: Input normalization
        var trimmedQuery = q?.Trim();
        var normalizedTake = Math.Clamp(take, 1, 50);
        var candidateTake = Math.Clamp(normalizedTake * 5, normalizedTake, 250);

        // SECTION: Base project filters
        var projectsQuery = _dbContext.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted && !project.IsArchived);

        // SECTION: Query-based search filters
        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            var pattern = $"%{trimmedQuery}%";
            projectsQuery = projectsQuery.Where(project =>
                EF.Functions.ILike(project.Name, pattern) ||
                (!string.IsNullOrWhiteSpace(project.CaseFileNumber) && EF.Functions.ILike(project.CaseFileNumber!, pattern)));
        }

        // SECTION: Lightweight response projection
        // NOTE: Eligibility currently depends on `ProcurementWorkflow.OrderOf(...)`, which is not SQL-translatable.
        // To preserve expected `take` semantics, we page through sorted candidates until we collect enough
        // eligible projects (or exhaust all matches), instead of truncating before eligibility checks.
        var eligibleProjects = new List<Project>(normalizedTake);
        var offset = 0;

        while (eligibleProjects.Count < normalizedTake)
        {
            var candidateProjects = await projectsQuery
                .Include(project => project.ProjectStages)
                .OrderBy(project => project.Name)
                .ThenBy(project => project.Id)
                .Skip(offset)
                .Take(candidateTake)
                .ToListAsync(cancellationToken);

            if (candidateProjects.Count == 0)
            {
                break;
            }

            foreach (var project in candidateProjects)
            {
                if (!IndustryPartnerProjectEligibility.IsEligibleForJdpLink(project, project.ProjectStages))
                {
                    continue;
                }

                eligibleProjects.Add(project);
                if (eligibleProjects.Count == normalizedTake)
                {
                    break;
                }
            }

            if (candidateProjects.Count < candidateTake)
            {
                break;
            }

            offset += candidateProjects.Count;
        }

        var items = eligibleProjects
            .Select(project => new
            {
                id = project.Id,
                name = string.IsNullOrWhiteSpace(project.CaseFileNumber)
                    ? $"{project.Name} (ID: {project.Id})"
                    : $"{project.Name} | {project.CaseFileNumber}"
            })
            .ToList();

        return Ok(new { items });
    }
}
