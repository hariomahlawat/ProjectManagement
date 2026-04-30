using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public static class ActionTaskUpdateTypes
{
    public const string Progress = "Progress";
    public const string Comment = "Comment";

    public static readonly string[] All =
    {
        Progress,
        Comment
    };
}

public class ActionTaskUpdate
{
    [Key]
    public int Id { get; set; }

    public int TaskId { get; set; }

    [Required, StringLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    [Required, StringLength(32)]
    public string UpdateType { get; set; } = ActionTaskUpdateTypes.Progress;

    [Required, StringLength(4000)]
    public string Body { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
}
