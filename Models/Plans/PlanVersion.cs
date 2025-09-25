using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Plans;

public class PlanVersion
{
    public const string ProjectTimelineTitle = "Project Timeline";

    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public int VersionNo { get; set; }

    [MaxLength(64)]
    public string Title { get; set; } = ProjectTimelineTitle;

    public PlanVersionStatus Status { get; set; } = PlanVersionStatus.Draft;

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(450)]
    public string? SubmittedByUserId { get; set; }
    public ApplicationUser? SubmittedByUser { get; set; }

    public DateTimeOffset? SubmittedOn { get; set; }

    [MaxLength(450)]
    public string? ApprovedByUserId { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }

    public DateTimeOffset? ApprovedOn { get; set; }

    [MaxLength(512)]
    public string? Reason { get; set; }

    [MaxLength(16)]
    public string? AnchorStageCode { get; set; }

    public DateOnly? AnchorDate { get; set; }

    public bool SkipWeekends { get; set; } = true;

    public PlanTransitionRule TransitionRule { get; set; } = PlanTransitionRule.NextWorkingDay;

    public bool PncApplicable { get; set; } = true;

    public List<StagePlan> StagePlans { get; set; } = new();

    public List<PlanApprovalLog> ApprovalLogs { get; set; } = new();
}

public enum PlanVersionStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3
}

public enum PlanTransitionRule
{
    SameDay = 0,
    NextWorkingDay = 1
}
