using System;

namespace ProjectManagement.Models;

public class ProjectMetaChangeRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;
    public string? ProposedName { get; set; }
    public string? ProposedDescription { get; set; }
    public string? ProposedCaseFileNumber { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTime RequestedOnUtc { get; set; }
    public string DecisionStatus { get; set; } = "Pending";
    public string? DecisionNote { get; set; }
    public DateTime? DecidedOnUtc { get; set; }
    public string? DecidedByUserId { get; set; }
}
