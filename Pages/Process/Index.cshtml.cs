using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Pages.Process;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public ProcessFlowVm Flow { get; private set; } = new();

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

        Flow = new ProcessFlowVm
        {
            CanEdit = User.IsInRole("MCO") || User.IsInRole("HoD") || User.IsInRole("Admin"),
            Nodes = stages.Select(stage => new FlowNode
            {
                Id = stage.Id.ToString(CultureInfo.InvariantCulture),
                Label = stage.Name,
                IsOptional = stage.IsOptional
            }).ToList(),
            Edges = edges.Select((edge, index) => new FlowEdge
            {
                Id = $"e{index}",
                Source = edge.FromStageId.ToString(CultureInfo.InvariantCulture),
                Target = edge.ToStageId.ToString(CultureInfo.InvariantCulture)
            }).ToList()
        };
    }

    public sealed class ProcessFlowVm
    {
        public IList<FlowNode> Nodes { get; set; } = new List<FlowNode>();
        public IList<FlowEdge> Edges { get; set; } = new List<FlowEdge>();
        public bool CanEdit { get; set; }
    }

    public sealed class FlowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = "process";
        public bool IsOptional { get; set; }
    }

    public sealed class FlowEdge
    {
        public string Id { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }
}
