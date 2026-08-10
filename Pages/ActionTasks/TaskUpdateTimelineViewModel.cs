using System;
using System.Collections.Generic;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Pages.ActionTasks;

public sealed class TaskUpdateTimelineViewModel
{
    public IReadOnlyList<ActionTaskUpdate> Updates { get; init; } = Array.Empty<ActionTaskUpdate>();
    public IReadOnlyDictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>> Attachments { get; init; }
        = new Dictionary<int, IReadOnlyList<ActionTaskAttachmentMetadata>>();
    public IReadOnlySet<int> EditedUpdateIds { get; init; } = new HashSet<int>();
    public Func<string?, string> ResolveActorName { get; init; } = _ => "System";
    public Func<ActionTaskUpdate, bool> CanEditUpdate { get; init; } = _ => false;
    public Func<ActionTaskUpdate, bool> CanDeleteUpdate { get; init; } = _ => false;
    public int TaskId { get; init; }
    public string EditPostUrl { get; init; } = string.Empty;
    public string DeletePostUrl { get; init; } = string.Empty;
    public int? Take { get; init; }
    public bool Compact { get; init; }
}
