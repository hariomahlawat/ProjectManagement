namespace ProjectManagement.Models.Process;

public class ProcessStageEdge
{
    public int FromStageId { get; set; }
    public int ToStageId { get; set; }

    public ProcessStage? FromStage { get; set; }
    public ProcessStage? ToStage { get; set; }
}
