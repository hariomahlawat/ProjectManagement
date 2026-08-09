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
    public Func<string?, string> ResolveActorName { get; init; } = _ => "System";
    public int? Take { get; init; }
    public bool Compact { get; init; }
}
