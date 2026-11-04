using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
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
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? q,
        [FromQuery] int take = 25,
        CancellationToken cancellationToken = default)
    {
        var trimmedQuery = q?.Trim();
        var normalizedTake = Math.Clamp(take, 1, 50);

        // Historical association is a first-class use case. Archived and completed
        // projects are searchable; only soft-deleted projects are excluded. The picker
        // links projects as JDP, so it lists only projects whose JDPs the user may change.
        var projects = ProjectJdpAccess.WhereUserCanManage(
            _dbContext.Projects
                .AsNoTracking()
                .Where(project => !project.IsDeleted),
            User);

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            // "%" and "_" typed by the user are matched literally, not as wildcards.
            var pattern = $"%{EscapeLikePattern(trimmedQuery)}%";
            projects = projects.Where(project =>
                EF.Functions.ILike(project.Name, pattern, LikeEscapeCharacter) ||
                (project.CaseFileNumber != null && EF.Functions.ILike(project.CaseFileNumber, pattern, LikeEscapeCharacter)));
        }

        var rows = await projects
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Take(normalizedTake)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.LifecycleStatus,
                project.IsArchived
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(project => new
        {
            id = project.Id,
            name = project.Name,
            caseFileNumber = project.CaseFileNumber,
            statusLabel = JdpStatusRules.Label(
                JdpStatusRules.ForProject(project.LifecycleStatus, project.IsArchived, isDeleted: false)),
            isArchived = project.IsArchived
        });

        return Ok(new { items });
    }

    private const string LikeEscapeCharacter = "\\";

    public static string EscapeLikePattern(string value) =>
        value
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);
}
