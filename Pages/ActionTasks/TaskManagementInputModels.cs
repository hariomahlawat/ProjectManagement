using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Pages.ActionTasks;

public sealed class TaskEditInput
{
    [Required]
    public int TaskId { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;
}

public sealed class TaskReassignInput
{
    [Required]
    public int TaskId { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required]
    public string AssignedToUserId { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Remarks { get; set; } = string.Empty;
}

public sealed class TaskPriorityInput
{
    [Required]
    public int TaskId { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [Required, StringLength(24)]
    public string Priority { get; set; } = "Normal";

    [Required, StringLength(1000)]
    public string Remarks { get; set; } = string.Empty;
}
