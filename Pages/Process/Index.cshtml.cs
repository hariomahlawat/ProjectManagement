using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Features.Process;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Pages.Process;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<StageTemplate> Stages { get; private set; } = new();
    public List<StageDependencyTemplate> Deps { get; private set; } = new();
    public IReadOnlyList<ProcessStageVm> FlowStages { get; private set; } = Array.Empty<ProcessStageVm>();

    public async Task OnGetAsync()
    {
        const string version = "SDD-1.0";
        Stages = await _db.StageTemplates
            .Where(x => x.Version == version)
            .OrderBy(x => x.Sequence)
            .ToListAsync();
        Deps = await _db.StageDependencyTemplates
            .Where(x => x.Version == version)
            .ToListAsync();

        var dependencyLookup = Deps
            .GroupBy(d => d.FromStageCode)
            .ToDictionary(g => g.Key, g => g.Select(d => d.DependsOnStageCode).ToList());

        FlowStages = Stages
            .Select(stage =>
            {
                var dependsOn = dependencyLookup.TryGetValue(stage.Code, out var dep)
                    ? dep
                    : new List<string>();

                var checklist = StageChecklistCatalog.GetChecklist(stage.Code);

                return new ProcessStageVm(
                    stage.Code,
                    stage.Name,
                    stage.Sequence,
                    stage.Optional,
                    stage.ParallelGroup,
                    dependsOn,
                    checklist);
            })
            .ToList();
    }

    public record ProcessStageVm(
        string Code,
        string Name,
        int Sequence,
        bool Optional,
        string? ParallelGroup,
        IReadOnlyList<string> DependsOn,
        IReadOnlyList<string> ChecklistItems);
}
