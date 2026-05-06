using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public class ActionSprint
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Goal { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public ActionSprintStatus Status { get; set; } = ActionSprintStatus.Planned;

    [Required, StringLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string CreatedByRole { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    [StringLength(450)]
    public string? UpdatedByUserId { get; set; }

    [StringLength(64)]
    public string? UpdatedByRole { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    // SECTION: Optimistic concurrency token for sprint updates
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // SECTION: Sprint relationships
    public ICollection<ActionTaskItem> Tasks { get; set; } = new List<ActionTaskItem>();
    public ICollection<ActionSprintAuditLog> AuditLogs { get; set; } = new List<ActionSprintAuditLog>();
}
