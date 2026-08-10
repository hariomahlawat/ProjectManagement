using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Models;

namespace ProjectManagement.Pages.ActionTasks;

/// <summary>
/// Shared request contract for human-authored task remarks in both the Peek and
/// full Task workspace. Remarks are append-only collaboration records and do
/// not carry the task row-version token used by workflow mutations.
/// </summary>
public sealed class TaskRemarkInput
{
    [Range(1, int.MaxValue)]
    public int TaskId { get; set; }

    [StringLength(4000)]
    public string Body { get; set; } = string.Empty;

    [Required, StringLength(32)]
    public string UpdateType { get; set; } = ActionTaskUpdateTypes.Comment;

    public List<IFormFile> Files { get; set; } = new();
}
