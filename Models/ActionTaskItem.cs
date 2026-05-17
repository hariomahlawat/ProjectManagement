using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public static class ActionTaskStatuses
{
    public const string Backlog = "Backlog";
    public const string Assigned = "Assigned";
    public const string InProgress = "In Progress";
    public const string Submitted = "Submitted";
    public const string Closed = "Closed";
    public const string Blocked = "Blocked";

    public static readonly string[] All =
    {
        Backlog,
        Assigned,
        InProgress,
        Submitted,
        Closed,
        Blocked
    };
}


public static class ActionTaskRegisterScopes
{
    public const string Open = "Open";
    public const string All = "All";

    public static string Normalize(string? scope)
        => string.Equals((scope ?? string.Empty).Trim(), All, StringComparison.OrdinalIgnoreCase) ? All : Open;

    public static bool IsOpenScope(string? scope)
        => string.Equals(Normalize(scope), Open, StringComparison.OrdinalIgnoreCase);
}

public class ActionTaskItem
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    [Required, StringLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    [Required, StringLength(450)]
    public string AssignedToUserId { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string CreatedByRole { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string AssignedToRole { get; set; } = string.Empty;

    public DateTime AssignedOn { get; set; }
    public DateTime DueDate { get; set; }

    [Required, StringLength(24)]
    public string Priority { get; set; } = "Normal";

    [Required, StringLength(32)]
    public string Status { get; set; } = ActionTaskStatuses.Assigned;

    public DateTime? SubmittedOn { get; set; }
    public DateTime? ClosedOn { get; set; }

    // SECTION: Closure authority metadata for reviewer and command-directed closures
    [StringLength(450)]
    public string? ClosedByUserId { get; set; }

    [StringLength(2000)]
    public string? ClosureRemarks { get; set; }

    // SECTION: Optional sprint assignment; null represents the backlog
    public int? SprintId { get; set; }
    public ActionSprint? Sprint { get; set; }

    // SECTION: Optimistic concurrency token for task updates
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public bool IsDeleted { get; set; }
}
