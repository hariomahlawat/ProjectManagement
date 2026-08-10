using System;
using System.Collections.Generic;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Pages.ActionTasks;

/// <summary>
/// Shared action-panel model for Peek and full Task Workspace. The same forms,
/// validation field names and action semantics are rendered on both surfaces;
/// only density differs through the Compact flag.
/// </summary>
public sealed class TaskActionPanelsViewModel
{
    public ActionTaskItem TaskItem { get; init; } = default!;
    public ActionTaskInteractionCapabilities Capabilities { get; init; } = ActionTaskInteractionCapabilities.None;
    public IReadOnlyList<UserOption> AssignableUsers { get; init; } = Array.Empty<UserOption>();
    public IReadOnlyList<ActionSprint> AssignableSprints { get; init; } = Array.Empty<ActionSprint>();
    public IReadOnlyList<string> PriorityOptions { get; init; } = Array.Empty<string>();
    public DateTime IstToday { get; init; }
    public bool Compact { get; init; }
    public bool IsBacklog { get; init; }
    public bool HasBacklogAssign { get; init; }
    public bool HasSprintAdd { get; init; }
    public bool CanRemoveFromSprint { get; init; }
    public bool CanMoveToBacklog { get; init; }
    public string RowVersion { get; init; } = string.Empty;

    public string UpdateStatusPostUrl { get; init; } = string.Empty;
    public string SubmitPostUrl { get; init; } = string.Empty;
    public string ReturnPostUrl { get; init; } = string.Empty;
    public string ClosePostUrl { get; init; } = string.Empty;
    public string EditTaskPostUrl { get; init; } = string.Empty;
    public string ReassignPostUrl { get; init; } = string.Empty;
    public string PriorityPostUrl { get; init; } = string.Empty;
    public string ChangeDatePostUrl { get; init; } = string.Empty;
    public string AssignBacklogPostUrl { get; init; } = string.Empty;
    public string AddSprintPostUrl { get; init; } = string.Empty;
    public string RemoveSprintPostUrl { get; init; } = string.Empty;
    public string MoveBacklogPostUrl { get; init; } = string.Empty;

    public bool CanBlock => Capabilities.CanBlockAsOwner || Capabilities.CanBlockAsCommandControl;
}
