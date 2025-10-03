using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using ProjectManagement.Data;
using ProjectManagement.Models.Process;

namespace ProjectManagement.Pages.Process;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<StageVm> Stages { get; private set; } = Array.Empty<StageVm>();
    public IReadOnlyList<EdgeVm> Edges { get; private set; } = Array.Empty<EdgeVm>();
    public bool CanEditChecklist { get; private set; }

    public async Task OnGetAsync()
    {
        var stages = await _db.ProcessStages
            .AsNoTracking()
            .OrderBy(s => s.Row)
            .ThenBy(s => s.Col)
            .ThenBy(s => s.Id)
            .ToListAsync();
        var edges = await _db.ProcessStageEdges
            .AsNoTracking()
            .ToListAsync();

        Stages = stages
            .Select(s => new StageVm(s.Id, s.Name, s.Row ?? 0, s.Col ?? 0, s.IsOptional))
            .ToArray();
        Edges = edges
            .Select(e => new EdgeVm(e.FromStageId, e.ToStageId))
            .ToArray();

        CanEditChecklist = User.IsInRole("MCO") || User.IsInRole("HoD") || User.IsInRole("Admin");
    }

    public sealed record StageVm(int Id, string Name, int Row, int Col, bool IsOptional);
    public sealed record EdgeVm(int FromId, int ToId);
}
