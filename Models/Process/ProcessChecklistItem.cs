using System;

namespace ProjectManagement.Models.Process;

public class ProcessChecklistItem
{
    public int Id { get; set; }
    public int StageId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public string? UpdatedByUserId { get; set; }

    public ProcessStage? Stage { get; set; }
}
