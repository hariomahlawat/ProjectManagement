namespace ProjectManagement.Models.Stages;

public class StageTemplate
{
    public int Id { get; set; }
    public string Version { get; set; } = "SDD-1.0";
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool Optional { get; set; }
    public string? ParallelGroup { get; set; }
}
