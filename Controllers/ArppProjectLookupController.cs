using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Controllers;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewArpp)]
[ApiController]
[Route("api/arpp/projects")]
public sealed class ArppProjectLookupController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ArppProjectLookupController(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? q,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        var query = q?.Trim();
        var limit = Math.Clamp(take, 1, 50);

        var projects = _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query}%";
            projects = projects.Where(project =>
                EF.Functions.ILike(project.Name, pattern) ||
                (project.CaseFileNumber != null && EF.Functions.ILike(project.CaseFileNumber, pattern)));
        }

        var items = await projects
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Take(limit)
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
                            : "Ongoing"
            })
            .ToListAsync(cancellationToken);

        return Ok(new { items });
    }
}
