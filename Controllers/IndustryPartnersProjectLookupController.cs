using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;

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
        var items = await projectsQuery
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Take(normalizedTake)
            .Select(project => new
            {
                id = project.Id,
                name = string.IsNullOrWhiteSpace(project.CaseFileNumber)
                    ? $"{project.Name} (ID: {project.Id})"
                    : $"{project.Name} | {project.CaseFileNumber}"
            })
            .ToListAsync(cancellationToken);

        return Ok(new { items });
    }
}
