using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;

namespace ProjectManagement.Pages.Projects;

[Authorize]
public class StagesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public StagesModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public record StageRow(
        string Code,
        string Name,
        DateOnly? PlannedStart,
        DateOnly? PlannedDue,
        StageStatus Status,
        DateOnly? ActualStart,
        DateOnly? CompletedOn);

    public int ProjectId { get; private set; }
    public string ProjectName { get; private set; } = string.Empty;
    public List<StageRow> Stages { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var cancellationToken = HttpContext.RequestAborted;

        var project = await _db.Projects
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        ProjectId = project.Id;
        ProjectName = project.Name;

        var templates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Code, t.Name })
            .ToListAsync(cancellationToken);

        var projectStages = await _db.ProjectStages
            .AsNoTracking()
            .Where(ps => ps.ProjectId == id)
            .ToListAsync(cancellationToken);

        var stageLookup = projectStages
            .ToDictionary(ps => ps.StageCode, ps => ps, StringComparer.OrdinalIgnoreCase);

        Stages = templates
            .Select(template =>
            {
                stageLookup.TryGetValue(template.Code, out var projectStage);

                var status = projectStage?.Status ?? StageStatus.NotStarted;

                return new StageRow(
                    template.Code,
                    template.Name,
                    projectStage?.PlannedStart,
                    projectStage?.PlannedDue,
                    status,
                    projectStage?.ActualStart,
                    projectStage?.CompletedOn);
            })
            .ToList();

        return Page();
    }
}
