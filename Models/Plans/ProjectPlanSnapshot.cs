using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Plans;

public class ProjectPlanSnapshot
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateTimeOffset TakenAt { get; set; }

    [MaxLength(450)]
    public string TakenByUserId { get; set; } = string.Empty;
    public ApplicationUser? TakenByUser { get; set; }

    public ICollection<ProjectPlanSnapshotRow> Rows { get; set; } = new List<ProjectPlanSnapshotRow>();
}

public class ProjectPlanSnapshotRow
{
    public int Id { get; set; }

    public int SnapshotId { get; set; }
    public ProjectPlanSnapshot? Snapshot { get; set; }

    [MaxLength(32)]
    public string StageCode { get; set; } = string.Empty;

    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedDue { get; set; }
}
