using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public class ActionSprintAuditLog
{
    [Key]
    public int Id { get; set; }

    public int SprintId { get; set; }

    // SECTION: Audit action metadata
    [Required, StringLength(64)]
    public string ActionType { get; set; } = string.Empty;

    [Required, StringLength(450)]
    public string PerformedByUserId { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string PerformedByRole { get; set; } = string.Empty;

    public DateTime PerformedAt { get; set; }

    // SECTION: Audit value snapshot
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    [StringLength(2000)]
    public string? Remarks { get; set; }

    // SECTION: Sprint relationship
    public ActionSprint? Sprint { get; set; }
}
