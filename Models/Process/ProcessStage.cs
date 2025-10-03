using System.Collections.Generic;

namespace ProjectManagement.Models.Process;

public class ProcessStage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Row { get; set; }
    public int? Col { get; set; }
    public bool IsOptional { get; set; }

    public ICollection<ProcessChecklistItem> ChecklistItems { get; set; } = new List<ProcessChecklistItem>();
    public ICollection<ProcessStageEdge> OutgoingEdges { get; set; } = new List<ProcessStageEdge>();
    public ICollection<ProcessStageEdge> IncomingEdges { get; set; } = new List<ProcessStageEdge>();
}
