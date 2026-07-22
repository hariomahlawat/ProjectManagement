using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;

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
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? q,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        var trimmedQuery = q?.Trim();
        var normalizedTake = Math.Clamp(take, 1, 50);

        // Historical association is a first-class use case. Archived and completed
        // projects are searchable; only soft-deleted projects are excluded.
        var projects = _dbContext.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted);

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            var pattern = $"%{trimmedQuery}%";
            projects = projects.Where(project =>
                EF.Functions.ILike(project.Name, pattern) ||
                (project.CaseFileNumber != null && EF.Functions.ILike(project.CaseFileNumber, pattern)));
        }

        var items = await projects
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Take(normalizedTake)
            .Select(project => new
            {
                id = project.Id,
                name = project.Name,
                caseFileNumber = project.CaseFileNumber,
                statusLabel = project.IsArchived
                    ? "Archived"
                    : project.LifecycleStatus == ProjectLifecycleStatus.Completed
                        ? "Completed"
                        : project.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                            ? "Cancelled"
                            : "Ongoing",
                isArchived = project.IsArchived
            })
            .ToListAsync(cancellationToken);

        return Ok(new { items });
    }
}
