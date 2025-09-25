namespace ProjectManagement.Models.Stages;

public class StageDependencyTemplate
{
    public int Id { get; set; }
    public string Version { get; set; } = "SDD-1.0";
    public string FromStageCode { get; set; } = string.Empty;
    public string DependsOnStageCode { get; set; } = string.Empty;
}
