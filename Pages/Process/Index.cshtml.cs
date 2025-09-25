using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
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
    }
}
