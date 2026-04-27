using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public static class ActionTaskStatuses
{
    public const string Assigned = "Assigned";
    public const string InProgress = "In Progress";
    public const string Submitted = "Submitted";
    public const string Closed = "Closed";
    public const string Blocked = "Blocked";

    public static readonly string[] All =
    {
        Assigned,
        InProgress,
        Submitted,
        Closed,
        Blocked
    };
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

    public bool IsDeleted { get; set; }
}
