using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Pages.ActionTasks;

/// <summary>
/// Shared presentation model for the low-friction task action surface used by
/// both the collection Peek and the full task workspace.
/// </summary>
public sealed class TaskActionBarViewModel
{
    public ActionTaskInteractionCapabilities Capabilities { get; init; } = ActionTaskInteractionCapabilities.None;
    public bool IsClosed { get; init; }
    public bool IsBacklog { get; init; }
    public bool HasBacklogAssign { get; init; }
    public bool HasSprintAdd { get; init; }
    public bool CanRemoveFromSprint { get; init; }
    public bool CanMoveToBacklog { get; init; }
    public bool Compact { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public string UpdateStatusPostUrl { get; init; } = string.Empty;

    public bool CanBlock => Capabilities.CanBlockAsOwner || Capabilities.CanBlockAsCommandControl;
    public bool CanResume => Capabilities.CanResumeWork || Capabilities.CanResumeAsCommandControl;
    public bool ShowCloseDirectly => Capabilities.CanCloseDirectly && !Capabilities.CanAcceptAndClose;

    public bool HasAnyAction => !IsClosed && (
        Capabilities.CanStartWork
        || CanResume
        || Capabilities.CanSubmitForClosure
        || CanBlock
        || Capabilities.CanAcceptAndClose
        || Capabilities.CanReturnForAction
        || Capabilities.CanChangeDate
        || HasBacklogAssign
        || HasSprintAdd
        || CanRemoveFromSprint
        || CanMoveToBacklog
        || ShowCloseDirectly);
}
