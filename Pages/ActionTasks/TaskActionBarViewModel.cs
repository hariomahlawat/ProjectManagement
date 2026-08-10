using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Pages.ActionTasks;

/// <summary>
/// Shared presentation model for the low-friction task action surface used by
/// both the collection Peek and the full task workspace. Workflow/state
/// authority lives in ActionTaskWorkflowPolicy; this model only adds resource
/// availability such as whether an open sprint actually exists.
/// </summary>
public sealed class TaskActionBarViewModel
{
    public ActionTaskInteractionCapabilities Capabilities { get; init; } = ActionTaskInteractionCapabilities.None;
    public bool IsClosed { get; init; }
    public bool IsBacklog { get; init; }
    public bool HasAssignableSprint { get; init; }
    public bool HasAssignableUsers { get; init; }
    public bool CurrentSprintMutable { get; init; } = true;
    public bool Compact { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public string UpdateStatusPostUrl { get; init; } = string.Empty;

    public bool CanBlock => Capabilities.CanBlockAsOwner || Capabilities.CanBlockAsCommandControl;
    public bool CanResume => Capabilities.CanResumeWork || Capabilities.CanResumeAsCommandControl;
    public bool ShowCloseDirectly => Capabilities.CanCloseDirectly && !Capabilities.CanAcceptAndClose;
    public bool HasBacklogAssign => Capabilities.CanAssignBacklogToSprint && HasAssignableSprint && HasAssignableUsers;
    public bool HasSprintAdd => Capabilities.CanAddToSprint && HasAssignableSprint;
    public bool CanRemoveFromSprint => Capabilities.CanRemoveFromSprint && CurrentSprintMutable;
    public bool CanMoveToBacklog => Capabilities.CanMoveToBacklog && CurrentSprintMutable;

    public bool HasAnyAction => !IsClosed && (
        Capabilities.CanStartWork
        || CanResume
        || Capabilities.CanSubmitForClosure
        || CanBlock
        || Capabilities.CanAcceptAndClose
        || Capabilities.CanReturnForAction
        || Capabilities.CanEditTaskDetails
        || Capabilities.CanReassignTask
        || Capabilities.CanChangePriority
        || Capabilities.CanChangeDate
        || HasBacklogAssign
        || HasSprintAdd
        || CanRemoveFromSprint
        || CanMoveToBacklog
        || ShowCloseDirectly);
}
