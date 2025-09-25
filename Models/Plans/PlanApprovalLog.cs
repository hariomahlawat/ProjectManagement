using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Plans;

public class PlanApprovalLog
{
    public int Id { get; set; }

    public int PlanVersionId { get; set; }
    public PlanVersion? PlanVersion { get; set; }

    [MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Note { get; set; }

    [MaxLength(450)]
    public string PerformedByUserId { get; set; } = string.Empty;
    public ApplicationUser? PerformedByUser { get; set; }

    public DateTimeOffset PerformedOn { get; set; } = DateTimeOffset.UtcNow;
}
