using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Plans;

public class PlanVersion
{
    public const string BaselineTitle = "Baseline";

    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int VersionNo { get; set; }

    [MaxLength(64)]
    public string Title { get; set; } = BaselineTitle;

    public PlanVersionStatus Status { get; set; } = PlanVersionStatus.Draft;

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public List<StagePlan> StagePlans { get; set; } = new();
}

public enum PlanVersionStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3
}
