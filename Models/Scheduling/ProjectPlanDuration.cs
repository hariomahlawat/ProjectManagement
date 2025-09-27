namespace ProjectManagement.Models.Scheduling;

public class ProjectPlanDuration
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public int? DurationDays { get; set; }
    public int SortOrder { get; set; }
}
