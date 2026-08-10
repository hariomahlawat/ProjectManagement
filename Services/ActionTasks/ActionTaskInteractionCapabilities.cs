namespace ProjectManagement.Services.ActionTasks;

/// <summary>
/// Role- and state-aware interaction capabilities for a single task.
/// The UI consumes this projection so that valid actions stay visible without
/// exposing irrelevant workflow choices. Submitted tasks are intentionally
/// frozen for metadata/planning mutations until Command accepts or returns them.
/// </summary>
public sealed record ActionTaskInteractionCapabilities(
    bool IsAssignedUser,
    bool IsPlanningAuthority,
    bool CanAddRemark,
    bool CanAddConferenceRemark,
    bool CanStartWork,
    bool CanResumeWork,
    bool CanSubmitForClosure,
    bool CanBlockAsOwner,
    bool CanBlockAsCommandControl,
    bool CanResumeAsCommandControl,
    bool CanAcceptAndClose,
    bool CanReturnForAction,
    bool CanChangeDate,
    bool CanEditTaskDetails,
    bool CanReassignTask,
    bool CanChangePriority,
    bool CanManagePlanning,
    bool CanAssignBacklogToSprint,
    bool CanAddToSprint,
    bool CanRemoveFromSprint,
    bool CanMoveToBacklog,
    bool CanCloseDirectly,
    bool CanViewSystemHistory)
{
    public static ActionTaskInteractionCapabilities None { get; } = new(
        false, false, false, false, false, false, false, false, false, false,
        false, false, false, false, false, false, false, false, false, false,
        false, false, false);

    public bool HasPrimaryWorkflowAction =>
        CanStartWork || CanResumeWork || CanSubmitForClosure || CanBlockAsOwner ||
        CanAcceptAndClose || CanReturnForAction;

    public bool HasCommandControls =>
        CanBlockAsCommandControl || CanResumeAsCommandControl || CanChangeDate ||
        CanEditTaskDetails || CanReassignTask || CanChangePriority || CanManagePlanning || CanCloseDirectly;
}
