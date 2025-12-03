using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.ViewModels;

public sealed class StagePlanEditInput
{
    [Required]
    public int ProjectId { get; set; }

    [Required]
    public string StageCode { get; set; } = string.Empty;

    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedDue { get; set; }

    public string? Action { get; set; }
}
